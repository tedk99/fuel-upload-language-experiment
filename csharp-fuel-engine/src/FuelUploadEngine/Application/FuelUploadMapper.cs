using System.Globalization;

namespace FuelUploadEngine.Application;

public static class FuelUploadMapper
{
    public static FuelUploadMapResult<BatchClassificationRequest> ToDomainRequest(FuelUploadRequestDto dto)
    {
        var errors = new List<FuelUploadMappingError>();

        var mode = ParseUploadMode(dto.UploadMode, "uploadMode", errors);
        var today = ParseDate(dto.Today, "today", errors);

        if (dto.Rows is null)
        {
            errors.Add(new FuelUploadMappingError(
                FuelUploadMappingErrorCode.MissingRows,
                "rows",
                "Rows are required."));
        }

        var rows = new List<BatchRowInput>();
        if (dto.Rows is not null)
        {
            for (var index = 0; index < dto.Rows.Count; index++)
            {
                var row = ToDomainRow(dto.Rows[index], index, errors);
                if (row is not null)
                {
                    rows.Add(row);
                }
            }
        }

        if (errors.Count > 0 || mode is null || today is null)
        {
            return new FuelUploadMapResult<BatchClassificationRequest>.Failure(errors);
        }

        var config = new ValidationConfig(
            dto.MaximumQuantity,
            dto.MaximumUnitPrice,
            dto.WarningQuantity,
            dto.WarningUnitPrice,
            dto.SuspiciousQuantity,
            dto.SuspiciousTotalCost,
            today.Value);

        return new FuelUploadMapResult<BatchClassificationRequest>.Success(
            new BatchClassificationRequest(rows, config, mode.Value));
    }

    public static FuelUploadResponseDto ToResponseDto(BatchDecision decision)
    {
        return new FuelUploadResponseDto(
            decision.RowDecisions.Select(ToDecisionDto).ToArray(),
            decision.Summary.TotalRows,
            decision.Summary.AcceptedTransactions,
            decision.Summary.AcceptedWithWarnings,
            decision.Summary.QuarantinedRows,
            decision.Summary.SkippedDuplicates,
            decision.Summary.RejectedRows,
            decision.Summary.FatalRows,
            decision.Summary.WarningCount,
            decision.Summary.UploadableTransactions,
            decision.HasFatalErrors);
    }

    private static BatchRowInput? ToDomainRow(
        FuelUploadRowDto dto,
        int index,
        List<FuelUploadMappingError> errors)
    {
        var prefix = $"rows[{index}]";
        var vehicleIdentifier = Require(dto.VehicleIdentifier, $"{prefix}.vehicleIdentifier", errors);
        var transactionDate = ParseDate(dto.TransactionDate, $"{prefix}.transactionDate", errors);
        var merchantName = Require(dto.MerchantName, $"{prefix}.merchantName", errors);
        var externalReference = Require(dto.ExternalReference, $"{prefix}.externalReference", errors);
        var vehicleLookup = ParseVehicleLookup(dto, prefix, vehicleIdentifier, errors);
        var duplicateCheck = ParseDuplicateCheck(dto, prefix, errors);

        if (vehicleIdentifier is null
            || transactionDate is null
            || merchantName is null
            || externalReference is null
            || vehicleLookup is null
            || duplicateCheck is null)
        {
            return null;
        }

        var row = new FuelRow(
            new RowNumber(dto.RowNumber),
            new VehicleIdentifier(vehicleIdentifier),
            transactionDate.Value,
            dto.Quantity,
            dto.UnitPrice,
            merchantName,
            new ExternalReference(externalReference));

        return new BatchRowInput(row, vehicleLookup, duplicateCheck);
    }

    private static VehicleLookupResult? ParseVehicleLookup(
        FuelUploadRowDto dto,
        string prefix,
        string? requestedIdentifier,
        List<FuelUploadMappingError> errors)
    {
        return Normalize(dto.VehicleLookupStatus) switch
        {
            "found" => ParseFoundVehicle(dto, prefix, requestedIdentifier, errors),
            "notfound" => requestedIdentifier is null
                ? null
                : new VehicleLookupResult.NotFound(new VehicleIdentifier(requestedIdentifier)),
            "ambiguous" => ParseAmbiguousVehicles(dto, prefix, requestedIdentifier, errors),
            "unavailable" => new VehicleLookupResult.Unavailable(new FatalError(
                FatalErrorCode.VehicleLookupUnavailable,
                Require(dto.VehicleLookupError, $"{prefix}.vehicleLookupError", errors) ?? string.Empty)),
            _ => InvalidVehicleLookupStatus(prefix, dto.VehicleLookupStatus, errors)
        };
    }

