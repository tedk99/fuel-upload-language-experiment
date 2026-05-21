namespace FuelUploadEngine.Tests;

public sealed class AuditProjectionTests
{
    [Fact]
    public void Accepted_row_projects_accepted_audit_event()
    {
        var audit = Project(new RowDecision.AcceptedTransaction(Transaction("txn-1")));

        Assert.Single(audit);
        Assert.Equal(AuditEventKind.Accepted, audit[0].Kind);
        Assert.Equal("accepted", AuditProjection.ToDto(audit[0]).Status);
        Assert.Equal("txn-1", audit[0].TransactionKey);
    }

    [Fact]
    public void Warning_row_projects_accepted_with_warnings_event()
    {
        var audit = Project(new RowDecision.AcceptedTransactionWithWarnings(
            Transaction("txn-2"),
            [new UploadWarning(WarningCode.QuantityAboveWarningThreshold)]));

        Assert.Equal(AuditEventKind.AcceptedWithWarnings, audit[0].Kind);
        Assert.Contains(WarningCode.QuantityAboveWarningThreshold, audit[0].Warnings);
        Assert.Empty(audit[0].QuarantineReasons);
    }

    [Fact]
    public void Rejected_row_projects_rejected_audit_event()
    {
        var audit = Project(new RowDecision.RejectedRow(
            new RowNumber(3),
            new RejectionReason.ValidationFailed([new ValidationError(ValidationErrorCode.NonPositiveQuantity)])));

        Assert.Equal(AuditEventKind.Rejected, audit[0].Kind);
        Assert.Equal(RejectionCode.ValidationFailed, audit[0].RejectionCode);
    }

    [Fact]
    public void Skipped_duplicate_projects_skipped_audit_event()
    {
        var audit = Project(new RowDecision.SkippedDuplicate(
            new RowNumber(4),
            new DuplicateState(new TransactionKey("existing"), new PreviousUploadOutcome.CanonicalFinalized()),
            DuplicateSkipCode.DuplicateInNormalMode));

        Assert.Equal(AuditEventKind.SkippedDuplicate, audit[0].Kind);
        Assert.Equal(DuplicateSkipCode.DuplicateInNormalMode, audit[0].DuplicateSkipCode);
        Assert.Equal("existing", audit[0].TransactionKey);
    }

    [Fact]
    public void Quarantined_row_projects_quarantined_audit_event_with_reasons()
    {
        var audit = Project(new RowDecision.QuarantinedRow(
            new RowNumber(5),
            Transaction("txn-5"),
            [new QuarantineReason(QuarantineReasonCode.SuspiciousMerchantName)],
            [new UploadWarning(WarningCode.UnitPriceAboveWarningThreshold)]));

        Assert.Equal(AuditEventKind.Quarantined, audit[0].Kind);
        Assert.Contains(QuarantineReasonCode.SuspiciousMerchantName, audit[0].QuarantineReasons);
        Assert.Contains(WarningCode.UnitPriceAboveWarningThreshold, audit[0].Warnings);
    }

    [Fact]
    public void Fatal_batch_projects_fatal_audit_event()
    {
        var audit = Project(new RowDecision.FatalProcessingError(
            new RowNumber(6),
            new FatalError(FatalErrorCode.DuplicateCheckUnavailable, "duplicate store timed out")));

        Assert.Equal(AuditEventKind.FatalBatch, audit[0].Kind);
        Assert.Equal("fatal_batch", AuditProjection.ToDto(audit[0]).Status);
        Assert.Equal(FatalErrorCode.DuplicateCheckUnavailable, audit[0].FatalCode);
    }

    [Fact]
    public void Audit_projection_does_not_recompute_classification()
    {
        var decision = new BatchDecision(
            [new RowDecision.AcceptedTransaction(Transaction("txn-inconsistent"))],
            new BatchSummary(99, 0, 0, 0, 0, 0, 99, 0, 0, 0));

        var audit = AuditProjection.Project(decision);

        Assert.Single(audit);
        Assert.Equal(AuditEventKind.Accepted, audit[0].Kind);
        Assert.Equal("txn-inconsistent", audit[0].TransactionKey);
    }

    private static IReadOnlyList<AuditRecord> Project(RowDecision decision)
    {
        return AuditProjection.Project(new BatchDecision(
            [decision],
            new BatchSummary(1, 0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }

    private static FuelTransaction Transaction(string key)
    {
        return new FuelTransaction(
            new TransactionKey(key),
            new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")),
            new DateOnly(2026, 5, 20),
            10m,
            2m,
            20m,
            new ExternalReference("line-1"));
    }
}
