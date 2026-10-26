using System.Net;
using System.Text;
using System.Text.Json;
using Htn.Api.Features.Organization;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Organization;

public sealed class PeopleImportTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private const string Header = "first_name,middle_name,last_name,email,level,position";

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");

    private static Task<HttpResponseMessage> UploadAsync(ApiClient client, byte[] csv, bool dryRun) =>
        client.PostFileAsync($"/api/people/import?dryRun={(dryRun ? "true" : "false")}", csv, "roster.csv", "text/csv");

    private static Dictionary<int, JsonElement> RowsByNumber(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("row").GetInt32());

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Goon_and_apprentice_cannot_import()
    {
        var csv = Csv(Header, $"Ana,,Lim,ana.{Tag()}@example.ph,Member,");
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(client, csv, dryRun: true)).StatusCode);
        }
    }

    [Fact]
    public async Task Dry_run_reports_row_errors_including_formula_injection_and_creates_nothing()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var existing = await factory.CreateUserAsync(PersonLevel.Member);
        var t = Tag();
        var csv = Csv(
            Header,
            $"Ana,,Lim,ana.{t}@example.ph,,",                              // 2 ok (Member by default)
            $"=HYPERLINK(\"http://evil\"),,Cruz,cruz.{t}@example.ph,Goon,", // 3 formula
            $"Ben,,Tan,ben.{t}@example.ph,Apprentice,@SUM(A1)",            // 4 formula in position
            $"Cara,,Ong,cara.{t}@example.ph,Board,",                       // 5 board not importable
            $"Dan,,Uy,{existing.Email.ToUpperInvariant()},Member,",        // 6 already in the org
            $"Eli,,Go,ANA.{t}@example.ph,Member,",                         // 7 duplicate in file
            "Fay,,,not-an-email,Overlord,",                                // 8 several errors
            $"\"Gil, Jr.\",\"Q\",\"Sy\",gil.{t}@example.ph,goon,\"Head of \"\"Tech\"\"\"", // 9 ok, quoted
            $"Hal,,Ko,hal.{t}@example.ph,Member,-1+1",                     // 10 formula with minus
            $"Ivy,,Li,\"\t=cmd\"@example.ph,Member,");                     // 11 tab-prefixed formula

        var res = await UploadAsync(board, csv, dryRun: true);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await ApiClient.JsonAsync(res);
        var rows = RowsByNumber(result);

        Assert.Equal(2, result.GetProperty("valid").GetInt32());
        Assert.Equal(8, result.GetProperty("invalid").GetInt32());
        Assert.Equal(0, result.GetProperty("created").GetInt32());

        Assert.Empty(rows[2].GetProperty("errors").EnumerateArray());
        Assert.Equal("Member", rows[2].GetProperty("level").GetString());
        Assert.Equal($"ana.{t}@example.ph", rows[2].GetProperty("email").GetString());

        static string Errors(JsonElement row) => string.Join(" | ", row.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("First name can't start with =, +, - or @.", Errors(rows[3]), StringComparison.Ordinal);
        Assert.Contains("Position can't start with", Errors(rows[4]), StringComparison.Ordinal);
        Assert.Contains("Board members can't be imported", Errors(rows[5]), StringComparison.Ordinal);
        Assert.Contains("Email is already in the organization.", Errors(rows[6]), StringComparison.Ordinal);
        Assert.Contains("more than once in the file (first on row 2)", Errors(rows[7]), StringComparison.Ordinal);
        var many = Errors(rows[8]);
        Assert.Contains("Last name is required.", many, StringComparison.Ordinal);
        Assert.Contains("Email isn't valid.", many, StringComparison.Ordinal);
        Assert.Contains("Level must be Member, Apprentice or Goon.", many, StringComparison.Ordinal);
        Assert.Empty(rows[9].GetProperty("errors").EnumerateArray());
        Assert.Equal("Gil, Jr.", rows[9].GetProperty("firstName").GetString());
        Assert.Equal("Goon", rows[9].GetProperty("level").GetString());
        Assert.Contains("Position can't start with", Errors(rows[10]), StringComparison.Ordinal);
        Assert.Contains("Email can't start with", Errors(rows[11]), StringComparison.Ordinal);

        Assert.False(await factory.DbAsync(db => db.People.AnyAsync(p => p.Email == $"ana.{t}@example.ph")));

        // Committing a file with errors is refused as a whole.
        var commit = await UploadAsync(board, csv, dryRun: false);
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
        Assert.Equal("import_has_errors", (await ApiClient.JsonAsync(commit)).GetProperty("code").GetString());
        Assert.False(await factory.DbAsync(db => db.People.AnyAsync(p => p.Email == $"ana.{t}@example.ph")));
    }

    [Fact]
    public async Task Commit_creates_people_and_invites_only_login_levels()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var t = Tag();
        var csv = Csv(
            "email,first_name,last_name,level", // any column order; middle_name/position optional
            $"m.{t}@example.ph,Mara,Cruz,Member",
            $"a.{t}@example.ph,Abe,Reyes,Apprentice",
            $"g.{t}@example.ph,Gia,Santos,Goon");

        var res = await UploadAsync(board, csv, dryRun: false);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await ApiClient.JsonAsync(res);
        Assert.Equal(3, result.GetProperty("created").GetInt32());
        Assert.Equal(3, result.GetProperty("valid").GetInt32());

        var people = await factory.DbAsync(db => db.People.AsNoTracking().Where(p => p.Email.EndsWith($".{t}@example.ph")).ToListAsync());
        Assert.Equal(3, people.Count);
        var ids = people.Select(p => p.Id).ToList();
        var users = await factory.DbAsync(db => db.Users.AsNoTracking().Where(u => u.PersonId != null && ids.Contains(u.PersonId.Value)).ToListAsync());
        Assert.Equal(2, users.Count);
        Assert.DoesNotContain(users, u => u.Email == $"m.{t}@example.ph");

        var invites = (await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(o => o.Type == EmailTypes.Invite).ToListAsync()))
            .Where(o => o.PayloadJson.Contains($".{t}@example.ph", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, invites.Count);

        Assert.Equal(3, await factory.DbAsync(db => db.AuditEntries.CountAsync(a => a.Action == "person.create" && ids.Select(i => i.ToString()).Contains(a.EntityId))));
        var summary = (await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Action == "people.import").ToListAsync()))
            .Single(a => JsonDocument.Parse(a.AfterJson!).RootElement.GetProperty("created").GetInt32() == 3);
        Assert.Equal(1, JsonDocument.Parse(summary.AfterJson!).RootElement.GetProperty("goons").GetInt32());

        // Same file again: everyone is already in the organization.
        var again = await ApiClient.JsonAsync(await UploadAsync(board, csv, dryRun: true));
        Assert.Equal(3, again.GetProperty("invalid").GetInt32());
    }

    [Fact]
    public async Task File_level_problems_are_400_on_file()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);

        async Task AssertFileError(byte[] content, string contains)
        {
            var res = await UploadAsync(board, content, dryRun: true);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var errors = (await ApiClient.JsonAsync(res)).GetProperty("errors").GetProperty("file");
            Assert.Contains(contains, errors[0].GetString(), StringComparison.Ordinal);
        }

        await AssertFileError(Csv("name,email", "Ana,ana@example.ph"), "Unknown column");
        await AssertFileError(Csv("first_name,email", "Ana,ana@example.ph"), "Missing column \"last_name\"");
        await AssertFileError(Csv(Header), "no people");
        await AssertFileError([], "empty");
        await AssertFileError(Csv(Header, "\"Ana,,Lim,a@example.ph,,"), "quoted value");
        await AssertFileError([0xFF, 0xFE, 0x00, 0x41], "UTF-8");

        var tooMany = new List<string> { Header };
        for (var i = 0; i <= PeopleImport.MaxRows; i++) tooMany.Add($"P{i},,L,p{i}.{Tag()}@example.ph,,");
        await AssertFileError(Csv([.. tooMany]), "more than 500 rows");

        var big = new byte[PeopleImport.MaxBytes + 10];
        Array.Fill(big, (byte)'a');
        var tooBig = await UploadAsync(board, big, dryRun: true);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("file_too_large", (await ApiClient.JsonAsync(tooBig)).GetProperty("code").GetString());

        var noFile = await board.PostAsync("/api/people/import?dryRun=true", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
    }

    [Fact]
    public async Task Upload_without_csrf_token_is_rejected()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var res = await board.PostFileAsync("/api/people/import?dryRun=true", Csv(Header, "Ana,,Lim,a@example.ph,,"), "r.csv", "text/csv", csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("antiforgery", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("=1+1", true)]
    [InlineData("+63 917", true)]
    [InlineData("-5", true)]
    [InlineData("@x", true)]
    [InlineData("  =x", true)]
    [InlineData("\t1", true)]
    [InlineData("Ana", false)]
    [InlineData("ana@example.ph", false)]
    [InlineData("", false)]
    public void Formula_detection(string value, bool expected) => Assert.Equal(expected, PeopleImport.IsFormula(value));
}
