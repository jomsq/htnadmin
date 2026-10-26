using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;

namespace Htn.Api.Tests.Committees;

internal static class CommitteeTestData
{
    public static object NewCommittee(string? name = null, int? sortOrder = null) => new
    {
        name = name ?? "Committee " + Guid.NewGuid().ToString("N")[..8],
        sortOrder,
    };

    public static object Volunteer(string name = "Carla Diaz", string? email = null, string? mobile = null, Guid? committeeId = null) => new
    {
        name, email, mobile, committeeId,
    };

    /// <summary>A user of the given level plus a client already signed in as them.</summary>
    public static async Task<(HtnApiFactory.TestUser User, ApiClient Client)> SignedInUserAsync(HtnApiFactory factory, PersonLevel level)
    {
        var user = await factory.CreateUserAsync(level);
        var client = factory.NewClient();
        await client.LoginOkAsync(user.Email, HtnApiFactory.Password);
        return (user, client);
    }

    public static async Task<Guid> AddCommitteeAsync(ApiClient board, Guid eventId, string? name = null, int? sortOrder = null)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/committees", NewCommittee(name, sortOrder));
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create committee failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    /// <summary>Makes <paramref name="personId"/> the lead of the committee (board only).</summary>
    public static async Task SetLeadAsync(ApiClient board, Guid eventId, Guid committeeId, Guid? personId)
    {
        var res = await board.PutAsync($"/api/events/{eventId}/committees/{committeeId}/lead", new { personId });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Set lead failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    public static async Task<JsonElement> PageAsync(ApiClient client, Guid eventId)
    {
        var res = await client.GetAsync($"/api/events/{eventId}/committees");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"List committees failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await ApiClient.JsonAsync(res);
    }

    public static JsonElement Committee(this JsonElement page, Guid committeeId) =>
        page.GetProperty("committees").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == committeeId);

    public static List<Guid> PoolPeople(this JsonElement page) =>
        page.GetProperty("pool").GetProperty("people").EnumerateArray().Select(p => p.GetProperty("personId").GetGuid()).ToList();

    public static List<Guid> PoolVolunteers(this JsonElement page) =>
        page.GetProperty("pool").GetProperty("volunteers").EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList();

    public static List<Guid> MemberIds(this JsonElement committee) =>
        committee.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("personId").GetGuid()).ToList();
}
