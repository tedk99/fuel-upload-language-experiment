namespace FuelUploadEngine;

public sealed record ValidationConfig(
    decimal MaximumQuantity,
    decimal MaximumUnitPrice,
    decimal WarningQuantity,
    decimal WarningUnitPrice,
    decimal SuspiciousQuantity,
    decimal SuspiciousTotalCost,
    DateOnly Today);

public sealed record ValidationError(ValidationErrorCode Code);

public sealed record UploadWarning(WarningCode Code);

public sealed record QuarantineReason(QuarantineReasonCode Code);

public sealed record FatalError(FatalErrorCode Code, string Detail);

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
