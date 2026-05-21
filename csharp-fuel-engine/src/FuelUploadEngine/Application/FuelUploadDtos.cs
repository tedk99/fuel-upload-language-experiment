namespace FuelUploadEngine.Application;

public sealed record FuelUploadRequestDto(
    string? UploadMode,
    decimal MaximumQuantity,
    decimal MaximumUnitPrice,
    decimal WarningQuantity,
    decimal WarningUnitPrice,
    decimal SuspiciousQuantity,
    decimal SuspiciousTotalCost,
    string? Today,
    IReadOnlyList<FuelUploadRowDto>? Rows);

public sealed record FuelUploadRowDto(
    int RowNumber,
    string? VehicleIdentifier,
    string? TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    string? MerchantName,
    string? ExternalReference,
    string? VehicleLookupStatus,
    string? VehicleId,
    IReadOnlyList<string>? AmbiguousVehicleIds,
    string? VehicleLookupError,
    string? DuplicateStatus,
    string? TransactionKey,
    string? PreviousOutcome,
    bool CanonicalTransactionKeyPresent,
    string? DuplicateError);

public sealed record FuelUploadResponseDto(
    IReadOnlyList<FuelUploadDecisionDto> Decisions,
    int TotalRows,
    int AcceptedTransactions,
    int AcceptedWithWarnings,
    int QuarantinedRows,
    int SkippedDuplicates,
    int RejectedRows,
    int FatalRows,
    int WarningCount,
    int UploadableTransactions,
    bool HasFatalErrors);

public sealed record FuelUploadDecisionDto(
    int? RowNumber,
    string? SourceReference,
    string Outcome,
    string? TransactionKey,
    string? VehicleId,
    decimal? GrossAmount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> QuarantineReasons,
    string? RejectionCode,
    IReadOnlyList<string> ValidationErrors,
    string? DuplicateSkipCode,
    string? FatalCode,
    string? FatalDetail);

public enum FuelUploadMappingErrorCode
{
    MissingRows,
    MissingRequiredField,
    InvalidDate,
    InvalidUploadMode,
    InvalidVehicleLookupStatus,
    MissingVehicleLookupPayload,
    InvalidDuplicateStatus,
    MissingDuplicatePayload,
    InvalidPreviousOutcome
}

public sealed record FuelUploadMappingError(
    FuelUploadMappingErrorCode Code,
    string Field,
    string Detail);

public abstract record FuelUploadMapResult<T>
{
    private FuelUploadMapResult()
    {
    }

    public sealed record Success(T Value) : FuelUploadMapResult<T>;

    public sealed record Failure(IReadOnlyList<FuelUploadMappingError> Errors) : FuelUploadMapResult<T>;
}
