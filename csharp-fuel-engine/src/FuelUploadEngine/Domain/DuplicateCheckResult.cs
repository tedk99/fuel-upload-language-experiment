namespace FuelUploadEngine;

public sealed record DuplicateState(TransactionKey ExistingTransactionKey, PreviousUploadOutcome PreviousOutcome);

public abstract record DuplicateCheckResult
{
    private DuplicateCheckResult()
    {
    }

    public sealed record NotDuplicate(TransactionKey ProposedTransactionKey) : DuplicateCheckResult;

    public sealed record Duplicate(DuplicateState State) : DuplicateCheckResult;

    public sealed record Unavailable(FatalError Error) : DuplicateCheckResult;
}
