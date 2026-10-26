using System.Net;
using System.Text.Json;
using Htn.Api.Common;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Api.Tests.Organization;

/// <summary>positions.holder_id and the open position_assignments term must always agree.</summary>
public sealed class PositionIntegrityTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    /// <summary>Positions whose holder differs from their open term's person (including an open term on a vacant position).</summary>
    private Task<int> MismatchesAsync() => factory.DbAsync(db => db.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value"
        FROM positions p
        LEFT JOIN position_assignments a ON a.position_id = p.id AND a.ended_at IS NULL
        WHERE p.holder_id IS DISTINCT FROM a.person_id
        """).SingleAsync());

    private Task<int> OpenTermsWithoutPositionHolderAsync() => factory.DbAsync(db => db.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value"
        FROM position_assignments a JOIN positions p ON p.id = a.position_id
        WHERE a.ended_at IS NULL AND p.holder_id IS DISTINCT FROM a.person_id
        """).SingleAsync());

    private async Task AssertConsistentAsync()
    {
        Assert.Equal(0, await MismatchesAsync());
        Assert.Equal(0, await OpenTermsWithoutPositionHolderAsync());
    }

    private static async Task<Guid> NewPositionAsync(ApiClient board)
    {
        var res = await board.PostAsync("/api/positions", new { kind = "GoonPosition", title = "Head of " + Guid.NewGuid().ToString("N")[..8] });
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Holder_matches_open_term_through_assign_reassign_vacate_deactivate_and_demote()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var a = await NewPositionAsync(board);
        var b = await NewPositionAsync(board);
        var c = await NewPositionAsync(board);
        var mika = await factory.CreateUserAsync(PersonLevel.Goon);
        var jo = await factory.CreateUserAsync(PersonLevel.Goon);
        var ria = await factory.CreateUserAsync(PersonLevel.Goon);
        await AssertConsistentAsync();

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{a}/assign", new { personId = mika.PersonId })).StatusCode);
        await AssertConsistentAsync();

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{a}/assign", new { personId = jo.PersonId })).StatusCode); // reassign
        await AssertConsistentAsync();

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{a}/vacate", new { reason = "Done" })).StatusCode);
        await AssertConsistentAsync();

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{b}/assign", new { personId = jo.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await board.PostAsync($"/api/people/{jo.PersonId}/deactivate", new { reason = "Left" })).StatusCode);
        await AssertConsistentAsync();
        Assert.Null(await factory.DbAsync(db => db.Positions.Where(p => p.Id == b).Select(p => p.HolderId).SingleAsync()));

        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/positions/{c}/assign", new { personId = ria.PersonId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await board.PostAsync($"/api/people/{ria.PersonId}/promote", new { toLevel = "Apprentice", reason = "Stepping back" })).StatusCode);
        await AssertConsistentAsync();
        Assert.Null(await factory.DbAsync(db => db.Positions.Where(p => p.Id == c).Select(p => p.HolderId).SingleAsync()));

        // Every position touched here has its full history of terms.
        Assert.Equal(2, await factory.DbAsync(db => db.PositionAssignments.CountAsync(t => t.PositionId == a && t.EndedAt != null)));
    }

    [Fact]
    public async Task Position_terms_can_only_be_closed_once_and_never_deleted()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = await NewPositionAsync(board);
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        await board.PostAsync($"/api/positions/{id}/assign", new { personId = goon.PersonId });
        await board.PostAsync($"/api/positions/{id}/vacate");

        async Task AssertRejected(FormattableString sql)
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql)));
            Assert.Contains("position_assignments", ex.Message, StringComparison.Ordinal);
        }

        await AssertRejected($"UPDATE position_assignments SET ended_at = NULL WHERE position_id = {id}");          // reopen
        await AssertRejected($"UPDATE position_assignments SET ended_at = now() WHERE position_id = {id}");         // close twice
        await AssertRejected($"UPDATE position_assignments SET person_id = person_id WHERE position_id = {id}");    // touch a closed term
        await AssertRejected($"DELETE FROM position_assignments WHERE position_id = {id}");
        await AssertRejected($"TRUNCATE position_assignments");

        // Rewriting who held an open term is rejected too.
        await board.PostAsync($"/api/positions/{id}/assign", new { personId = goon.PersonId });
        var other = await factory.CreateUserAsync(PersonLevel.Goon);
        await AssertRejected($"UPDATE position_assignments SET person_id = {other.PersonId}, ended_at = now() WHERE position_id = {id} AND ended_at IS NULL");
    }

    [Fact]
    public async Task Concurrent_change_to_a_position_fails_the_second_save()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var id = await NewPositionAsync(board);

        await using var scopeA = factory.Services.CreateAsyncScope();
        await using var scopeB = factory.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<HtnDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<HtnDbContext>();
        var a = await dbA.Positions.SingleAsync(p => p.Id == id);
        var b = await dbB.Positions.SingleAsync(p => p.Id == id);

        a.Rename("Head of A");
        await dbA.SaveChangesAsync();
        b.Rename("Head of B");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
        Assert.Equal("Head of A", await factory.DbAsync(db => db.Positions.Where(p => p.Id == id).Select(p => p.Title).SingleAsync()));
    }

    [Fact]
    public async Task Concurrency_failures_become_409_concurrent_change()
    {
        var http = new DefaultHttpContext { RequestServices = factory.Services };
        http.Response.Body = new MemoryStream();
        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(http, new DbUpdateConcurrencyException("lost race"), CancellationToken.None);
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("concurrent_change", body.RootElement.GetProperty("code").GetString());
    }
}
