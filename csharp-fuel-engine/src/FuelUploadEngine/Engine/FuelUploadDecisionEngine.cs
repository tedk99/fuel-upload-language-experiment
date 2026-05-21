namespace FuelUploadEngine;

public static class FuelUploadDecisionEngine
{
    public static RowDecision ClassifyRow(
        FuelRow row,
        VehicleLookupResult vehicleLookup,
        DuplicateCheckResult duplicateCheck,
        ValidationConfig validationConfig,
        UploadMode mode)
    {
        if (vehicleLookup is VehicleLookupResult.Unavailable vehicleUnavailable)
        {
            return new RowDecision.FatalProcessingError(row.RowNumber, vehicleUnavailable.Error);
        }

        if (duplicateCheck is DuplicateCheckResult.Unavailable duplicateUnavailable)
        {
            return new RowDecision.FatalProcessingError(row.RowNumber, duplicateUnavailable.Error);
        }

        var validationErrors = FuelRowValidator.Validate(row, validationConfig);
        if (validationErrors.Count > 0)
        {
            return new RowDecision.RejectedRow(row.RowNumber, new RejectionReason.ValidationFailed(validationErrors));
        }

        if (vehicleLookup is VehicleLookupResult.NotFound notFound)
        {
            return new RowDecision.RejectedRow(
                row.RowNumber,
                new RejectionReason.VehicleNotFound(notFound.Identifier));
        }

        if (vehicleLookup is VehicleLookupResult.Ambiguous ambiguous)
        {
            return new RowDecision.RejectedRow(
                row.RowNumber,
                new RejectionReason.AmbiguousVehicle(ambiguous.Identifier, ambiguous.Candidates));
        }

        var vehicle = vehicleLookup is VehicleLookupResult.Found found
            ? found.Vehicle
            : throw new InvalidOperationException("Unhandled vehicle lookup result.");

        return duplicateCheck switch
        {
            DuplicateCheckResult.NotDuplicate notDuplicate => CreateAcceptedDecision(
                row,
                vehicle,
                notDuplicate.ProposedTransactionKey,
                validationConfig),
            DuplicateCheckResult.Duplicate duplicate => DuplicatePolicy.ClassifyDuplicate(
                    row,
                    vehicle,
                    duplicate.State,
                    validationConfig,
                    mode)
                ?? CreateAcceptedDecision(
                    row,
                    vehicle,
                    duplicate.State.ExistingTransactionKey,
                    validationConfig),
            _ => throw new InvalidOperationException("Unhandled duplicate check result.")
        };
    }

    public static BatchDecision ClassifyBatch(BatchClassificationRequest request)
    {
        var decisions = request.Rows
            .Select(row => ClassifyRow(
                row.Row,
                row.VehicleLookup,
                row.DuplicateCheck,
                request.ValidationConfig,
                request.Mode))
            .ToArray();

        return new BatchDecision(decisions, BatchSummaryCalculator.Summarize(decisions));
    }

    private static RowDecision CreateAcceptedDecision(
        FuelRow row,
        Vehicle vehicle,
        TransactionKey transactionKey,
        ValidationConfig validationConfig)
    {
        var transaction = TransactionFactory.Create(row, vehicle, transactionKey);
        var warnings = WarningPolicy.WarningsFor(row, validationConfig);

        return warnings.Count == 0
            ? new RowDecision.AcceptedTransaction(transaction)
            : new RowDecision.AcceptedTransactionWithWarnings(transaction, warnings);
    }
}

public static class FuelUploadDecider
{
    public static RowDecision ClassifyRow(
        FuelRow row,
        VehicleLookupResult vehicleLookup,
        DuplicateCheckResult duplicateCheck,
        ValidationConfig validationConfig,
        UploadMode mode)
    {
        return FuelUploadDecisionEngine.ClassifyRow(row, vehicleLookup, duplicateCheck, validationConfig, mode);
    }

    public static BatchDecision ClassifyBatch(BatchClassificationRequest request)
    {
        return FuelUploadDecisionEngine.ClassifyBatch(request);
    }
}