    private static VehicleLookupResult? ParseFoundVehicle(
        FuelUploadRowDto dto,
        string prefix,
        string? requestedIdentifier,
        List<FuelUploadMappingError> errors)
    {
        var vehicleId = Require(dto.VehicleId, $"{prefix}.vehicleId", errors);
        if (vehicleId is null || requestedIdentifier is null)
        {
            return null;
        }

        return new VehicleLookupResult.Found(
            new Vehicle(new VehicleId(vehicleId), new VehicleIdentifier(requestedIdentifier)));
    }

    private static VehicleLookupResult? ParseAmbiguousVehicles(
        FuelUploadRowDto dto,
        string prefix,
        string? requestedIdentifier,
        List<FuelUploadMappingError> errors)
    {
        if (requestedIdentifier is null)
        {
            return null;
        }

        if (dto.AmbiguousVehicleIds is null || dto.AmbiguousVehicleIds.Count == 0)
        {
            errors.Add(new FuelUploadMappingError(
                FuelUploadMappingErrorCode.MissingVehicleLookupPayload,
                $"{prefix}.ambiguousVehicleIds",
                "Ambiguous vehicle lookup requires at least one candidate id."));
            return null;
        }

        var candidates = dto.AmbiguousVehicleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new Vehicle(new VehicleId(id.Trim()), new VehicleIdentifier(requestedIdentifier)))
            .ToArray();

