using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Htn.Api.Features.Settings;

/// <summary><see cref="AccountNumber"/> is the full number for Board callers only; everyone else gets null.</summary>
public sealed record PaymentAccountDto(
    Guid Id, PaymentAccountKind Kind, string Label, string? BankName, string AccountName,
    string? AccountNumber, string AccountNumberMasked, bool IsDefault, bool IsActive);

public sealed record PaymentAccountRequest(
    PaymentAccountKind? Kind, string Label, string? BankName, string AccountName, string? AccountNumber, bool? IsDefault);

public sealed record DeactivatePaymentAccountRequest(string Reason);

public sealed class PaymentAccountRequestValidator : AbstractValidator<PaymentAccountRequest>
{
    public PaymentAccountRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().WithMessage("Choose GCash, bank or cash.").IsInEnum().WithMessage("Choose GCash, bank or cash.");
        RuleFor(x => x.Label).NotEmpty().WithMessage("Enter a label.")
            .MaximumLength(PaymentAccount.LabelMaxLength).WithMessage($"Use at most {PaymentAccount.LabelMaxLength} characters.");
        RuleFor(x => x.BankName).NotEmpty().When(x => x.Kind == PaymentAccountKind.Bank).WithMessage("Enter the bank name.");
        RuleFor(x => x.BankName).MaximumLength(PaymentAccount.BankNameMaxLength).WithMessage($"Use at most {PaymentAccount.BankNameMaxLength} characters.");
        RuleFor(x => x.AccountName).NotEmpty().WithMessage("Enter the account name.")
            .MaximumLength(PaymentAccount.AccountNameMaxLength).WithMessage($"Use at most {PaymentAccount.AccountNameMaxLength} characters.");
        RuleFor(x => x.AccountNumber).NotEmpty().When(x => x.Kind is PaymentAccountKind.GCash or PaymentAccountKind.Bank)
            .WithMessage("Enter the account number.");
        RuleFor(x => x.AccountNumber).MaximumLength(PaymentAccount.AccountNumberMaxLength)
            .WithMessage($"Use at most {PaymentAccount.AccountNumberMaxLength} characters.")
            .Matches("^[0-9A-Za-z][0-9A-Za-z -]*$").When(x => !string.IsNullOrWhiteSpace(x.AccountNumber))
            .WithMessage("Use digits, letters, spaces and dashes only.");
    }
}

public sealed class DeactivatePaymentAccountRequestValidator : AbstractValidator<DeactivatePaymentAccountRequest>
{
    public DeactivatePaymentAccountRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason.")
            .MaximumLength(PaymentAccount.ReasonMaxLength).WithMessage($"Use at most {PaymentAccount.ReasonMaxLength} characters.");
    }
}

public static class PaymentAccountsEndpoints
{
    public static RouteGroupBuilder MapPaymentAccounts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings/payment-accounts").WithTags("Settings");
        g.MapGet("/", List).RequireAuthorization(Policies.SettingsManage);
        g.MapPost("/", Create).RequireAuthorization(Policies.SettingsManage).Validate<PaymentAccountRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.SettingsManage).Validate<PaymentAccountRequest>();
        g.MapPost("/{id:guid}/deactivate", Deactivate).RequireAuthorization(Policies.SettingsManage).Validate<DeactivatePaymentAccountRequest>();
        return g;
    }

    /// <summary>Board-only fields (the full number) are stripped here, in the projection.</summary>
    private static PaymentAccountDto ToDto(PaymentAccount a, bool board) => new(
        a.Id, a.Kind, a.Label, a.BankName, a.AccountName, board ? a.AccountNumber : null, a.AccountNumberMasked, a.IsDefault, a.IsActive);

    /// <summary>What the audit log keeps: never the full account number.</summary>
    private static object AuditView(PaymentAccount a) => new
    {
        a.Kind, a.Label, a.BankName, a.AccountName, accountNumberMasked = a.AccountNumberMasked, a.IsDefault, a.IsActive,
    };

    private static bool IsBoard(HttpContext http) => http.User.IsInRole(Roles.Board);

    private static async Task<IResult> List(HttpContext http, bool? includeInactive, HtnDbContext db, CancellationToken ct)
    {
        var q = db.PaymentAccounts.AsNoTracking();
        if (includeInactive != true) q = q.Where(a => a.IsActive);
        var rows = await q.OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.IsActive).ThenBy(a => a.Kind).ThenBy(a => a.Label)
            .ToListAsync(ct);
        var board = IsBoard(http);
        return TypedResults.Ok(rows.Select(a => ToDto(a, board)).ToList());
    }

    private static async Task<IResult> Create(
        PaymentAccountRequest req, HttpContext http, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var account = PaymentAccount.Create(req.Kind!.Value, req.Label, req.BankName, req.AccountName, req.AccountNumber, clock.GetUtcNow());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.PaymentAccounts.Add(account);
            var previousDefault = req.IsDefault == true ? await ClearDefaultAsync(db, account.Id, ct) : null;
            if (req.IsDefault == true) account.SetDefault(true);
            audit.Record(me.Id, "payment_account.create", nameof(PaymentAccount), account.Id.ToString(),
                after: new { account = AuditView(account), previousDefaultId = previousDefault });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Conflict();
        }
        return TypedResults.Created($"/api/settings/payment-accounts/{account.Id}", ToDto(account, IsBoard(http)));
    }

    private static async Task<IResult> Update(
        Guid id, PaymentAccountRequest req, HttpContext http, HtnDbContext db, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var account = await db.PaymentAccounts.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return TypedResults.NotFound();
        if (!account.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This account is deactivated.", code: "account_deactivated");

        var before = AuditView(account);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            account.Update(req.Kind!.Value, req.Label, req.BankName, req.AccountName, req.AccountNumber);
            Guid? previousDefault = null;
            if (req.IsDefault == true && !account.IsDefault)
            {
                previousDefault = await ClearDefaultAsync(db, account.Id, ct);
                account.SetDefault(true);
            }
            else if (req.IsDefault == false && account.IsDefault)
            {
                account.SetDefault(false);
            }
            audit.Record(me.Id, "payment_account.update", nameof(PaymentAccount), account.Id.ToString(),
                before, new { account = AuditView(account), previousDefaultId = previousDefault });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Conflict();
        }
        return TypedResults.Ok(ToDto(account, IsBoard(http)));
    }

    private static async Task<IResult> Deactivate(
        Guid id, DeactivatePaymentAccountRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.PaymentAccounts.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return TypedResults.NotFound();
        if (!account.IsActive)
            return Problems.Result(StatusCodes.Status409Conflict, "This account is already deactivated.", code: "account_deactivated");

        var before = AuditView(account);
        var reason = req.Reason.Trim();
        account.Deactivate(reason, clock.GetUtcNow());
        audit.Record(me.Id, "payment_account.deactivate", nameof(PaymentAccount), account.Id.ToString(), before, AuditView(account), reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Clears the current default (other than <paramref name="exceptId"/>) and saves, so the single-default index holds.</summary>
    private static async Task<Guid?> ClearDefaultAsync(HtnDbContext db, Guid exceptId, CancellationToken ct)
    {
        var current = await db.PaymentAccounts.SingleOrDefaultAsync(a => a.IsDefault && a.Id != exceptId, ct);
        if (current is null) return null;
        current.SetDefault(false);
        await db.SaveChangesAsync(ct);
        return current.Id;
    }

    private static IResult Conflict() =>
        Problems.Result(StatusCodes.Status409Conflict, "The default account changed at the same time. Reload and try again.", code: "conflict");

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
