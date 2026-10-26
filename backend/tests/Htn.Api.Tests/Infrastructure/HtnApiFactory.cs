using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Htn.Api.Tests.Infrastructure;

/// <summary>One API host + one isolated database per test class.</summary>
public class HtnApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Correct-Horse-Battery-1";
    public const string TicketSigningKey = "test-only-ticket-signing-key-0123456789abcdef";

    private TestDatabase? _database;

    protected virtual bool RequireBoard2fa => false;

    /// <summary>Extra configuration applied after the defaults (e.g. low rate limits for a dedicated fixture).</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    protected virtual string Environment => "Testing";

    public FakeEmailSender Emails { get; } = new();

    /// <summary>Per-fixture upload folder (Storage:Path), deleted on dispose.</summary>
    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "htn-test-uploads-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HtnDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
        if (Directory.Exists(StoragePath)) Directory.Delete(StoragePath, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting("ConnectionStrings:Db", _database!.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        builder.UseSetting("RateLimiting:LoginPerEmailPermitLimit", "100000");
        builder.UseSetting("RateLimiting:ForgotPerEmailPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicReadPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicVoucherPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicRegisterPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicProofPermitLimit", "100000");
        builder.UseSetting("RateLimiting:RegisterPerEmailPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicEmailsPerIpPermitLimit", "100000");
        builder.UseSetting("RateLimiting:CheckInPermitLimit", "100000");
        builder.UseSetting("RateLimiting:CheckInSearchPermitLimit", "100000");
        builder.UseSetting("Tickets:SigningKey", TicketSigningKey);
        builder.UseSetting("Email:AllowLoggingSender", "true"); // the fake sender replaces it below
        builder.UseSetting("Auth:RequireBoard2fa", RequireBoard2fa ? "true" : "false");
        builder.UseSetting("Auth:SecurityStampValidationInterval", "00:00:00");
        builder.UseSetting("App:PublicBaseUrl", "https://admin.test");
        builder.UseSetting("Storage:Path", StoragePath);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public ApiClient NewClient() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    }));

    public async Task<ApiClient> SignedInAsync(PersonLevel level, bool twoFactor = false)
    {
        var user = await CreateUserAsync(level, twoFactor: twoFactor);
        var client = NewClient();
        if (twoFactor)
        {
            var res = await client.LoginAsync(user.Email, Password);
            if ((int)res.StatusCode != 202) throw new InvalidOperationException("Expected 2FA challenge");
            var ok = await client.PostAsync("/api/auth/login/2fa", new { code = Totp.Code(user.AuthenticatorKey!) });
            ok.EnsureSuccessStatusCode();
        }
        else
        {
            await client.LoginOkAsync(user.Email, Password);
        }
        return client;
    }

    public sealed record TestUser(Guid PersonId, Guid? UserId, string Email, string? AuthenticatorKey);

    public static string UniqueEmail(string prefix = "user") => $"{prefix}.{Guid.NewGuid():N}@example.ph";

    /// <summary>Creates a person and (for login levels) an active user with <see cref="Password"/>.</summary>
    public async Task<TestUser> CreateUserAsync(PersonLevel level, string? email = null, bool twoFactor = false, bool withPassword = true)
    {
        email ??= UniqueEmail(level.ToString().ToLowerInvariant());
        await using var scope = Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<HtnDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var now = DateTimeOffset.UtcNow;
        var person = Person.Create("Test", null, level.ToString(), email, level, null, now, null);
        db.People.Add(person);
        await db.SaveChangesAsync();
        if (!level.HasLogin()) return new TestUser(person.Id, null, person.Email, null);

        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = person.Email, Email = person.Email, PersonId = person.Id, CreatedAt = now, EmailConfirmed = true, LockoutEnabled = true };
        var result = withPassword ? await users.CreateAsync(user, Password) : await users.CreateAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, Roles.For(level));
        string? key = null;
        if (twoFactor)
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
        }
        return new TestUser(person.Id, user.Id, person.Email, key);
    }

    public async Task<int> ProcessOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    public async Task<T> DbAsync<T>(Func<HtnDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<HtnDbContext>());
    }
}

/// <summary>Same host with Auth:RequireBoard2fa = true (the production default).</summary>
public sealed class Board2faApiFactory : HtnApiFactory
{
    protected override bool RequireBoard2fa => true;
}
