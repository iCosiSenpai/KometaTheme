using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Resolver that searches for anime by title using fuzzy matching.
/// Used as a fallback when no external IDs are available.
/// </summary>
public class TitleSearchResolver
{
    private const int MinimumFallbackScore = 62;
    private const int ExactMatchScore = 100;

    /// <summary>
    /// Subtracted when the searched title names a season the candidate does not.
    /// Large enough to push an otherwise perfect token match below the confident threshold.
    /// </summary>
    private const int SeasonMismatchScorePenalty = 26;

    private readonly AnimeThemesApi _api;
    private readonly IResolutionCache _cache;
    private readonly ILogger<TitleSearchResolver> _logger;

    private static readonly Regex CodecResolutionRegex = new(@"\b(10[0-9]{2}p|2160p|4[Kk]|8[Kk])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodecVideoRegex = new(@"\b([xXhH]2[0-9]{2}|[Hh]\.?2[0-9]{2}|HEVC|AV1|AVC|MPEG-\d)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodecAudioRegex = new(@"\b(AAC|AC3|EAC3|DTS|FLAC|OPUS|MP3|AAC\d?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LanguageTagRegex = new(@"\b(Subs?\s*)?(iTA|ITA|ENG|JAP|JPN|MULTi?)(\s*Subs?)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SourceTagRegex = new(@"\b(BDRip|BRRip|WEBRip|WEB-DL|WEB|BluRay|BLURAY|HDRip|DVDRip)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketInfoRegex = new(@"\[.*?\]|\(.*?(?:H26[45]|x26[45]|HEVC|AV1|AAC|AC3|FLAC|BD|WEB|A Mux).*?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SeasonOrdinalRegex = new(
        @"\b(?:season|series|cour|part|stagione)\s*(?:(?<num>\d{1,2})|(?<roman>I{1,3}|IV|VI{0,3}|IX|X))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SearchNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "cour",
        "episode",
        "movie",
        "no",
        "ova",
        "part",
        "season",
        "special",
        "the",
        "tv"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleSearchResolver"/> class.
    /// </summary>
    /// <param name="api">AnimeThemes API instance.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="logger">Logger.</param>
    public TitleSearchResolver(AnimeThemesApi api, IResolutionCache cache, ILogger<TitleSearchResolver> logger)
    {
        _api = api;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves items by searching for them by title.
    /// </summary>
    /// <param name="items">Items to resolve.</param>
    /// <param name="threshold">Minimum Levenshtein similarity ratio (0.0-1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of resolved items.</returns>
    public async IAsyncEnumerable<ItemWithAnime> ResolveAsync(
        BaseItem[] items,
        double threshold,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var searchKeys = GetSearchKeys(item);
            if (searchKeys.Count == 0)
            {
                continue;
            }

            var year = item.ProductionYear;
            var primaryTitle = searchKeys[0].Value;
            var cacheKey = $"title2:{string.Join("|", searchKeys.Select(key => key.Normalized))}:{year}";

            if (_cache.TryGet(cacheKey, out var cached))
            {
                if (cached != null && cached.Length > 0)
                {
                    yield return new ItemWithAnime(item, new ReadOnlyCollection<Anime>(cached));
                }

                continue;
            }

            Anime? bestMatch = null;
            var bestScore = 0;
            var bestQuery = primaryTitle;

            foreach (var query in searchKeys.Select(key => key.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Anime[] results;
                try
                {
                    results = await _api.SearchByTitleAsync(query, year, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TitleSearchResolver: API search failed for '{Title}'. Skipping query.", query);
                    continue;
                }

                foreach (var anime in results)
                {
                    var score = ScoreSearchCandidate(anime, searchKeys, year);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = anime;
                        bestQuery = query;
                    }
                }

                if (bestScore >= ExactMatchScore)
                {
                    break;
                }
            }

            var requiredScore = Math.Clamp((int)Math.Round(threshold * 100, MidpointRounding.AwayFromZero), MinimumFallbackScore, ExactMatchScore);
            if (bestMatch != null && bestScore >= requiredScore)
            {
                var hydratedMatch = await HydrateMatchAsync(bestMatch, cancellationToken).ConfigureAwait(false);
                if (hydratedMatch == null)
                {
                    _logger.LogDebug("Title match for '{Title}' could not be hydrated from AnimeThemes.", primaryTitle);
                    _cache.SetNegative(cacheKey);
                    continue;
                }

                _logger.LogInformation(
                    "Title match found for '{Title}' via '{Query}': '{Match}' (score={Score}, required={Required})",
                    primaryTitle,
                    bestQuery,
                    hydratedMatch.Name,
                    bestScore,
                    requiredScore);

                var matchArray = new[] { hydratedMatch };
                _cache.SetPositive(cacheKey, matchArray);
                yield return new ItemWithAnime(item, new ReadOnlyCollection<Anime>(matchArray));
            }
            else
            {
                _logger.LogDebug(
                    "No title match found for '{Title}' (best score={Score}, required={Required})",
                    primaryTitle,
                    bestScore,
                    requiredScore);

                _cache.SetNegative(cacheKey);
            }
        }
    }

    private async ValueTask<Anime?> HydrateMatchAsync(Anime match, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(match.Slug))
        {
            var anime = await _api.GetAnimeBySlugAsync(match.Slug, cancellationToken).ConfigureAwait(false);
            if (anime != null)
            {
                return anime;
            }
        }

        return await _api.GetAnimeByIdAsync(match.Id, cancellationToken).ConfigureAwait(false);
    }

    private static List<SearchKey> GetSearchKeys(BaseItem item)
    {
        var keys = new List<SearchKey>();

        AddSearchKey(keys, item.Name);
        AddSearchKey(keys, item.OriginalTitle);
        AddSearchKey(keys, GetProviderValue(item, "TvdbSlug"));
        AddSearchKey(keys, ExtractAnimeClickSlug(GetProviderValue(item, "AnimeClick")));

        return keys;
    }

    /// <summary>
    /// Removes codec/quality/resolution patterns from a title that may be present
    /// when Jellyfin stores a raw filename as the item name.
    /// </summary>
    private static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var cleaned = CodecResolutionRegex.Replace(title, string.Empty);
        cleaned = CodecVideoRegex.Replace(cleaned, string.Empty);
        cleaned = CodecAudioRegex.Replace(cleaned, string.Empty);
        cleaned = LanguageTagRegex.Replace(cleaned, string.Empty);
        cleaned = SourceTagRegex.Replace(cleaned, string.Empty);
        cleaned = BracketInfoRegex.Replace(cleaned, string.Empty);
        cleaned = MultiSpaceRegex.Replace(cleaned, " ");
        cleaned = cleaned.Trim();

        return cleaned.Length > 0 ? cleaned : title.Trim();
    }

    private static string? GetProviderValue(BaseItem item, string key)
    {
        return item.ProviderIds != null && item.ProviderIds.TryGetValue(key, out var value) ? value : null;
    }

    private static string? ExtractAnimeClickSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < trimmed.Length - 1)
        {
            trimmed = trimmed[(lastSlash + 1)..];
        }

        var index = 0;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        while (index < trimmed.Length && (trimmed[index] == '-' || trimmed[index] == '_' || trimmed[index] == '.'))
        {
            index++;
        }

        return index < trimmed.Length ? trimmed[index..] : trimmed;
    }

    private static void AddSearchKey(List<SearchKey> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var cleaned = CleanTitle(value);
        var normalized = NormalizeSearchText(cleaned);
        if (normalized.Length == 0 || keys.Any(key => key.Normalized == normalized))
        {
            return;
        }

        keys.Add(new SearchKey(cleaned, normalized, TokenizeSearchText(cleaned)));
    }

    private static int ScoreSearchCandidate(Anime anime, List<SearchKey> searchKeys, int? year)
    {
        var candidateName = NormalizeSearchText(anime.Name);
        var candidateSlug = NormalizeSearchText(anime.Slug);
        var candidateTokens = TokenizeSearchText(string.Create(CultureInfo.InvariantCulture, $"{anime.Name} {anime.Slug}"));
        var bestScore = 0;

        foreach (var key in searchKeys)
        {
            var score = 0;
            if (candidateName == key.Normalized || candidateSlug == key.Normalized)
            {
                score = ExactMatchScore;
            }
            else
            {
                var coverage = CalculateTokenCoverage(key.Tokens, candidateTokens);
                var orderedCoverage = CalculateOrderedTokenCoverage(key.Tokens, candidateTokens);
                if (coverage >= 0.95)
                {
                    score = 86;
                }
                else if (coverage >= 0.80 && orderedCoverage >= 0.60)
                {
                    score = 74;
                }
                else if (coverage >= 0.65 && orderedCoverage >= 0.50)
                {
                    score = 62;
                }
                else if (key.Normalized.Length >= 8 && (candidateName.Contains(key.Normalized, StringComparison.Ordinal) || candidateSlug.Contains(key.Normalized, StringComparison.Ordinal)))
                {
                    score = 68;
                }
                else if (candidateName.Length >= 8 && key.Normalized.StartsWith(candidateName, StringComparison.Ordinal))
                {
                    score = ScorePrefixMatch(key.Normalized, candidateName);
                }
                else if (candidateSlug.Length >= 8 && key.Normalized.StartsWith(candidateSlug, StringComparison.Ordinal))
                {
                    score = ScorePrefixMatch(key.Normalized, candidateSlug);
                }
            }

            if (year.HasValue && anime.Year.HasValue)
            {
                var yearDelta = Math.Abs(anime.Year.Value - year.Value);
                if (yearDelta == 0)
                {
                    score += 8;
                }
                else if (yearDelta == 1)
                {
                    score += 3;
                }
                else if (yearDelta > 3)
                {
                    score -= 8;
                }
            }

            score -= SeasonMismatchPenalty(key.Value, anime.Name, anime.Slug);

            bestScore = Math.Max(bestScore, score);
        }

        return Math.Max(0, bestScore);
    }

    /// <summary>
    /// Scores a match where the candidate's title is a prefix of the searched title.
    /// </summary>
    /// <remarks>
    /// This bucket used to be a flat 80, which is at or above the default confidence threshold. That
    /// made "Fullmetal Alchemist Brotherhood" confidently match the *different* show "Fullmetal
    /// Alchemist", and the wrong match was then cached positively for days with no per-item way to
    /// invalidate it. A prefix match is only strong evidence when what remains is trivial; a
    /// substantial leftover usually means the candidate is the parent series, a different season or
    /// another entry in the franchise, so it now scores below the confident threshold and is
    /// surfaced as a weak suggestion instead of being bound automatically.
    /// </remarks>
    /// <param name="query">Normalized searched title.</param>
    /// <param name="candidate">Normalized candidate title.</param>
    /// <returns>A score for this match.</returns>
    private static int ScorePrefixMatch(string query, string candidate)
    {
        var leftover = query.Length - candidate.Length;
        return leftover <= 2 ? 80 : MinimumFallbackScore;
    }

    /// <summary>
    /// Penalty for a season/part ordinal present on one side and absent or different on the other.
    /// </summary>
    /// <remarks>
    /// <c>season</c>, <c>part</c> and <c>cour</c> are in the noise-word list, so tokenizing
    /// "Attack on Titan Season 2" and "Attack on Titan" produced identical token sets and a
    /// coverage of 1.0 — a confident match onto the wrong season. The ordinal is exactly the
    /// discriminator that was being thrown away, so it is compared separately here.
    /// </remarks>
    /// <param name="query">Raw searched title.</param>
    /// <param name="candidateName">Candidate anime name.</param>
    /// <param name="candidateSlug">Candidate anime slug.</param>
    /// <returns>The penalty to subtract from the score.</returns>
    private static int SeasonMismatchPenalty(string? query, string? candidateName, string? candidateSlug)
    {
        var queryOrdinal = ExtractSeasonOrdinal(query);
        if (queryOrdinal == null)
        {
            return 0;
        }

        var candidateOrdinal = ExtractSeasonOrdinal(candidateName) ?? ExtractSeasonOrdinal(candidateSlug);

        // A candidate with no ordinal at all is most likely season 1 / the parent entry.
        if (candidateOrdinal == null)
        {
            return queryOrdinal.Value == 1 ? 0 : SeasonMismatchScorePenalty;
        }

        return candidateOrdinal.Value == queryOrdinal.Value ? 0 : SeasonMismatchScorePenalty;
    }

    /// <summary>
    /// Extracts an explicit season/part/cour ordinal from a title, in digits or roman numerals.
    /// </summary>
    /// <param name="value">Title to inspect.</param>
    /// <returns>The ordinal, or null when the title carries none.</returns>
    internal static int? ExtractSeasonOrdinal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SeasonOrdinalRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var digits = match.Groups["num"].Value;
        if (digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed is > 0 and <= 50 ? parsed : null;
        }

        var roman = match.Groups["roman"].Value;
        return roman.Length > 0 ? RomanToInt(roman) : null;
    }

    private static int? RomanToInt(string roman)
    {
        return roman.ToUpperInvariant() switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            "X" => 10,
            _ => null
        };
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string[] TokenizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var tokens = new List<string>();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            AddToken(tokens, builder);
        }

