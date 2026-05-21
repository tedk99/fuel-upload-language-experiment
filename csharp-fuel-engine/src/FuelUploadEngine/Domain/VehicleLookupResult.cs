namespace FuelUploadEngine;

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
