using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Auth;
using Htn.Api.Features.Organization;
using Htn.Api.Features.Settings;
using Htn.Api.Seeding;
using Htn.Infrastructure;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var isDev = builder.Environment.IsDevelopment();
var cookieSecure = isDev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.AddHtnInfrastructure(builder.Configuration);
builder.Services.AddOptions<AuthSettings>().BindConfiguration(AuthSettings.Section);
builder.Services.AddOptions<RateLimitSettings>().BindConfiguration(RateLimitSettings.Section);

// ---- Reverse proxy (nginx / TLS terminator) ----
// Trust X-Forwarded-For/Proto so Secure cookies work behind TLS termination and rate limits see the real client IP.
// Default trusts any proxy (the API is only reachable through the docker network). Set ForwardedHeaders:KnownProxies
// (comma-separated IPs) to restrict it.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    var known = builder.Configuration["ForwardedHeaders:KnownProxies"];
    if (!string.IsNullOrWhiteSpace(known))
        foreach (var ip in known.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
});

// ---- Problem details, JSON ----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- Identity + cookie auth ----
builder.Services
    .AddIdentity<AppUser, IdentityRole<Guid>>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredUniqueChars = 1;
        o.User.RequireUniqueEmail = true;
        o.User.AllowedUserNameCharacters = ""; // user names are emails
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<HtnDbContext>()
    .AddSignInManager<HtnSignInManager>()
    .AddClaimsPrincipalFactory<HtnClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.AddDataProtection().SetApplicationName("htn-admin").PersistKeysToDbContext<HtnDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".htn.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSecure;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized, "Sign in to continue.");
    o.Events.OnRedirectToAccessDenied = ctx => Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status403Forbidden, "You don't have access to this.");
});
builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<IOptions<AuthSettings>>((o, s) => o.ExpireTimeSpan = s.Value.SessionLifetime);
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
{
    o.Cookie.Name = ".htn.2fa";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSecure;
    o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddOptions<SecurityStampValidatorOptions>()
    .Configure<IOptions<AuthSettings>>((o, s) => o.ValidationInterval = s.Value.SecurityStampValidationInterval);

builder.Services.AddAuthorizationBuilder().AddHtnPolicies();

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = AntiforgeryValidationMiddleware.HeaderName;
    o.Cookie.Name = ".htn.csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSecure;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

// ---- Rate limiting (fixed window per client IP and route) ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) => new ValueTask(Problems.WriteAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests,
        "Too many attempts", "Wait a minute and try again.", "rate_limited"));
    o.AddPolicy(RateLimitSettings.AuthPolicy, http =>
    {
        var s = http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
        var key = $"{http.Connection.RemoteIpAddress}|{http.Request.Path}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = s.AuthPermitLimit,
            Window = TimeSpan.FromSeconds(s.AuthWindowSeconds),
            QueueLimit = 0,
        });
    });
});

// ---- App services ----
ValidationExtensions.UseCamelCasePropertyNames();
builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<MeService>();
builder.Services.AddHealthChecks().AddDbContextCheck<HtnDbContext>("db");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<AntiforgeryValidationMiddleware>();
app.UseMiddleware<TwoFactorGateMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuth();
app.MapPeople();
app.MapPreferences();

var migrate = app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment());
if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<HtnDbContext>().Database.MigrateAsync();
}
if (app.Environment.IsDevelopment())
    await DevSeeder.SeedAsync(app.Services);

await app.RunAsync();

public partial class Program;
