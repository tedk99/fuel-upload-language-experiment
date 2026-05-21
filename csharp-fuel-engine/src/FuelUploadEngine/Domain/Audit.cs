namespace FuelUploadEngine;

public enum AuditEventKind
{
    Accepted,
    AcceptedWithWarnings,
    Rejected,
    SkippedDuplicate,
    Quarantined,
    FatalBatch
}

public sealed record AuditRecord(
    AuditEventKind Kind,
    int? RowNumber,
    string? SourceReference,
    string? TransactionKey,
    string? VehicleId,
    IReadOnlyList<WarningCode> Warnings,
    IReadOnlyList<QuarantineReasonCode> QuarantineReasons,
    RejectionCode? RejectionCode,
    DuplicateSkipCode? DuplicateSkipCode,
    FatalErrorCode? FatalCode,
    string? FatalDetail);

public sealed record AuditRecordDto(
    string Status,
    int? RowNumber,
    string? SourceReference,
    string? TransactionKey,
    string? VehicleId,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> QuarantineReasons,
    string? RejectionCode,
    string? DuplicateSkipCode,
    string? FatalCode,
    string? FatalDetail);

public static class AuditProjection
{
    public static IReadOnlyList<AuditRecord> Project(BatchDecision decision)
    {
        return decision.RowDecisions.Select(Project).ToArray();
    }

    public static AuditRecordDto ToDto(AuditRecord record)
    {
        return new AuditRecordDto(
            Status(record.Kind),
            record.RowNumber,
            record.SourceReference,
            record.TransactionKey,
            record.VehicleId,
            record.Warnings.Select(warning => warning.ToString()).ToArray(),
            record.QuarantineReasons.Select(reason => reason.ToString()).ToArray(),
            record.RejectionCode?.ToString(),
            record.DuplicateSkipCode?.ToString(),
            record.FatalCode?.ToString(),
            record.FatalDetail);
    }

    public static string Status(AuditEventKind kind)
    {
        return kind switch
        {
            AuditEventKind.Accepted => "accepted",
            AuditEventKind.AcceptedWithWarnings => "accepted_with_warnings",
            AuditEventKind.Rejected => "rejected",
            AuditEventKind.SkippedDuplicate => "skipped_duplicate",
            AuditEventKind.Quarantined => "quarantined",
            AuditEventKind.FatalBatch => "fatal_batch",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported audit event kind.")
        };
    }

    private static AuditRecord Project(RowDecision decision)
    {
        return decision switch
        {
            RowDecision.AcceptedTransaction accepted => TransactionRecord(
                AuditEventKind.Accepted,
                accepted.Transaction,
                []),
            RowDecision.AcceptedTransactionWithWarnings accepted => TransactionRecord(
                AuditEventKind.AcceptedWithWarnings,
                accepted.Transaction,
                accepted.Warnings),
            RowDecision.QuarantinedRow quarantined => new AuditRecord(
                AuditEventKind.Quarantined,
                quarantined.RowNumber.Value,
                quarantined.Transaction.SourceReference.Value,
                quarantined.Transaction.Key.Value,
                quarantined.Transaction.Vehicle.Id.Value,
                quarantined.Warnings.Select(warning => warning.Code).ToArray(),
                quarantined.Reasons.Select(reason => reason.Code).ToArray(),
                null,
                null,
                null,
                null),
            RowDecision.SkippedDuplicate skipped => new AuditRecord(
                AuditEventKind.SkippedDuplicate,
                skipped.RowNumber.Value,
                null,
                skipped.Duplicate.ExistingTransactionKey.Value,
                null,
                [],
                [],
                null,
                skipped.Reason,
                null,
                null),
            RowDecision.RejectedRow rejected => new AuditRecord(
                AuditEventKind.Rejected,
                rejected.RowNumber.Value,
                null,
                null,
                null,
                [],
                [],
                rejected.Reason.Code,
                null,
                null,
                null),
            RowDecision.FatalProcessingError fatal => new AuditRecord(
                AuditEventKind.FatalBatch,
                fatal.RowNumber.Value,
                null,
                null,
                null,
                [],
                [],
                null,
                null,
                fatal.Error.Code,
                fatal.Error.Detail),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unsupported row decision.")
        };
    }

    private static AuditRecord TransactionRecord(
        AuditEventKind kind,
        FuelTransaction transaction,
        IReadOnlyList<UploadWarning> warnings)
    {
        return new AuditRecord(
            kind,
            null,
            transaction.SourceReference.Value,
            transaction.Key.Value,
            transaction.Vehicle.Id.Value,
            warnings.Select(warning => warning.Code).ToArray(),
            [],
            null,
            null,
            null,
            null);
    }
}
