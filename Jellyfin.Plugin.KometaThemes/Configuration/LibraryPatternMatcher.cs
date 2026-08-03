using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// Builds the library-name matcher from the user's <c>LibraryPattern</c> setting.
/// </summary>
/// <remarks>
/// The pattern comes from a free-text configuration field and was previously compiled with
/// <c>new Regex(pattern, RegexOptions.IgnoreCase)</c> in three separate places, with no match
/// timeout. A pathological pattern — the classic nested-quantifier shape is easy to type by accident
/// — could therefore back-track for an unbounded time on a library name, once per item, inside a
/// scheduled task. A timeout plus a single construction point fixes both the exposure and the
/// duplication, and an invalid pattern now degrades to a plain substring match instead of throwing
/// out of whichever call site happened to compile it first.
/// </remarks>
public static class LibraryPatternMatcher
{
    /// <summary>
    /// Default pattern used when the setting is empty.
    /// </summary>
    public const string DefaultPattern = "Anime";

    /// <summary>
    /// Upper bound on a single match attempt.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Creates a predicate that decides whether a library name is included.
    /// </summary>
    /// <param name="pattern">The configured pattern.</param>
    /// <returns>A predicate over library names. Never throws for a bad pattern.</returns>
    public static Func<string?, bool> Create(string? pattern)
    {
        var effective = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern;

        Regex regex;
        try
        {
            regex = new Regex(effective, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            // Not a valid expression: treat it as literal text, which is what a user typing a plain
            // library name expects anyway.
            return name => name != null && name.Contains(effective, StringComparison.OrdinalIgnoreCase);
        }

        return name =>
        {
            if (name == null)
            {
                return false;
            }

            try
            {
                return regex.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pattern that cannot be evaluated in time must not silently include everything.
                return false;
            }
        };
    }
}
