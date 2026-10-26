using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Tasks;

/// <summary>
/// The cast every task test needs: one event with two committees, each with its own goon lead, an apprentice who is a
/// member of the first committee (so she can be assigned and work her own cards) and an apprentice who is on no
/// committee at all (the 403 case).
/// </summary>
internal sealed record TaskScene(
    ApiClient Board,
    Guid EventId,
    Guid Committee,
    HtnApiFactory.TestUser Lead,
    ApiClient LeadClient,
    Guid OtherCommittee,
    HtnApiFactory.TestUser OtherLead,
    ApiClient OtherLeadClient,
    HtnApiFactory.TestUser Worker,
    ApiClient WorkerClient,
    HtnApiFactory.TestUser Outsider,
    ApiClient OutsiderClient);

internal static class TaskTestData
{
    public static async Task<TaskScene> SceneAsync(HtnApiFactory factory)
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var eventId = await OnSaleAsync(board);
        var logistics = await AddCommitteeAsync(board, eventId, "Logistics " + Guid.NewGuid().ToString("N")[..6]);
        var programs = await AddCommitteeAsync(board, eventId, "Programs " + Guid.NewGuid().ToString("N")[..6]);

        var (lead, leadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        var (otherLead, otherLeadClient) = await SignedInUserAsync(factory, PersonLevel.Goon);
        await SetLeadAsync(board, eventId, logistics, lead.PersonId);
        await SetLeadAsync(board, eventId, programs, otherLead.PersonId);

        var (worker, workerClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);
        var added = await board.PostAsync($"/api/events/{eventId}/committees/{logistics}/members", new { personId = worker.PersonId });
        if (added.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Add member failed: {(int)added.StatusCode} {await added.Content.ReadAsStringAsync()}");
        var (outsider, outsiderClient) = await SignedInUserAsync(factory, PersonLevel.Apprentice);

        return new TaskScene(board, eventId, logistics, lead, leadClient, programs, otherLead, otherLeadClient,
            worker, workerClient, outsider, outsiderClient);
    }

    public static object NewTask(Guid committeeId, string? title = null, int? basePoints = null, string? status = null,
        DateTimeOffset? dueAt = null, string[]? labels = null, string? description = null) => new
    {
        committeeId,
        title = title ?? "Task " + Guid.NewGuid().ToString("N")[..8],
        description,
        status,
        basePoints,
        dueAt,
        labels,
    };

    public static async Task<Guid> CreateTaskAsync(ApiClient client, Guid eventId, Guid committeeId, string? title = null,
        int? basePoints = null, string? status = null, DateTimeOffset? dueAt = null, string[]? labels = null)
    {
        var res = await client.PostAsync($"/api/events/{eventId}/tasks",
            NewTask(committeeId, title, basePoints, status, dueAt, labels));
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create task failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> MoveAsync(ApiClient client, Guid taskId, string status, int? sortOrder = null) =>
        client.PostAsync($"/api/tasks/{taskId}/move", new { status, sortOrder });

    public static Task<HttpResponseMessage> ApproveAsync(ApiClient client, Guid taskId) =>
        client.PostAsync($"/api/tasks/{taskId}/approve");

    public static Task<HttpResponseMessage> ReopenAsync(ApiClient client, Guid taskId, string? reason, string? status = null) =>
        client.PostAsync($"/api/tasks/{taskId}/reopen", new { reason, status });

    public static Task<HttpResponseMessage> AssignAsync(ApiClient client, Guid taskId, Guid? personId, string role,
        Guid? volunteerId = null) =>
        client.PostAsync($"/api/tasks/{taskId}/assignees", new { personId, volunteerId, role });

    public static async Task AssignOkAsync(ApiClient client, Guid taskId, Guid personId, string role)
    {
        var res = await AssignAsync(client, taskId, personId, role);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Assign failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    public static async Task<JsonElement> DetailAsync(ApiClient client, Guid taskId)
    {
        var res = await client.GetAsync($"/api/tasks/{taskId}");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Task detail failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static async Task<JsonElement> BoardAsync(ApiClient client, Guid eventId, string query = "")
    {
        var res = await client.GetAsync($"/api/events/{eventId}/tasks{query}");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Board failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static JsonElement Column(this JsonElement board, string status) =>
        board.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("status").GetString() == status);

    public static List<JsonElement> Cards(this JsonElement column) =>
        column.GetProperty("cards").EnumerateArray().ToList();

    public static List<string> CardTitles(this JsonElement board, string status) =>
        board.Column(status).Cards().Select(c => c.GetProperty("title").GetString()!).ToList();

    public static JsonElement Card(this JsonElement board, Guid taskId) =>
        board.GetProperty("columns").EnumerateArray()
            .SelectMany(c => c.GetProperty("cards").EnumerateArray())
            .Single(c => c.GetProperty("id").GetGuid() == taskId);

    /// <summary>Live (not reversed) award rows of a task, as (name, points).</summary>
    public static List<(string Name, decimal Points)> LiveAwards(this JsonElement detail) =>
        detail.GetProperty("awards").EnumerateArray()
            .Where(a => a.GetProperty("reversedAt").ValueKind == JsonValueKind.Null)
            .Select(a => (a.GetProperty("name").GetString()!, a.GetProperty("points").GetDecimal()))
            .ToList();

    public static List<string> ActivityKinds(this JsonElement detail) =>
        detail.GetProperty("activity").EnumerateArray().Select(a => a.GetProperty("kind").GetString()!).ToList();

    public static string Status(this JsonElement detail) => detail.GetProperty("status").GetString()!;

    /// <summary>A tiny but valid 1×1 PNG (the upload path trusts the bytes, never the name).</summary>
    public static byte[] Png { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    /// <summary>A minimal, structurally complete PDF: starts with %PDF-1.4 and ends with %%EOF.</summary>
    public static byte[] Pdf { get; } = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
}
