namespace FuelUploadEngine;

public enum OperationalBatchStatus
{
    Ready,
    Fatal
}

public sealed record OperationalQuarantinedRow(
    RowNumber RowNumber,
    IReadOnlyList<QuarantineReason> Reasons);

public sealed record OperationalBatchReport(
    OperationalBatchStatus Status,
    BatchSummary Counts,
    IReadOnlyList<TransactionKey> UploadedTransactionIds,
    IReadOnlyList<RowNumber> RejectedRowNumbers,
    IReadOnlyList<OperationalQuarantinedRow> QuarantinedRows,
    IReadOnlyList<RowNumber> SkippedDuplicateRowNumbers,
    IReadOnlyList<FatalError> FatalErrors);

public static class OperationalBatchReportProjector
{
    public static OperationalBatchReport Project(BatchDecision decision)
    {
        var status = decision.HasFatalErrors ? OperationalBatchStatus.Fatal : OperationalBatchStatus.Ready;
        var uploadedTransactionIds = status == OperationalBatchStatus.Fatal
            ? Array.Empty<TransactionKey>()
            : decision.RowDecisions
                .Select(UploadedTransactionId)
                .OfType<TransactionKey>()
                .ToArray();

        return new OperationalBatchReport(
            status,
            decision.Summary,
            uploadedTransactionIds,
            decision.RowDecisions
                .OfType<RowDecision.RejectedRow>()
                .Select(rejected => rejected.RowNumber)
                .ToArray(),
            decision.RowDecisions
                .OfType<RowDecision.QuarantinedRow>()
                .Select(quarantined => new OperationalQuarantinedRow(
                    quarantined.RowNumber,
                    quarantined.Reasons))
                .ToArray(),
            decision.RowDecisions
                .OfType<RowDecision.SkippedDuplicate>()
                .Select(skipped => skipped.RowNumber)
                .ToArray(),
            decision.RowDecisions
                .OfType<RowDecision.FatalProcessingError>()
                .Select(fatal => fatal.Error)
                .ToArray());
    }

    private static TransactionKey? UploadedTransactionId(RowDecision decision)
    {
        return decision switch
        {
            RowDecision.AcceptedTransaction accepted => accepted.Transaction.Key,
            RowDecision.AcceptedTransactionWithWarnings accepted => accepted.Transaction.Key,
            _ => null
        };
    }
}
