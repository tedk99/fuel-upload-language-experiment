namespace FuelUploadEngine;

public abstract record RowDecision
{
    private RowDecision()
    {
    }

    public sealed record AcceptedTransaction(FuelTransaction Transaction) : RowDecision;

    public sealed record AcceptedTransactionWithWarnings(
        FuelTransaction Transaction,
        IReadOnlyList<UploadWarning> Warnings) : RowDecision;

    public sealed record SkippedDuplicate(
        RowNumber RowNumber,
        DuplicateState Duplicate,
        DuplicateSkipCode Reason) : RowDecision;

    public sealed record RejectedRow(
        RowNumber RowNumber,
        RejectionReason Reason) : RowDecision;

    public sealed record FatalProcessingError(
        RowNumber RowNumber,
        FatalError Error) : RowDecision;
}
