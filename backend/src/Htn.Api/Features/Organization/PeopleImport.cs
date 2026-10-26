using System.Net.Mail;
using System.Text;
using Htn.Domain.Organization;

namespace Htn.Api.Features.Organization;

/// <summary>Minimal RFC 4180 CSV reader: comma separated, double-quoted fields with "" escapes, CRLF or LF line ends.</summary>
public static class Csv
{
    /// <exception cref="FormatException">A quoted field isn't closed.</exception>
    public static List<string[]> Parse(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"' when !fieldStarted:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break; // handled by the \n
                case '\n':
                case '\r':
                    fields.Add(field.ToString());
                    records.Add([.. fields]);
                    fields.Clear();
                    field.Clear();
                    fieldStarted = false;
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (inQuotes) throw new FormatException("A quoted value isn't closed.");
        if (fieldStarted || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add([.. fields]);
        }
        return records;
    }
}

/// <summary>Parses and checks a roster CSV for POST /api/people/import. Database checks (existing emails) are added by the endpoint.</summary>
public static class PeopleImport
{
    public const long MaxBytes = 1024 * 1024;
    public const int MaxRows = 500;
    public const string Header = "first_name,middle_name,last_name,email,level,position";

    private static readonly string[] Known = ["first_name", "middle_name", "last_name", "email", "level", "position"];
    private static readonly string[] Required = ["first_name", "last_name", "email"];

    /// <summary>Levels allowed in an import. Board seats are never imported.</summary>
    public static readonly PersonLevel[] ImportableLevels = [PersonLevel.Member, PersonLevel.Apprentice, PersonLevel.Goon];

    public sealed class Row
    {
        public required int Number { get; init; }
        public required string FirstName { get; init; }
        public required string? MiddleName { get; init; }
        public required string LastName { get; init; }
        public required string Email { get; init; }
        public required string LevelText { get; init; }
        public required PersonLevel? Level { get; init; }
        public required string? Position { get; init; }
        public List<string> Errors { get; } = [];

        public ImportRowDto ToDto() => new(Number, FirstName, LastName, Email, Level?.ToString() ?? LevelText, Errors);
    }

    /// <summary>Returns the parsed rows, or a file-level error message (bad header, too many rows...).</summary>
    public static (List<Row>? Rows, string? FileError) Read(byte[] bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (null, "Save the file as UTF-8 CSV and try again.");
        }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        if (text.Contains('\0')) return (null, "This doesn't look like a CSV file.");

        List<string[]> records;
        try
        {
            records = Csv.Parse(text);
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }
        if (records.Count == 0) return (null, $"The file is empty. The first row must be: {Header}");

        var header = records[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var unknown = header.Where(h => !Known.Contains(h)).ToArray();
        if (unknown.Length > 0)
            return (null, $"Unknown column \"{Truncate(unknown[0], 40)}\". The first row must be: {Header}");
        if (header.Distinct().Count() != header.Length) return (null, "A column appears twice in the first row.");
        var missing = Required.Where(r => !header.Contains(r)).ToArray();
        if (missing.Length > 0) return (null, $"Missing column \"{missing[0]}\". The first row must be: {Header}");

        var index = header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        var rows = new List<Row>();
        for (var r = 1; r < records.Count; r++)
        {
            var cells = records[r];
            if (cells.All(string.IsNullOrWhiteSpace)) continue; // blank line
            if (rows.Count == MaxRows) return (null, $"The file has more than {MaxRows} rows. Split it and import each part.");

            string Raw(string column) => index.TryGetValue(column, out var i) && i < cells.Length ? cells[i] : "";
            var levelText = Raw("level").Trim();
            var row = new Row
            {
                Number = r + 1, // the header is row 1
                FirstName = Raw("first_name").Trim(),
                MiddleName = NullIfEmpty(Raw("middle_name")),
                LastName = Raw("last_name").Trim(),
                Email = Person.NormalizeEmail(Raw("email")),
                LevelText = levelText,
                Level = ParseLevel(levelText),
                Position = NullIfEmpty(Raw("position")),
            };

            if (cells.Length > header.Length) row.Errors.Add("This row has more values than there are columns.");
            foreach (var column in Known)
                if (IsFormula(Raw(column)))
                    row.Errors.Add($"{Label(column)} can't start with =, +, - or @.");

            Check(row.FirstName, "First name", Person.NameMaxLength, required: true, row.Errors);
            Check(row.MiddleName, "Middle name", Person.NameMaxLength, required: false, row.Errors);
            Check(row.LastName, "Last name", Person.NameMaxLength, required: true, row.Errors);
            Check(row.Position, "Position", Person.PositionMaxLength, required: false, row.Errors);
            if (row.Email.Length == 0) row.Errors.Add("Email is required.");
            else if (row.Email.Length > Person.EmailMaxLength) row.Errors.Add($"Email must be at most {Person.EmailMaxLength} characters.");
            else if (!IsEmail(row.Email)) row.Errors.Add("Email isn't valid.");

            if (row.Level is null)
                row.Errors.Add(Enum.TryParse<PersonLevel>(levelText, ignoreCase: true, out var l) && l == PersonLevel.Board
                    ? "Board members can't be imported. Add them one by one."
                    : "Level must be Member, Apprentice or Goon.");

            rows.Add(row);
        }

        if (rows.Count == 0) return (null, "The file has no people in it.");

        foreach (var dup in rows.Where(r => r.Email.Length > 0).GroupBy(r => r.Email).Where(g => g.Count() > 1))
            foreach (var row in dup.Skip(1))
                row.Errors.Add($"Email appears more than once in the file (first on row {dup.First().Number}).");

        return (rows, null);
    }

    /// <summary>Spreadsheet formula injection: values that start with = + - @ (or a tab/CR that hides one) are refused.</summary>
    public static bool IsFormula(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value[0] is '\t' or '\r') return true;
        var t = value.TrimStart();
        return t.Length > 0 && t[0] is '=' or '+' or '-' or '@';
    }

    private static PersonLevel? ParseLevel(string text)
    {
        if (text.Length == 0) return PersonLevel.Member;
        return Enum.TryParse<PersonLevel>(text, ignoreCase: true, out var level) && Enum.IsDefined(level) && ImportableLevels.Contains(level)
            && !int.TryParse(text, out _)
            ? level
            : null;
    }

    private static bool IsEmail(string email) =>
        MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && email.IndexOf('@') is > 0 && !email.EndsWith('@');

    private static void Check(string? value, string label, int max, bool required, List<string> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (required) errors.Add($"{label} is required.");
            return;
        }
        if (value.Length > max) errors.Add($"{label} must be at most {max} characters.");
    }

    private static string Label(string column) => column switch
    {
        "first_name" => "First name",
        "middle_name" => "Middle name",
        "last_name" => "Last name",
        "email" => "Email",
        "level" => "Level",
        _ => "Position",
    };

    private static string? NullIfEmpty(string value)
    {
        var v = value.Trim();
        return v.Length == 0 ? null : v;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
