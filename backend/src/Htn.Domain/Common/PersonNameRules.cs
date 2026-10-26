using System.Text.RegularExpressions;

namespace Htn.Domain.Common;

/// <summary>
/// The character rules for a person's name, shared by attendee names (<c>RegistrationForm</c>) and event volunteer
/// names. Keeping them in one place means a name that is refused on the public registration form is also refused when
/// an organizer types it on the Committees screen.
/// </summary>
public static partial class PersonNameRules
{
    public const string Message = "Use letters, spaces, periods, commas, apostrophes and hyphens only.";

    /// <summary>
    /// Unicode letters and combining marks, spaces, and . , ' ’ - (e.g. "Ma. Niña Dela Cruz-Ñañez", "O'Brien",
    /// "Santos, Jr."). Must start with a letter. Rules out links, emails and markup (no ':', '/', '@', '&lt;', '&gt;',
    /// digits or control characters).
    /// </summary>
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M} .,'’-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    /// <summary>A trimmed person name that passes the character rules (length is checked separately).</summary>
    public static bool IsValidName(string? value) => value is null || (NameRegex().IsMatch(value.Trim()) && !HasLinkOrMarkup(value));

    /// <summary>Shared by names and org text: anything that reads like a URL, an email address or markup.</summary>
    public static bool HasLinkOrMarkup(string value) =>
        value.Contains("://", StringComparison.Ordinal) || value.IndexOfAny(['@', '<', '>']) >= 0 || value.Any(char.IsControl)
        || DotJoinsWord().IsMatch(value);

    /// <summary>
    /// A '.' directly followed by a letter or digit ("gcash-ph.com", "Ma.Niña"): domain-like. An abbreviation is followed
    /// by a space, comma, hyphen or the end ("Ma. Niña", "Santos, Jr.", "St. Luke's").
    /// </summary>
    [GeneratedRegex(@"\.[\p{L}\p{N}]", RegexOptions.CultureInvariant)]
    private static partial Regex DotJoinsWord();
}
