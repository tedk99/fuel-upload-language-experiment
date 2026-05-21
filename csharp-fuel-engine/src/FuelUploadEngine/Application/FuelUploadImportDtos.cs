namespace FuelUploadEngine.Application;

public sealed record ImportBatchRequest(
    string? UploadMode,
    string? MaximumQuantity,
    string? MaximumUnitPrice,
    string? WarningQuantity,
    string? WarningUnitPrice,
    string? SuspiciousQuantity,
    string? SuspiciousTotalCost,
    string? Today,
    IReadOnlyList<ImportedFuelRow>? Rows);

public sealed record ImportedFuelRow(
    string? RowNumber,
    string? VehicleIdentifier,
    string? TransactionDate,
    string? Quantity,
    string? UnitPrice,
    string? MerchantName,
    string? ExternalReference,
    string? VehicleLookupStatus,
    string? VehicleId,
    IReadOnlyList<string>? AmbiguousVehicleIds,
    string? VehicleLookupError,
    string? DuplicateStatus,
    string? TransactionKey,
    string? PreviousOutcome,
    string? CanonicalTransactionKeyPresent,
    string? DuplicateError);

public enum FuelImportErrorCode
{
    MissingRows,
    MissingRequiredCell,
    InvalidNumber,
    InvalidDate,
    InvalidUploadMode,
    InvalidBoolean
}

public sealed record FuelImportError(
    FuelImportErrorCode Code,
    string Field,
    string Detail);

public abstract record FuelImportMapResult<T>
{
    private FuelImportMapResult()
    {
    }

    public sealed record Success(T Value) : FuelImportMapResult<T>;

    public sealed record Failure(IReadOnlyList<FuelImportError> Errors) : FuelImportMapResult<T>;
}
