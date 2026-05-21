namespace FuelUploadEngine;

public readonly record struct RowNumber(int Value);

public readonly record struct VehicleIdentifier(string Value);

public readonly record struct VehicleId(string Value);

public readonly record struct ExternalReference(string Value);

public readonly record struct TransactionKey(string Value);

public enum UploadMode
{
    Normal,
    Retry,
    Recovery
}

public enum ValidationErrorCode
{
    MissingVehicleIdentifier,
    NonPositiveQuantity,
    QuantityExceedsMaximum,
    NegativeUnitPrice,
    UnitPriceExceedsMaximum,
    TransactionDateInFuture
}

public enum WarningCode
{
    QuantityAboveWarningThreshold,
    UnitPriceAboveWarningThreshold
}

public enum QuarantineReasonCode
{
    SuspiciousMerchantName,
    SuspiciousQuantityPattern,
    SuspiciousCostPattern
}

public enum DuplicateSkipCode
{
    DuplicateInNormalMode,
    PreviousAttemptNotRetryable,
    PreviousAttemptAlreadyCanonicalized
}

public enum RejectionCode
{
    ValidationFailed,
    VehicleNotFound,
    AmbiguousVehicle
}

public enum FatalErrorCode
{
    VehicleLookupUnavailable,
    DuplicateCheckUnavailable
}