        AddToken(tokens, builder);
        return tokens.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length > 1 && !SearchNoiseWords.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static double CalculateTokenCoverage(string[] expectedTokens, string[] candidateTokens)
    {
        if (expectedTokens.Length == 0)
        {
            return 0;
        }

        var candidateSet = new HashSet<string>(candidateTokens, StringComparer.Ordinal);
        var matches = expectedTokens.Count(candidateSet.Contains);
        return (double)matches / expectedTokens.Length;
    }

    private static double CalculateOrderedTokenCoverage(string[] expectedTokens, string[] candidateTokens)
    {
        if (expectedTokens.Length == 0)
        {
            return 0;
        }

        var expectedIndex = 0;
        foreach (var candidateToken in candidateTokens)
        {
            if (expectedIndex < expectedTokens.Length && string.Equals(candidateToken, expectedTokens[expectedIndex], StringComparison.Ordinal))
            {
                expectedIndex++;
            }
        }

        return (double)expectedIndex / expectedTokens.Length;
    }

    /// <summary>
    /// Calculates the Levenshtein similarity ratio between two strings.
    /// Returns a value between 0.0 (completely different) and 1.0 (identical).
    /// </summary>
    /// <param name="s1">First string to compare.</param>
    /// <param name="s2">Second string to compare.</param>
    /// <returns>A similarity ratio between 0.0 and 1.0.</returns>
    internal static double CalculateSimilarity(string s1, string s2)
    {
        if (string.Equals(s1, s2, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0)
        {
            return 1.0;
        }

        var distance = LevenshteinDistance(s1, s2);
        return 1.0 - ((double)distance / maxLen);
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;

        // Use two single-dimensional arrays instead of 2D array (CA1814)
        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }

            // Swap arrays
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    private sealed record SearchKey(string Value, string Normalized, string[] Tokens);
}
