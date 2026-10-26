using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Auth;

public sealed class AuthEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Login_without_csrf_header_is_rejected()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        var client = factory.NewClient();
        var res = await client.PostAsync("/api/auth/login", new { email = user.Email, password = HtnApiFactory.Password }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("antiforgery", (await ApiClient.JsonAsync(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Authenticated_post_without_csrf_header_is_rejected()
    {
        var client = await factory.SignedInAsync(PersonLevel.Board);
        var res = await client.PostAsync("/api/people", new { firstName = "A", lastName = "B", email = HtnApiFactory.UniqueEmail(), level = "Member" }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_success_returns_me_and_sets_secure_cookie()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Board);
        var client = factory.NewClient();
        var res = await client.LoginAsync(user.Email.ToUpperInvariant(), HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var cookie = Assert.Single(res.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".htn.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await ApiClient.JsonAsync(res);
        Assert.Equal(user.UserId, me.GetProperty("id").GetGuid());
        Assert.Equal(user.PersonId, me.GetProperty("personId").GetGuid());
        Assert.Equal("Board", me.GetProperty("level").GetString());
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.False(me.GetProperty("isCfo").GetBoolean());
        Assert.False(me.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.False(me.GetProperty("twoFactorRequired").GetBoolean());
        var policies = me.GetProperty("policies").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("People.Manage", policies);
        Assert.Contains("Settings.Personal", policies);
        Assert.DoesNotContain("Finance.Post", policies); // CFO only
        var prefs = me.GetProperty("preferences");
        Assert.Equal("system", prefs.GetProperty("mode").GetString());
        Assert.Equal("default", prefs.GetProperty("textSize").GetString());
        Assert.Equal("comfortable", prefs.GetProperty("density").GetString());

        var again = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var lastSignIn = await factory.DbAsync(db => db.Users.Where(u => u.Id == user.UserId).Select(u => u.LastSignInAt).SingleAsync());
        Assert.NotNull(lastSignIn);
    }

    [Fact]
    public async Task Goon_me_has_people_view_but_not_manage()
    {
        var client = await factory.SignedInAsync(PersonLevel.Goon);
        var me = await ApiClient.JsonAsync(await client.GetAsync("/api/auth/me"));
        var policies = me.GetProperty("policies").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("People.View", policies);
        Assert.Contains("Settings.Personal", policies);
        Assert.DoesNotContain("People.Manage", policies);
        Assert.DoesNotContain("Finance.View", policies);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_same_401()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        var client = factory.NewClient();

        var wrong = await client.LoginAsync(user.Email, "definitely-wrong-password");
        var unknown = await client.LoginAsync(HtnApiFactory.UniqueEmail("nobody"), "definitely-wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(
            (await ApiClient.JsonAsync(wrong)).GetProperty("title").GetString(),
            (await ApiClient.JsonAsync(unknown)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Invite_pending_user_without_password_cannot_login()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon, withPassword: false);
        var res = await factory.NewClient().LoginAsync(user.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_login_and_existing_session_ends()
    {
        var goon = await factory.CreateUserAsync(PersonLevel.Goon);
        var goonClient = factory.NewClient();
        await goonClient.LoginOkAsync(goon.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, (await goonClient.GetAsync("/api/auth/me")).StatusCode);

        var board = await factory.SignedInAsync(PersonLevel.Board);
        var deactivate = await board.PostAsync($"/api/people/{goon.PersonId}/deactivate");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await goonClient.GetAsync("/api/auth/me")).StatusCode);
        var login = await factory.NewClient().LoginAsync(goon.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Me_requires_sign_in()
    {
        var res = await factory.NewClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var client = await factory.SignedInAsync(PersonLevel.Apprentice);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_requires_sign_in()
    {
        var res = await factory.NewClient().PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Health_is_anonymous_and_checks_db()
    {
        var res = await factory.NewClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lockout_after_five_failures()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        var client = factory.NewClient();
        for (var i = 0; i < 5; i++) await client.LoginAsync(user.Email, "wrong-password-xyz");
        var res = await client.LoginAsync(user.Email, HtnApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

public sealed class RememberMeTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    [Fact]
    public async Task Remember_me_issues_a_persistent_cookie()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Goon);
        var session = await factory.NewClient().LoginAsync(user.Email, HtnApiFactory.Password);
        var sessionCookie = session.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".htn.auth=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var persistent = await factory.NewClient().PostAsync("/api/auth/login", new { email = user.Email, password = HtnApiFactory.Password, rememberMe = true });
        Assert.Equal(HttpStatusCode.OK, persistent.StatusCode);
        var cookie = persistent.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".htn.auth=", StringComparison.Ordinal));
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
