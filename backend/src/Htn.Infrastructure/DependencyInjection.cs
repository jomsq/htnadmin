using Htn.Domain.Common;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Htn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHtnInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPublicIdGenerator, PublicIdGenerator>();

        // Connection string is resolved lazily so tests can override configuration.
        services.AddDbContext<HtnDbContext>((sp, options) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Db")
                     ?? throw new InvalidOperationException("ConnectionStrings:Db is not configured.");
            HtnDbContextOptions.Configure(options, cs);
        });

        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAccountTokenService, AccountTokenService>();
        services.AddOptions<AccountTokenOptions>().BindConfiguration(AccountTokenOptions.Section);
        services.AddOptions<AppLinkOptions>().BindConfiguration(AppLinkOptions.Section);
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.Section);
        services.AddOptions<MailtrapOptions>().Configure<IConfiguration>((o, c) =>
        {
            var from = MailtrapOptions.FromConfiguration(c);
            o.ApiToken = from.ApiToken;
            o.Mode = from.Mode;
            o.InboxId = from.InboxId;
            o.From = from.From;
        });

        services.AddScoped<EmailRenderer>();
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxWorker>();

        if (MailtrapOptions.FromConfiguration(configuration).IsConfigured)
        {
            services.AddHttpClient<IEmailSender, MailtrapEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(60))
                .AddStandardResilienceHandler(o =>
                {
                    o.Retry.MaxRetryAttempts = 3;
                    o.Retry.BackoffType = DelayBackoffType.Exponential;
                    o.Retry.UseJitter = true;
                    o.Retry.Delay = TimeSpan.FromSeconds(1);
                    // Only 429 and 5xx (and transport errors) are retried.
                    o.Retry.ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException ||
                        args.Outcome.Result is { } r && ((int)r.StatusCode == 429 || (int)r.StatusCode >= 500));
                });
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
