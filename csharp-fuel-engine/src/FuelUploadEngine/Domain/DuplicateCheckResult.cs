namespace FuelUploadEngine;

public sealed record DuplicateState(
    TransactionKey ExistingTransactionKey,
    PreviousUploadOutcome PreviousOutcome,
    CanonicalTransactionKeyState CanonicalTransactionKey)
{
    public DuplicateState(TransactionKey existingTransactionKey, PreviousUploadOutcome previousOutcome)
        : this(
            existingTransactionKey,
            previousOutcome,
            new CanonicalTransactionKeyState.Present(existingTransactionKey))
    {
    }
}

public abstract record CanonicalTransactionKeyState
{
    private CanonicalTransactionKeyState()
    {
    }

    public sealed record Present(TransactionKey Key) : CanonicalTransactionKeyState;

    public sealed record Missing : CanonicalTransactionKeyState;
}

public abstract record DuplicateCheckResult
{
    private DuplicateCheckResult()
    {
    }

    public sealed record NotDuplicate(TransactionKey ProposedTransactionKey) : DuplicateCheckResult;

    public sealed record Duplicate(DuplicateState State) : DuplicateCheckResult;

    public sealed record Unavailable(FatalError Error) : DuplicateCheckResult;
}
