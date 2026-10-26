namespace Htn.Domain.Common;

/// <summary>Thrown when an entity method rejects a state transition or invariant.</summary>
public sealed class DomainException(string message) : Exception(message);
