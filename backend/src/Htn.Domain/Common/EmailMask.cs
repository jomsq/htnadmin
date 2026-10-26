namespace Htn.Domain.Common;

/// <summary>Masks an email for lists and logs: first character, "•••", then "@domain" ("m•••@hackthenorth.ph").</summary>
public static class EmailMask
{
    public const string Dots = "•••";

    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return Dots;
        var value = email.Trim();
        var at = value.LastIndexOf('@');
        if (at < 0) return value[..1] + Dots;
        var domain = value[at..];
        return at == 0 ? Dots + domain : value[..1] + Dots + domain;
    }
}
