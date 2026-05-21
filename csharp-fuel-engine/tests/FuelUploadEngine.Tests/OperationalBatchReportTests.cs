namespace FuelUploadEngine.Tests;

public sealed class OperationalBatchReportTests
{
    [Fact]
    public void Project_includes_decision_derived_operational_lists()
    {
        var decisions = new RowDecision[]
        {
            new RowDecision.AcceptedTransaction(Transaction("txn-1", 1)),
            new RowDecision.AcceptedTransactionWithWarnings(
                Transaction("txn-2", 2),
                new[] { new UploadWarning(WarningCode.QuantityAboveWarningThreshold) }),
            new RowDecision.RejectedRow(
                new RowNumber(3),
                new RejectionReason.ValidationFailed(new[] { new ValidationError(ValidationErrorCode.NonPositiveQuantity) })),
            new RowDecision.SkippedDuplicate(
                new RowNumber(4),
                new DuplicateState(new TransactionKey("existing"), new PreviousUploadOutcome.CanonicalFinalized()),
                DuplicateSkipCode.DuplicateInNormalMode),
            new RowDecision.QuarantinedRow(
                new RowNumber(5),
                Transaction("txn-quarantine", 5),
                new[] { new QuarantineReason(QuarantineReasonCode.SuspiciousMerchantName) },
                Array.Empty<UploadWarning>())
        };
        var batch = new BatchDecision(decisions, BatchSummaryCalculator.Summarize(decisions));

        var report = OperationalBatchReportProjector.Project(batch);

        Assert.Equal(OperationalBatchStatus.Ready, report.Status);
        Assert.Equal(new[] { new TransactionKey("txn-1"), new TransactionKey("txn-2") }, report.UploadedTransactionIds);
        Assert.Equal(new[] { new RowNumber(3) }, report.RejectedRowNumbers);
        Assert.Equal(new[] { new RowNumber(4) }, report.SkippedDuplicateRowNumbers);
        var quarantined = Assert.Single(report.QuarantinedRows);
        Assert.Equal(new RowNumber(5), quarantined.RowNumber);
        Assert.Contains(quarantined.Reasons, reason => reason.Code == QuarantineReasonCode.SuspiciousMerchantName);
    }

    [Fact]
    public void Project_fatal_batch_has_fatal_status_and_no_uploaded_transactions()
    {
        var decisions = new RowDecision[]
        {
            new RowDecision.AcceptedTransaction(Transaction("txn-accepted-but-blocked", 1)),
            new RowDecision.FatalProcessingError(
                new RowNumber(2),
                new FatalError(FatalErrorCode.DuplicateCheckUnavailable, "duplicate service down"))
        };
        var batch = new BatchDecision(decisions, BatchSummaryCalculator.Summarize(decisions));

        var report = OperationalBatchReportProjector.Project(batch);

        Assert.Equal(OperationalBatchStatus.Fatal, report.Status);
        Assert.Empty(report.UploadedTransactionIds);
        Assert.Equal(new[] { new FatalError(FatalErrorCode.DuplicateCheckUnavailable, "duplicate service down") }, report.FatalErrors);
    }

    [Fact]
    public void Project_counts_match_decision_summary_and_does_not_inspect_raw_rows()
    {
        var decisions = new RowDecision[]
        {
            new RowDecision.AcceptedTransaction(Transaction("txn-impossible", 99))
        };
        var summary = new BatchSummary(
            TotalRows: 10,
            AcceptedTransactions: 7,
            AcceptedWithoutWarnings: 6,
            AcceptedWithWarnings: 1,
            QuarantinedRows: 2,
            SkippedDuplicates: 3,
            RejectedRows: 4,
            FatalRows: 0,
            WarningCount: 5,
            UploadableTransactions: 7);
        var batch = new BatchDecision(decisions, summary);

        var report = OperationalBatchReportProjector.Project(batch);

        Assert.Same(summary, report.Counts);
        Assert.Equal(new[] { new TransactionKey("txn-impossible") }, report.UploadedTransactionIds);
    }

    private static FuelTransaction Transaction(string key, int rowNumber)
    {
        return new FuelTransaction(
            new TransactionKey(key),
            new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")),
            new DateOnly(2026, 5, 20),
            13m,
            3.50m,
            45.50m,
            new ExternalReference($"line-{rowNumber}"));
    }
}
