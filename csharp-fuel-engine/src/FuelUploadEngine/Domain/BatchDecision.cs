namespace FuelUploadEngine;

public sealed record BatchRowInput(
    FuelRow Row,
    VehicleLookupResult VehicleLookup,
    DuplicateCheckResult DuplicateCheck);

public sealed record BatchClassificationRequest(
    IReadOnlyList<BatchRowInput> Rows,
    ValidationConfig ValidationConfig,
    UploadMode Mode);

public sealed record BatchSummary(
    int TotalRows,
    int AcceptedTransactions,
    int AcceptedWithoutWarnings,
    int AcceptedWithWarnings,
    int SkippedDuplicates,
    int RejectedRows,
    int FatalRows,
    int WarningCount,
    int UploadableTransactions);

public sealed record BatchDecision(
    IReadOnlyList<RowDecision> RowDecisions,
    BatchSummary Summary)
{
    public bool HasFatalErrors => Summary.FatalRows > 0;
}
