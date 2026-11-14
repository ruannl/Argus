namespace argus.Server.Data;

/// <summary>
/// Immutable request authority derived only by the finance guard from a
/// separately validated session. It is never serialized, logged or inferred
/// from request fields, and it never lives in a singleton or pooled context.
/// </summary>
public sealed record FinanceAuthority(Guid CustomerId, string SessionHash)
{
    public override string ToString() => $"FinanceAuthority({CustomerId:D})";
}

/// <summary>
/// A missing, invalid, stale or revoked customer authority. Callers map this to
/// the existing unauthorized response; it carries no identifiers or content.
/// </summary>
public sealed class FinanceAuthorityException() : Exception("finance authority is not valid");
