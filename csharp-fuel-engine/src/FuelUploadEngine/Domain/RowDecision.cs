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

    public sealed record QuarantinedRow : RowDecision
    {
        public QuarantinedRow(
            RowNumber rowNumber,
            FuelTransaction transaction,
            IReadOnlyList<QuarantineReason> reasons,
            IReadOnlyList<UploadWarning> warnings)
        {
            if (reasons.Count == 0)
            {
                throw new ArgumentException("Quarantined rows require at least one reason.", nameof(reasons));
            }

            RowNumber = rowNumber;
            Transaction = transaction;
            Reasons = reasons;
            Warnings = warnings;
        }

        public RowNumber RowNumber { get; }

        public FuelTransaction Transaction { get; }

        public IReadOnlyList<QuarantineReason> Reasons { get; }

        public IReadOnlyList<UploadWarning> Warnings { get; }
    }

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