        return new VehicleLookupResult.Ambiguous(new VehicleIdentifier(requestedIdentifier), candidates);
    }

    private static VehicleLookupResult? InvalidVehicleLookupStatus(
        string prefix,
        string? value,
        List<FuelUploadMappingError> errors)
    {
        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.InvalidVehicleLookupStatus,
            $"{prefix}.vehicleLookupStatus",
            $"Unsupported vehicle lookup status '{value}'."));
        return null;
    }

    private static DuplicateCheckResult? ParseDuplicateCheck(
        FuelUploadRowDto dto,
        string prefix,
        List<FuelUploadMappingError> errors)
    {
        return Normalize(dto.DuplicateStatus) switch
        {
            "notduplicate" => ParseNotDuplicate(dto, prefix, errors),
            "duplicate" => ParseDuplicate(dto, prefix, errors),
            "unavailable" => new DuplicateCheckResult.Unavailable(new FatalError(
                FatalErrorCode.DuplicateCheckUnavailable,
                Require(dto.DuplicateError, $"{prefix}.duplicateError", errors) ?? string.Empty)),
            _ => InvalidDuplicateStatus(prefix, dto.DuplicateStatus, errors)
        };
    }

    private static DuplicateCheckResult? ParseNotDuplicate(
        FuelUploadRowDto dto,
        string prefix,
        List<FuelUploadMappingError> errors)
    {
        var transactionKey = Require(dto.TransactionKey, $"{prefix}.transactionKey", errors);
        return transactionKey is null
            ? null
            : new DuplicateCheckResult.NotDuplicate(new TransactionKey(transactionKey));
    }

    private static DuplicateCheckResult? ParseDuplicate(
        FuelUploadRowDto dto,
        string prefix,
        List<FuelUploadMappingError> errors)
    {
        var transactionKey = Require(dto.TransactionKey, $"{prefix}.transactionKey", errors);
        var previousOutcome = ParsePreviousOutcome(dto.PreviousOutcome, $"{prefix}.previousOutcome", errors);

        if (transactionKey is null || previousOutcome is null)
        {
            return null;
        }

        CanonicalTransactionKeyState canonicalState = dto.CanonicalTransactionKeyPresent
            ? new CanonicalTransactionKeyState.Present(new TransactionKey(transactionKey))
            : new CanonicalTransactionKeyState.Missing();

        return new DuplicateCheckResult.Duplicate(
            new DuplicateState(new TransactionKey(transactionKey), previousOutcome, canonicalState));
    }

    private static DuplicateCheckResult? InvalidDuplicateStatus(
        string prefix,
        string? value,
        List<FuelUploadMappingError> errors)
    {
        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.InvalidDuplicateStatus,
            $"{prefix}.duplicateStatus",
            $"Unsupported duplicate status '{value}'."));
        return null;
    }

    private static PreviousUploadOutcome? ParsePreviousOutcome(
        string? value,
        string field,
        List<FuelUploadMappingError> errors)
    {
        return Normalize(value) switch
        {
            "canonicalfinalized" => new PreviousUploadOutcome.CanonicalFinalized(),
            "retryablefailure" => new PreviousUploadOutcome.RetryableFailure(),
            "nonretryablefailure" => new PreviousUploadOutcome.NonRetryableFailure(),
            "failedbeforecanonicalfinalization" => new PreviousUploadOutcome.FailedBeforeCanonicalFinalization(),
            "failedaftercanonicalfinalization" => new PreviousUploadOutcome.FailedAfterCanonicalFinalization(),
            _ => InvalidPreviousOutcome(field, value, errors)
        };
    }

    private static PreviousUploadOutcome? InvalidPreviousOutcome(
        string field,
        string? value,
        List<FuelUploadMappingError> errors)
    {
        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.InvalidPreviousOutcome,
            field,
            $"Unsupported previous outcome '{value}'."));
        return null;
    }

    private static string? Require(string? value, string field, List<FuelUploadMappingError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.MissingRequiredField,
            field,
            "A non-empty value is required."));
        return null;
    }

    private static DateOnly? ParseDate(string? value, string field, List<FuelUploadMappingError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(required, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.InvalidDate,
            field,
            "Date must use yyyy-MM-dd format."));
        return null;
    }

    private static UploadMode? ParseUploadMode(
        string? value,
        string field,
        List<FuelUploadMappingError> errors)
    {
        return Normalize(value) switch
        {
            "normal" => UploadMode.Normal,
            "retry" => UploadMode.Retry,
            "conservativerecovery" => UploadMode.ConservativeRecovery,
            "aggressiverecovery" => UploadMode.AggressiveRecovery,
            _ => InvalidUploadMode(field, value, errors)
        };
    }

    private static UploadMode? InvalidUploadMode(
        string field,
        string? value,
        List<FuelUploadMappingError> errors)
    {
        errors.Add(new FuelUploadMappingError(
            FuelUploadMappingErrorCode.InvalidUploadMode,
            field,
            $"Unsupported upload mode '{value}'."));
        return null;
    }

    private static FuelUploadDecisionDto ToDecisionDto(RowDecision decision)
    {
        return decision switch
        {
            RowDecision.AcceptedTransaction accepted => AcceptedDto("accepted", accepted.Transaction, []),
            RowDecision.AcceptedTransactionWithWarnings accepted => AcceptedDto(
                "accepted_with_warnings",
                accepted.Transaction,
                accepted.Warnings.Select(warning => warning.Code.ToString()).ToArray()),
            RowDecision.QuarantinedRow quarantined => new FuelUploadDecisionDto(
                quarantined.RowNumber.Value,
                quarantined.Transaction.SourceReference.Value,
                "quarantined",
                quarantined.Transaction.Key.Value,
                quarantined.Transaction.Vehicle.Id.Value,
                quarantined.Transaction.GrossAmount,
                quarantined.Warnings.Select(warning => warning.Code.ToString()).ToArray(),
                quarantined.Reasons.Select(reason => reason.Code.ToString()).ToArray(),
                null,
                [],
                null,
                null,
                null),
            RowDecision.SkippedDuplicate skipped => new FuelUploadDecisionDto(
                skipped.RowNumber.Value,
                null,
                "skipped_duplicate",
                skipped.Duplicate.ExistingTransactionKey.Value,
                null,
                null,
                [],
                [],
                null,
                [],
                skipped.Reason.ToString(),
                null,
                null),
            RowDecision.RejectedRow rejected => RejectedDto(rejected),
            RowDecision.FatalProcessingError fatal => new FuelUploadDecisionDto(
                fatal.RowNumber.Value,
                null,
                "fatal",
                null,
                null,
                null,
                [],
                [],
                null,
                [],
                null,
                fatal.Error.Code.ToString(),
                fatal.Error.Detail),
            _ => throw new InvalidOperationException("Unhandled row decision.")
        };
    }

    private static FuelUploadDecisionDto AcceptedDto(
        string outcome,
        FuelTransaction transaction,
        IReadOnlyList<string> warnings)
    {
        return new FuelUploadDecisionDto(
            null,
            transaction.SourceReference.Value,
            outcome,
            transaction.Key.Value,
            transaction.Vehicle.Id.Value,
            transaction.GrossAmount,
            warnings,
            [],
            null,
            [],
            null,
            null,
            null);
    }

    private static FuelUploadDecisionDto RejectedDto(RowDecision.RejectedRow rejected)
    {
        var validationErrors = rejected.Reason is RejectionReason.ValidationFailed validation
            ? validation.Errors.Select(error => error.Code.ToString()).ToArray()
            : [];

        return new FuelUploadDecisionDto(
            rejected.RowNumber.Value,
            null,
            "rejected",
            null,
            null,
            null,
            [],
            [],
            rejected.Reason.Code.ToString(),
            validationErrors,
            null,
            null,
            null);
    }

    private static string Normalize(string? value)
    {
        return value?.Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant()
            ?? string.Empty;
    }
}
