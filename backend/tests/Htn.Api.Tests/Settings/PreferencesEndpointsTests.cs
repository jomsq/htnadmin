using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;

namespace Htn.Api.Tests.Settings;

public sealed class PreferencesEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Update_preferences_is_reflected_in_me()
    {
        var client = await factory.SignedInAsync(PersonLevel.Apprentice);
        var res = await client.PutAsync("/api/me/preferences", new { mode = "dark", textSize = "large", density = "compact" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var prefs = (await ApiClient.JsonAsync(await client.GetAsync("/api/auth/me"))).GetProperty("preferences");
        Assert.Equal("dark", prefs.GetProperty("mode").GetString());
        Assert.Equal("large", prefs.GetProperty("textSize").GetString());
        Assert.Equal("compact", prefs.GetProperty("density").GetString());
    }

    [Fact]
    public async Task Invalid_values_are_400()
    {
        var client = await factory.SignedInAsync(PersonLevel.Goon);
        var res = await client.PutAsync("/api/me/preferences", new { mode = "neon", textSize = "default", density = "comfortable" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await ApiClient.JsonAsync(res)).GetProperty("errors").TryGetProperty("mode", out _));
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        var res = await factory.NewClient().PutAsync("/api/me/preferences", new { mode = "dark", textSize = "default", density = "compact" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
