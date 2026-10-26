using Htn.Domain.Common;

namespace Htn.Domain.Settings;

public enum PaymentAccountKind
{
    GCash,
    Bank,
    Cash,
}

/// <summary>An account shown on billing emails (GCash, bank, or cash at the desk). Deactivated, never deleted.</summary>
public sealed class PaymentAccount
{
    public const int LabelMaxLength = 100;
    public const int BankNameMaxLength = 100;
    public const int AccountNameMaxLength = 150;
    public const int AccountNumberMaxLength = 50;
    public const int ReasonMaxLength = 500;
    public const string MaskDots = "••••";

    private PaymentAccount() { }

    public Guid Id { get; private set; }
    public PaymentAccountKind Kind { get; private set; }
    public string Label { get; private set; } = default!;
    public string? BankName { get; private set; }
    public string AccountName { get; private set; } = default!;

    /// <summary>Full number. Board-only in responses; never written to the audit log.</summary>
    public string? AccountNumber { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public string? DeactivationReason { get; private set; }

    public string AccountNumberMasked => Mask(AccountNumber);

    public static PaymentAccount Create(
        PaymentAccountKind kind, string label, string? bankName, string accountName, string? accountNumber, DateTimeOffset at)
    {
        var account = new PaymentAccount { Id = Guid.CreateVersion7(at), IsActive = true, CreatedAt = at };
        account.Apply(kind, label, bankName, accountName, accountNumber);
        return account;
    }

    public void Update(PaymentAccountKind kind, string label, string? bankName, string accountName, string? accountNumber)
    {
        EnsureActive();
        Apply(kind, label, bankName, accountName, accountNumber);
    }

    /// <summary>Only an active account can be the default. The caller clears the previous default.</summary>
    public void SetDefault(bool isDefault)
    {
        if (isDefault) EnsureActive();
        IsDefault = isDefault;
    }

    public void Deactivate(string reason, DateTimeOffset at)
    {
        EnsureActive();
        var r = reason?.Trim();
        if (string.IsNullOrEmpty(r)) throw new DomainException("A reason is required.");
        if (r.Length > ReasonMaxLength) throw new DomainException($"Reason must be at most {ReasonMaxLength} characters.");
        IsActive = false;
        IsDefault = false;
        DeactivatedAt = at;
        DeactivationReason = r;
    }

    /// <summary>"•••• 4567": the last four letters/digits only.</summary>
    public static string Mask(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return MaskDots;
        var chars = number.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length <= 4 ? MaskDots : $"{MaskDots} {new string(chars[^4..])}";
    }

    private void Apply(PaymentAccountKind kind, string label, string? bankName, string accountName, string? accountNumber)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Unknown account kind.");
        var bank = Optional(bankName, "Bank name", BankNameMaxLength);
        var number = Optional(accountNumber, "Account number", AccountNumberMaxLength);
        if (kind == PaymentAccountKind.Bank && bank is null) throw new DomainException("Bank name is required for bank accounts.");
        if (kind != PaymentAccountKind.Cash && number is null) throw new DomainException("Account number is required.");
        Kind = kind;
        Label = Optional(label, "Label", LabelMaxLength) ?? throw new DomainException("Label is required.");
        BankName = kind == PaymentAccountKind.Bank ? bank : null;
        AccountName = Optional(accountName, "Account name", AccountNameMaxLength) ?? throw new DomainException("Account name is required.");
        AccountNumber = number;
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("This account is deactivated.");
    }

    private static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }
}
