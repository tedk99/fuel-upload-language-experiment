namespace FuelUploadEngine.Application;

public interface IVehicleRepository
{
    VehicleRepositoryResult Lookup(VehicleIdentifier identifier);
}

public interface IDuplicateRepository
{
    DuplicateRepositoryResult Lookup(DuplicateLookup lookup);
}

public sealed record DuplicateLookup(
    RowNumber RowNumber,
    VehicleIdentifier VehicleIdentifier,
    ExternalReference ExternalReference);

public sealed record VehicleRepositoryError(VehicleRepositoryErrorCode Code, string Detail);

public enum VehicleRepositoryErrorCode
{
    Unavailable,
    TimedOut
}

public sealed record DuplicateRepositoryError(DuplicateRepositoryErrorCode Code, string Detail);

public enum DuplicateRepositoryErrorCode
{
    Unavailable,
    TimedOut
}

public abstract record VehicleRepositoryResult
{
    private VehicleRepositoryResult()
    {
    }

    public sealed record Success(VehicleLookupResult Lookup) : VehicleRepositoryResult;

    public sealed record Failure(VehicleRepositoryError Error) : VehicleRepositoryResult;
}

public abstract record DuplicateRepositoryResult
{
    private DuplicateRepositoryResult()
    {
    }

    public sealed record Success(DuplicateCheckResult Lookup) : DuplicateRepositoryResult;

    public sealed record Failure(DuplicateRepositoryError Error) : DuplicateRepositoryResult;
}
