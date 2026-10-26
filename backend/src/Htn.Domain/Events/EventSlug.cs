using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Htn.Domain.Events;

/// <summary>Public URL slug of an event (<c>/r/{slug}</c>): lower-case letters and digits in dash-separated words, 3–60 characters.</summary>
public static partial class EventSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 60;
    public const string Pattern = "^[a-z0-9]+(-[a-z0-9]+)*$";

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    public static bool IsValid(string? slug) =>
        slug is { Length: >= MinLength and <= MaxLength } && SlugRegex().IsMatch(slug);

    /// <summary>
    /// "HTN Con 2026" → "htn-con-2026". Accents are dropped ("Señor" → "senor"), anything else that isn't a letter or
    /// digit becomes a dash. Too short results fall back to "event…", too long ones are cut at a word boundary.
    /// </summary>
    public static string FromName(string name)
    {
        var source = name ?? "";
        var sb = new StringBuilder(source.Length);
        var dash = false;
        foreach (var ch in source)
        {
            // The app runs with InvariantGlobalization, so string.Normalize can't strip accents; fold them by table.
            var lower = char.ToLowerInvariant(ch);
            var folded = Fold.TryGetValue(lower, out var f) ? f : lower.ToString();
            if (folded.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'))
            {
                sb.Append(folded);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength];
            var cut = slug.LastIndexOf('-');
            slug = cut >= MinLength ? slug[..cut] : slug.TrimEnd('-');
        }
        if (slug.Length < MinLength) slug = slug.Length == 0 ? "event" : $"{slug}-event";
        return slug;
    }

    /// <summary>Accented Latin letters (Filipino and Spanish names, mostly) to their plain ASCII spelling.</summary>
    private static readonly Dictionary<char, string> Fold = BuildFold();

    private static Dictionary<char, string> BuildFold()
    {
        var map = new Dictionary<char, string>();
        void Add(string chars, string plain)
        {
            foreach (var c in chars) map[c] = plain;
        }
        Add("àáâãäåāăą", "a");
        Add("çćĉċč", "c");
        Add("ďđ", "d");
        Add("èéêëēĕėęě", "e");
        Add("ĝğġģ", "g");
        Add("ĥħ", "h");
        Add("ìíîïĩīĭįı", "i");
        Add("ĵ", "j");
        Add("ķ", "k");
        Add("ĺļľŀł", "l");
        Add("ñńņňŉ", "n");
        Add("òóôõöøōŏő", "o");
        Add("ŕŗř", "r");
        Add("śŝşš", "s");
        Add("ţťŧ", "t");
        Add("ùúûüũūŭůűų", "u");
        Add("ŵ", "w");
        Add("ýÿŷ", "y");
        Add("źżž", "z");
        map['æ'] = "ae";
        map['œ'] = "oe";
        map['ß'] = "ss";
        map['þ'] = "th";
        map['ð'] = "d";
        return map;
    }

    /// <summary>"htn-con-2026" with suffix 2 → "htn-con-2026-2", shortened so the result still fits <see cref="MaxLength"/>.</summary>
    public static string WithSuffix(string slug, int n)
    {
        var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
        var head = slug.Length + suffix.Length > MaxLength ? slug[..(MaxLength - suffix.Length)].TrimEnd('-') : slug;
        return head + suffix;
    }
}
