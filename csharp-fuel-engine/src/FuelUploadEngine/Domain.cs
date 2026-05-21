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

public sealed record FuelRow(
    RowNumber RowNumber,
    VehicleIdentifier VehicleIdentifier,
    DateOnly TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    ExternalReference ExternalReference);

public sealed record Vehicle(VehicleId Id, VehicleIdentifier Identifier);

public sealed record FuelTransaction(
    TransactionKey Key,
    Vehicle Vehicle,
    DateOnly TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    ExternalReference SourceReference);

public sealed record ValidationConfig(
    decimal MaximumQuantity,
    decimal MaximumUnitPrice,
    decimal WarningQuantity,
    decimal WarningUnitPrice,
    DateOnly Today);

public sealed record ValidationError(ValidationErrorCode Code);

public sealed record UploadWarning(WarningCode Code);

public sealed record FatalError(FatalErrorCode Code, string Detail);

public abstract record VehicleLookupResult
{
    private VehicleLookupResult()
    {
    }

    public sealed record Found(Vehicle Vehicle) : VehicleLookupResult;

    public sealed record NotFound(VehicleIdentifier Identifier) : VehicleLookupResult;

    public sealed record Ambiguous(VehicleIdentifier Identifier, IReadOnlyList<Vehicle> Candidates) : VehicleLookupResult;

    public sealed record Unavailable(FatalError Error) : VehicleLookupResult;
}

public abstract record PreviousUploadOutcome
{
    private PreviousUploadOutcome()
    {
    }

    public sealed record CanonicalFinalized : PreviousUploadOutcome;

    public sealed record RetryableFailure : PreviousUploadOutcome;

    public sealed record NonRetryableFailure : PreviousUploadOutcome;

    public sealed record FailedBeforeCanonicalFinalization : PreviousUploadOutcome;

    public sealed record FailedAfterCanonicalFinalization : PreviousUploadOutcome;
}

public sealed record DuplicateState(TransactionKey ExistingTransactionKey, PreviousUploadOutcome PreviousOutcome);

public abstract record DuplicateCheckResult
{
    private DuplicateCheckResult()
    {
    }

    public sealed record NotDuplicate(TransactionKey ProposedTransactionKey) : DuplicateCheckResult;

    public sealed record Duplicate(DuplicateState State) : DuplicateCheckResult;

    public sealed record Unavailable(FatalError Error) : DuplicateCheckResult;
}

public abstract record RejectionReason
{
    private RejectionReason()
    {
    }

    public abstract RejectionCode Code { get; }

    public sealed record ValidationFailed(IReadOnlyList<ValidationError> Errors) : RejectionReason
    {
        public override RejectionCode Code => RejectionCode.ValidationFailed;
    }

    public sealed record VehicleNotFound(VehicleIdentifier Identifier) : RejectionReason
    {
        public override RejectionCode Code => RejectionCode.VehicleNotFound;
    }

    public sealed record AmbiguousVehicle(VehicleIdentifier Identifier, IReadOnlyList<Vehicle> Candidates) : RejectionReason
    {
        public override RejectionCode Code => RejectionCode.AmbiguousVehicle;
    }
}

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
