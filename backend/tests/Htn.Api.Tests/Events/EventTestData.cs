using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;

namespace Htn.Api.Tests.Events;

internal static class EventTestData
{
    public static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(40);

    public static object NewEvent(string? name = null, string? slug = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null,
        int capacity = 400) => new
    {
        name = name ?? "Event " + Guid.NewGuid().ToString("N")[..8],
        slug,
        startsAt = startsAt ?? Start,
        endsAt = endsAt ?? (startsAt ?? Start).AddHours(10),
        venue = "SMX Convention Center, Pasay",
        capacity,
        description = "Talks and workshops.",
    };

    public static object Ticket(string name = "Regular", decimal price = 1500m, int quantity = 200, int? groupSize = null,
        DateTimeOffset? salesStartsAt = null, DateTimeOffset? salesEndsAt = null, string? visibility = null) => new
    {
        name, price, quantity, groupSize, salesStartsAt, salesEndsAt, visibility,
    };

    public static object Voucher(string code = "STUDENT20", string kind = "Percent", decimal value = 20m, Guid[]? ticketTypeIds = null,
        int? usageLimit = 50, DateTimeOffset? expiresAt = null) => new
    {
        code, kind, value, ticketTypeIds = ticketTypeIds ?? [], usageLimit, expiresAt,
    };

    public static async Task<JsonElement> CreateAsync(ApiClient board, object? body = null)
    {
        var res = await board.PostAsync("/api/events", body ?? NewEvent());
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create event failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static async Task<JsonElement> AddTicketAsync(ApiClient board, Guid eventId, object? body = null)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/tickets", body ?? Ticket());
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add ticket failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    /// <summary>A Draft event with one ticket, put on sale.</summary>
    public static async Task<Guid> OnSaleAsync(ApiClient board, object? body = null)
    {
        var id = (await CreateAsync(board, body)).GetProperty("id").GetGuid();
        await AddTicketAsync(board, id);
        var res = await board.PostAsync($"/api/events/{id}/publish");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Publish failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return id;
    }

    public static Guid Id(this JsonElement e) => e.GetProperty("id").GetGuid();

    public static string? Code(this JsonElement problem) =>
        problem.TryGetProperty("code", out var c) ? c.GetString() : null;

    public static bool HasError(this JsonElement problem, string field) =>
        problem.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out _);
}
