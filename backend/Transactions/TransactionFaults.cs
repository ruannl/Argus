namespace argus.Server.Transactions;

/// <summary>
/// Test-only insertion points for durable-effect fault evidence. The
/// production registration is a no-op; no request or environment value can
/// enable a failure.
/// </summary>
public static class TransactionFaultPoints
{
    public const string AfterTransactionUpdate = "after_transaction_update";
    public const string AfterTagLinkMutation = "after_tag_link_mutation";
    public const string BeforeReceiptWrite = "before_receipt_write";
    public const string AfterReceiptWrite = "after_receipt_write";
    public const string AfterCommit = "after_commit";
}

public interface ITransactionFaultInjector
{
    void OnStep(string step);
}

public sealed class NoopTransactionFaultInjector : ITransactionFaultInjector
{
    public static readonly NoopTransactionFaultInjector Instance = new();

    public void OnStep(string step)
    {
    }
}
