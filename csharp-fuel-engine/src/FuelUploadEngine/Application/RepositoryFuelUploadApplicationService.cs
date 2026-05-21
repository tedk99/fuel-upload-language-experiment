namespace FuelUploadEngine.Application;

public sealed class RepositoryFuelUploadApplicationService(
    IVehicleRepository vehicleRepository,
    IDuplicateRepository duplicateRepository)
{
    public FuelUploadMapResult<FuelUploadResponseDto> Classify(FuelUploadRequestDto request)
    {
        var rows = request.Rows?.Select(ResolveRow).ToArray();
        var resolvedRequest = request with { Rows = rows };
        return new FuelUploadApplicationService().Classify(resolvedRequest);
    }

    private FuelUploadRowDto ResolveRow(FuelUploadRowDto row)
    {
        if (string.IsNullOrWhiteSpace(row.VehicleIdentifier)
            || string.IsNullOrWhiteSpace(row.ExternalReference))
        {
            return row;
        }

        var identifier = new VehicleIdentifier(row.VehicleIdentifier.Trim());
        var vehicleLookup = vehicleRepository.Lookup(identifier);
        var duplicateLookup = ResolveDuplicate(row, identifier, vehicleLookup);

        return row with
        {
            VehicleLookupStatus = VehicleLookupStatus(vehicleLookup),
            VehicleId = VehicleId(vehicleLookup),
            AmbiguousVehicleIds = AmbiguousVehicleIds(vehicleLookup),
            VehicleLookupError = VehicleLookupError(vehicleLookup),
            DuplicateStatus = DuplicateStatus(duplicateLookup),
            TransactionKey = TransactionKey(duplicateLookup),
            PreviousOutcome = PreviousOutcome(duplicateLookup),
            CanonicalTransactionKeyPresent = CanonicalTransactionKeyPresent(duplicateLookup),
            DuplicateError = DuplicateError(duplicateLookup)
        };
    }

    private DuplicateRepositoryResult ResolveDuplicate(
        FuelUploadRowDto row,
        VehicleIdentifier identifier,
        VehicleRepositoryResult vehicleLookup)
    {
        if (vehicleLookup is VehicleRepositoryResult.Failure)
        {
            return new DuplicateRepositoryResult.Success(
                new DuplicateCheckResult.NotDuplicate(new TransactionKey(row.ExternalReference!.Trim())));
        }

        return duplicateRepository.Lookup(
            new DuplicateLookup(
                new RowNumber(row.RowNumber),
                identifier,
                new ExternalReference(row.ExternalReference!.Trim())));
    }

    private static string VehicleLookupStatus(VehicleRepositoryResult result)
    {
        return result switch
        {
            VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Found } => "found",
            VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.NotFound } => "not_found",
            VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Ambiguous } => "ambiguous",
            VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Unavailable } => "unavailable",
            VehicleRepositoryResult.Failure => "unavailable",
            _ => throw new InvalidOperationException("Unhandled vehicle repository result.")
        };
    }

    private static string? VehicleId(VehicleRepositoryResult result)
    {
        return result is VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Found found }
            ? found.Vehicle.Id.Value
            : null;
    }

    private static IReadOnlyList<string>? AmbiguousVehicleIds(VehicleRepositoryResult result)
    {
        return result is VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Ambiguous ambiguous }
            ? ambiguous.Candidates.Select(candidate => candidate.Id.Value).ToArray()
            : null;
    }

    private static string? VehicleLookupError(VehicleRepositoryResult result)
    {
        return result switch
        {
            VehicleRepositoryResult.Success { Lookup: VehicleLookupResult.Unavailable unavailable } => unavailable.Error.Detail,
            VehicleRepositoryResult.Failure failure => failure.Error.Detail,
            _ => null
        };
    }

    private static string DuplicateStatus(DuplicateRepositoryResult result)
    {
        return result switch
        {
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.NotDuplicate } => "not_duplicate",
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Duplicate } => "duplicate",
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Unavailable } => "unavailable",
            DuplicateRepositoryResult.Failure => "unavailable",
            _ => throw new InvalidOperationException("Unhandled duplicate repository result.")
        };
    }

    private static string? TransactionKey(DuplicateRepositoryResult result)
    {
        return result switch
        {
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.NotDuplicate notDuplicate } =>
                notDuplicate.ProposedTransactionKey.Value,
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Duplicate duplicate } =>
                duplicate.State.ExistingTransactionKey.Value,
            _ => null
        };
    }

    private static string? PreviousOutcome(DuplicateRepositoryResult result)
    {
        return result is DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Duplicate duplicate }
            ? duplicate.State.PreviousOutcome switch
            {
                PreviousUploadOutcome.CanonicalFinalized => "canonical_finalized",
                PreviousUploadOutcome.RetryableFailure => "retryable_failure",
                PreviousUploadOutcome.NonRetryableFailure => "non_retryable_failure",
                PreviousUploadOutcome.FailedBeforeCanonicalFinalization => "failed_before_canonical_finalization",
                PreviousUploadOutcome.FailedAfterCanonicalFinalization => "failed_after_canonical_finalization",
                _ => throw new InvalidOperationException("Unhandled previous outcome.")
            }
            : null;
    }

    private static bool CanonicalTransactionKeyPresent(DuplicateRepositoryResult result)
    {
        return result is not DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Duplicate duplicate }
            || duplicate.State.CanonicalTransactionKey is CanonicalTransactionKeyState.Present;
    }

    private static string? DuplicateError(DuplicateRepositoryResult result)
    {
        return result switch
        {
            DuplicateRepositoryResult.Success { Lookup: DuplicateCheckResult.Unavailable unavailable } => unavailable.Error.Detail,
            DuplicateRepositoryResult.Failure failure => failure.Error.Detail,
            _ => null
        };
    }
}
