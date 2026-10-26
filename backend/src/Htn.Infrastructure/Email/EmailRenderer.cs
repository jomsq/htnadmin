using Htn.Infrastructure.Email.Templates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>Renders Razor component templates to HTML with <see cref="HtmlRenderer"/>.</summary>
public sealed class EmailRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    public async Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    public async Task<EmailMessage> InviteAsync(InviteEmailPayload p, string link, int expiresInHours)
    {
        var html = await RenderAsync<InviteEmail>(new Dictionary<string, object?>
        {
            [nameof(InviteEmail.FirstName)] = p.FirstName,
            [nameof(InviteEmail.InviterName)] = p.InviterName,
            [nameof(InviteEmail.Level)] = p.Level,
            [nameof(InviteEmail.Position)] = p.Position,
            [nameof(InviteEmail.Link)] = link,
            [nameof(InviteEmail.ExpiresInHours)] = expiresInHours,
        });
        var text =
            $"""
            Hi {p.FirstName},

            {p.InviterName ?? "An admin"} created {EmailText.WithArticle(p.Level)} account for you{(p.Position is null ? "" : $" ({p.Position})")} on the {Brand.OrgName} Admin system.

            Set your password to activate it. The link works once and expires in {expiresInHours} hours:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(p.To, p.FirstName, $"You're invited to {Brand.OrgName} Admin", html, text, EmailCategories.Invite);
    }

    public async Task<EmailMessage> PasswordResetAsync(PasswordResetEmailPayload p, string link, int expiresInMinutes)
    {
        var html = await RenderAsync<PasswordResetEmail>(new Dictionary<string, object?>
        {
            [nameof(PasswordResetEmail.FirstName)] = p.FirstName,
            [nameof(PasswordResetEmail.Link)] = link,
            [nameof(PasswordResetEmail.ExpiresInMinutes)] = expiresInMinutes,
        });
        var text =
            $"""
            Hi {p.FirstName},

            We received a request to reset the password for your {Brand.OrgName} Admin account.
            The link works once and expires in {expiresInMinutes} minutes:
            {link}

            If you didn't ask for this, you can ignore this email; your password stays the same.

            {Brand.OrgName}
            """;
        return new EmailMessage(p.To, p.FirstName, "Reset your password", html, text, EmailCategories.PasswordReset);
    }
}
