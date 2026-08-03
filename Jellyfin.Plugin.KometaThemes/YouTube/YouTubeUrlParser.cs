using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Validates YouTube links and reduces them to a canonical video ID.
/// </summary>
/// <remarks>
/// The important property here is that the user-supplied string never reaches the extractor. Only
/// the extracted 11-character video ID does, embedded in a URL this class builds itself. That closes
/// off argument injection (an ID cannot start with <c>-</c> or contain whitespace), SSRF via a
/// lookalike host, and redirect-following into an internal address — none of which a plain host
/// allowlist would fully prevent.
/// </remarks>
public static class YouTubeUrlParser
{
    /// <summary>
    /// YouTube video IDs are exactly 11 characters from the URL-safe base64 alphabet.
    /// </summary>
    private static readonly Regex VideoIdRegex = new(
        "^[A-Za-z0-9_-]{11}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Attempts to extract a YouTube video ID from a user-supplied string.
    /// </summary>
    /// <param name="value">A YouTube URL, or a bare video ID.</param>
    /// <param name="videoId">The extracted video ID.</param>
    /// <returns>Whether a valid video ID could be extracted.</returns>
    public static bool TryGetVideoId(string? value, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // A bare ID pasted on its own is accepted.
        if (VideoIdRegex.IsMatch(trimmed))
        {
            videoId = trimmed;
            return true;
        }

        // Tolerate a missing scheme ("youtu.be/xyz") without ever accepting a non-HTTP one.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Credentials in the URL are never legitimate here.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var candidate = host switch
        {
            "youtu.be" => FirstPathSegment(uri),
            "youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com"
                => ExtractFromYouTubeCom(uri),
            _ => null
        };

        if (candidate != null && VideoIdRegex.IsMatch(candidate))
        {
            videoId = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the canonical watch URL for a validated video ID.
    /// </summary>
    /// <param name="videoId">A video ID previously validated by <see cref="TryGetVideoId"/>.</param>
    /// <returns>The canonical watch URL.</returns>
    /// <exception cref="ArgumentException">Thrown when the ID is not a valid YouTube video ID.</exception>
    public static string BuildWatchUrl(string videoId)
    {
        if (!IsValidVideoId(videoId))
        {
            throw new ArgumentException("Not a valid YouTube video ID.", nameof(videoId));
        }

        return string.Create(CultureInfo.InvariantCulture, $"https://www.youtube.com/watch?v={videoId}");
    }

    /// <summary>
    /// Checks whether a string is a well-formed YouTube video ID.
    /// </summary>
    /// <param name="videoId">Candidate ID.</param>
    /// <returns>Whether the candidate is a valid video ID.</returns>
    public static bool IsValidVideoId(string? videoId)
        => !string.IsNullOrEmpty(videoId) && VideoIdRegex.IsMatch(videoId);

    private static string? ExtractFromYouTubeCom(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // /watch?v=ID
        if (segments.Length >= 1 && string.Equals(segments[0], "watch", StringComparison.OrdinalIgnoreCase))
        {
            return GetQueryValue(uri.Query, "v");
        }

        // /shorts/ID, /embed/ID, /v/ID, /live/ID
        if (segments.Length >= 2 &&
            (string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "v", StringComparison.OrdinalIgnoreCase)))
        {
            return segments[1];
        }

        // Some share links carry the id only in the query.
        return GetQueryValue(uri.Query, "v");
    }

    private static string? FirstPathSegment(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[0] : null;
    }

    /// <summary>
    /// Minimal query reader. Avoids a dependency on ASP.NET query parsing so this class stays
    /// trivially unit-testable.
    /// </summary>
    private static string? GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(pair[..separator], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
