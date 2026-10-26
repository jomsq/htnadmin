using System.Text.RegularExpressions;
using Htn.Domain.Common;

namespace Htn.Domain.Registrations;

/// <summary>The fixed options and limits of the registration form (frame 10), and the Data Privacy Act consent text.</summary>
public static partial class RegistrationForm
{
    public const int NameMaxLength = 80;
    public const int EmailMaxLength = 254;
    public const int OccupationMaxLength = 120;
    public const int CompanyMaxLength = 160;
    public const int ReasonMaxLength = 500;
    public const int ReferenceMaxLength = 60;

    public static readonly IReadOnlyList<string> Genders =
        ["Female", "Male", "Non-binary", "Prefer to self-describe", "Prefer not to say"];

    public static readonly IReadOnlyList<string> HearAbout =
        ["Facebook", "LinkedIn", "Friend or colleague", "School or organization", "Sponsor", "Other"];

    public static readonly IReadOnlyList<string> ShirtSizes = ["XS", "S", "M", "L", "XL", "2XL"];

    /// <summary>Bump the version whenever <see cref="ConsentText"/> changes; each registration keeps the version it agreed to.</summary>
    public const string ConsentVersion = "2026-10-dpa-v1";

    public const string ConsentText =
        "I agree to the event terms and the processing of my data under the Data Privacy Act of 2012.";

    /// <summary>Occupation and company: as names, plus digits and &amp; , ( ) / (e.g. "DICT Region 1", "R&amp;D (IT/Security)").</summary>
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{M}\p{N} .'\u2019&,()/-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex OrgTextRegex();

    /// <summary>Person names follow <see cref="PersonNameRules"/>, shared with event volunteer names.</summary>
    public const string NameMessage = PersonNameRules.Message;
    public const string OrgTextMessage = "Use letters, numbers, spaces and . , ' - & ( ) / only.";

    /// <summary>A trimmed person name that passes the character rules (length is checked separately).</summary>
    public static bool IsValidName(string? value) => PersonNameRules.IsValidName(value);

    /// <summary>A trimmed occupation or company that passes the character rules (length is checked separately).</summary>
    public static bool IsValidOrgText(string? value) =>
        value is null || (OrgTextRegex().IsMatch(value.Trim()) && !PersonNameRules.HasLinkOrMarkup(value) && !SlashTouchesDottedToken(value));

    /// <summary>Org text: a '/' touching a dotted token ("Inc./Ltd", "x./y") reads like a path or link.</summary>
    private static bool SlashTouchesDottedToken(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Contains('/') && token.Split('/').Any(part => part.Contains('.')));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    public static bool IsEmail(string? value) =>
        value is { Length: > 0 and <= EmailMaxLength } && EmailRegex().IsMatch(value.Trim());
}

/// <summary>Philippine mobile numbers, stored as <c>+639XXXXXXXXX</c>.</summary>
public static partial class MobileNumber
{
    public const string Pattern = @"^\+639[0-9]{9}$";

    [GeneratedRegex(@"^\+639[0-9]{9}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Normalized();

    /// <summary>
    /// "9171234567", "09171234567", "639171234567" and "+63 917 123 4567" all become "+639171234567". Spaces, dashes,
    /// dots and brackets are ignored. Returns null when the result is not a +63 mobile number.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 30) return null;
        var plus = input.TrimStart().StartsWith('+');
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (input.Any(c => !(char.IsAsciiDigit(c) || c is ' ' or '-' or '.' or '(' or ')' or '+'))) return null;
        string? local = digits switch
        {
            { Length: 10 } when !plus && digits[0] == '9' => digits,
            { Length: 11 } when !plus && digits.StartsWith("09", StringComparison.Ordinal) => digits[1..],
            { Length: 12 } when digits.StartsWith("639", StringComparison.Ordinal) => digits[2..],
            _ => null,
        };
        if (local is null) return null;
        var result = "+63" + local;
        return Normalized().IsMatch(result) ? result : null;
    }
}
