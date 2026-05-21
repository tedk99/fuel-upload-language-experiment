namespace FuelUploadEngine;

public static class FuelUploadDecider
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

        var validationErrors = Validate(row, validationConfig);
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

        var duplicateDecision = duplicateCheck switch
        {
            DuplicateCheckResult.NotDuplicate notDuplicate => CreateAcceptedDecision(
                row,
                vehicle,
                notDuplicate.ProposedTransactionKey,
                validationConfig),
            DuplicateCheckResult.Duplicate duplicate => ClassifyDuplicate(
                row,
                vehicle,
                duplicate.State,
                validationConfig,
                mode),
            _ => throw new InvalidOperationException("Unhandled duplicate check result.")
        };

        return duplicateDecision;
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

        return new BatchDecision(decisions, Summarize(decisions));
    }

    private static RowDecision ClassifyDuplicate(
        FuelRow row,
        Vehicle vehicle,
        DuplicateState duplicate,
        ValidationConfig validationConfig,
        UploadMode mode)
    {
        return mode switch
        {
            UploadMode.Normal => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.DuplicateInNormalMode),
            UploadMode.Retry when duplicate.PreviousOutcome is PreviousUploadOutcome.RetryableFailure => CreateAcceptedDecision(
                row,
                vehicle,
                duplicate.ExistingTransactionKey,
                validationConfig),
            UploadMode.Retry => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptNotRetryable),
            UploadMode.Recovery when duplicate.PreviousOutcome is PreviousUploadOutcome.FailedBeforeCanonicalFinalization => CreateAcceptedDecision(
                row,
                vehicle,
                duplicate.ExistingTransactionKey,
                validationConfig),
            UploadMode.Recovery => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptAlreadyCanonicalized),
            _ => throw new InvalidOperationException("Unhandled upload mode.")
        };
    }

    private static RowDecision CreateAcceptedDecision(
        FuelRow row,
        Vehicle vehicle,
        TransactionKey transactionKey,
        ValidationConfig validationConfig)
    {
        var transaction = new FuelTransaction(
            transactionKey,
            vehicle,
            row.TransactionDate,
            row.Quantity,
            row.UnitPrice,
            Math.Round(row.Quantity * row.UnitPrice, 2, MidpointRounding.AwayFromZero),
            row.ExternalReference);

        var warnings = Warn(row, validationConfig);

        return warnings.Count == 0
            ? new RowDecision.AcceptedTransaction(transaction)
            : new RowDecision.AcceptedTransactionWithWarnings(transaction, warnings);
    }

    private static IReadOnlyList<ValidationError> Validate(FuelRow row, ValidationConfig config)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(row.VehicleIdentifier.Value))
        {
            errors.Add(new ValidationError(ValidationErrorCode.MissingVehicleIdentifier));
        }

        if (row.Quantity <= 0)
        {
            errors.Add(new ValidationError(ValidationErrorCode.NonPositiveQuantity));
        }

        if (row.Quantity > config.MaximumQuantity)
        {
            errors.Add(new ValidationError(ValidationErrorCode.QuantityExceedsMaximum));
        }

        if (row.UnitPrice < 0)
        {
            errors.Add(new ValidationError(ValidationErrorCode.NegativeUnitPrice));
        }

        if (row.UnitPrice > config.MaximumUnitPrice)
        {
            errors.Add(new ValidationError(ValidationErrorCode.UnitPriceExceedsMaximum));
        }

        if (row.TransactionDate > config.Today)
        {
            errors.Add(new ValidationError(ValidationErrorCode.TransactionDateInFuture));
        }

        return errors;
    }

    private static IReadOnlyList<UploadWarning> Warn(FuelRow row, ValidationConfig config)
    {
        var warnings = new List<UploadWarning>();

        if (row.Quantity > config.WarningQuantity)
        {
            warnings.Add(new UploadWarning(WarningCode.QuantityAboveWarningThreshold));
        }

        if (row.UnitPrice > config.WarningUnitPrice)
        {
            warnings.Add(new UploadWarning(WarningCode.UnitPriceAboveWarningThreshold));
        }

        return warnings;
    }

    private static BatchSummary Summarize(IReadOnlyCollection<RowDecision> decisions)
    {
        var acceptedWithoutWarnings = decisions.OfType<RowDecision.AcceptedTransaction>().Count();
        var acceptedWithWarnings = decisions.OfType<RowDecision.AcceptedTransactionWithWarnings>().ToArray();
        var acceptedTransactions = acceptedWithoutWarnings + acceptedWithWarnings.Length;
        var fatalRows = decisions.OfType<RowDecision.FatalProcessingError>().Count();

        return new BatchSummary(
            TotalRows: decisions.Count,
            AcceptedTransactions: acceptedTransactions,
            AcceptedWithoutWarnings: acceptedWithoutWarnings,
            AcceptedWithWarnings: acceptedWithWarnings.Length,
            SkippedDuplicates: decisions.OfType<RowDecision.SkippedDuplicate>().Count(),
            RejectedRows: decisions.OfType<RowDecision.RejectedRow>().Count(),
            FatalRows: fatalRows,
            WarningCount: acceptedWithWarnings.Sum(decision => decision.Warnings.Count),
            UploadableTransactions: fatalRows == 0 ? acceptedTransactions : 0);
    }
}
