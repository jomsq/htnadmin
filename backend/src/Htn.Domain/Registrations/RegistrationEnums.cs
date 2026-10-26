namespace Htn.Domain.Registrations;

/// <summary>
/// Checkout lifecycle: <see cref="PendingPayment"/> → <see cref="Paid"/> → <see cref="CheckedIn"/>, or <see cref="Cancelled"/>.
/// Overdue is never stored: it is derived (<c>PendingPayment</c> and the deadline has passed). Never deleted.
/// </summary>
public enum RegistrationStatus
{
    PendingPayment,
    Paid,
    CheckedIn,
    Cancelled,
}

public enum RegistrationSource
{
    /// <summary>The public registration page.</summary>
    Public,

    /// <summary>Entered by an admin (manual registration).</summary>
    Manual,
}

/// <summary>How a payment was received. <see cref="Comp"/> is a complimentary seat (no money, amount 0).</summary>
public enum PaymentMethod
{
    GCash,
    Bank,
    Cash,
    Comp,
}
