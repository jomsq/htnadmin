namespace Htn.Infrastructure.Data.Configurations;

/// <summary>Builds SQL for check constraints on string-stored statuses/enums.</summary>
internal static class CheckConstraints
{
    public static string In(string column, params string[] values) =>
        $"{column} IN ({string.Join(", ", values.Select(v => $"'{v.Replace("'", "''")}'"))})";
}
