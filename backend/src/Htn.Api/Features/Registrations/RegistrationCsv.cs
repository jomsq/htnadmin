using System.Globalization;
using System.Text;
using Htn.Domain.Registrations;

namespace Htn.Api.Features.Registrations;

/// <summary>One row per attendee. RFC 4180 quoting plus formula-injection escaping (see <see cref="Cell"/>).</summary>
public static class RegistrationCsv
{
    public static readonly string[] Header =
    [
        "Participant ID", "First name", "Middle name", "Last name", "Email", "Mobile", "Gender", "Occupation", "Agency / company",
        "Shirt size", "Heard about", "Ticket", "Buyer", "Status", "Overdue", "Subtotal", "Discount", "Total", "Voucher",
        "Payment method", "Reference", "Registered at", "Paid at", "Source",
    ];

    public static string Build(
        IEnumerable<Registration> regs, Func<Registration, string> ticketName, Func<Registration, Payment?> payment, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        Line(sb, Header);
        foreach (var r in regs)
        {
            var pay = payment(r);
            var buyer = r.Buyer;
            foreach (var a in r.Attendees.OrderBy(a => a.SortOrder))
            {
                Line(sb,
                [
                    a.ParticipantId, a.FirstName, a.MiddleName, a.LastName, a.Email, a.Mobile, a.Gender, a.Occupation, a.Company,
                    a.ShirtSize, a.HearAbout, ticketName(r), buyer.FullName, r.Status.ToString(), r.IsOverdueAt(now) ? "yes" : "no",
                    Money(r.Subtotal), Money(r.Discount), Money(r.Total), r.VoucherCode, pay?.Method.ToString(), pay?.Reference,
                    Time(r.CreatedAt), r.PaidAt is { } paid ? Time(paid) : null, r.Source.ToString(),
                ]);
            }
        }
        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) => at.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Cell(cells[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>
    /// A cell that starts with <c>= + - @</c>, tab or CR could run as a formula in Excel/Sheets, so it gets a leading
    /// apostrophe. Then RFC 4180: quote when it has a comma, quote or line break, doubling quotes.
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : v;
    }
}
