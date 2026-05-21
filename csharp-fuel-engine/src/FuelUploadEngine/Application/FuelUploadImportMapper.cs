using System.Globalization;

namespace FuelUploadEngine.Application;

public static class FuelUploadImportMapper
{
    public static FuelImportMapResult<FuelUploadRequestDto> ToApplicationRequest(ImportBatchRequest request)
    {
        var errors = new List<FuelImportError>();

        var uploadMode = ParseUploadMode(request.UploadMode, "uploadMode", errors);
        var maximumQuantity = ParseDecimal(request.MaximumQuantity, "maximumQuantity", errors);
        var maximumUnitPrice = ParseDecimal(request.MaximumUnitPrice, "maximumUnitPrice", errors);
        var warningQuantity = ParseDecimal(request.WarningQuantity, "warningQuantity", errors);
        var warningUnitPrice = ParseDecimal(request.WarningUnitPrice, "warningUnitPrice", errors);
        var suspiciousQuantity = ParseDecimal(request.SuspiciousQuantity, "suspiciousQuantity", errors);
        var suspiciousTotalCost = ParseDecimal(request.SuspiciousTotalCost, "suspiciousTotalCost", errors);
        var today = ParseDate(request.Today, "today", errors);

        if (request.Rows is null)
        {
            errors.Add(new FuelImportError(FuelImportErrorCode.MissingRows, "rows", "Rows are required."));
        }

        var rows = new List<FuelUploadRowDto>();
        if (request.Rows is not null)
        {
            for (var index = 0; index < request.Rows.Count; index++)
            {
                var row = ToApplicationRow(request.Rows[index], index, errors);
                if (row is not null)
                {
                    rows.Add(row);
                }
            }
        }

        if (errors.Count > 0
            || uploadMode is null
            || maximumQuantity is null
            || maximumUnitPrice is null
            || warningQuantity is null
            || warningUnitPrice is null
            || suspiciousQuantity is null
            || suspiciousTotalCost is null
            || today is null)
        {
            return new FuelImportMapResult<FuelUploadRequestDto>.Failure(errors);
        }

        return new FuelImportMapResult<FuelUploadRequestDto>.Success(
            new FuelUploadRequestDto(
                uploadMode,
                maximumQuantity.Value,
                maximumUnitPrice.Value,
                warningQuantity.Value,
                warningUnitPrice.Value,
                suspiciousQuantity.Value,
                suspiciousTotalCost.Value,
                today,
                rows));
    }

    public static FuelImportMapResult<FuelUploadResponseDto> Classify(ImportBatchRequest request)
    {
        var mapped = ToApplicationRequest(request);
        if (mapped is FuelImportMapResult<FuelUploadRequestDto>.Failure importFailure)
        {
            return new FuelImportMapResult<FuelUploadResponseDto>.Failure(importFailure.Errors);
        }

        var applicationRequest = ((FuelImportMapResult<FuelUploadRequestDto>.Success)mapped).Value;
        var classified = new FuelUploadApplicationService().Classify(applicationRequest);
        return classified switch
        {
            FuelUploadMapResult<FuelUploadResponseDto>.Success success =>
                new FuelImportMapResult<FuelUploadResponseDto>.Success(success.Value),
            FuelUploadMapResult<FuelUploadResponseDto>.Failure applicationFailure =>
                new FuelImportMapResult<FuelUploadResponseDto>.Failure(applicationFailure.Errors.Select(ToImportError).ToArray()),
            _ => throw new InvalidOperationException("Unhandled application mapping result.")
        };
    }

    private static FuelUploadRowDto? ToApplicationRow(
        ImportedFuelRow row,
        int index,
        List<FuelImportError> errors)
    {
        var prefix = $"rows[{index}]";
        var rowNumber = ParseInt(row.RowNumber, $"{prefix}.rowNumber", errors);
        var vehicleIdentifier = Require(row.VehicleIdentifier, $"{prefix}.vehicleIdentifier", errors);
        var transactionDate = ParseDate(row.TransactionDate, $"{prefix}.transactionDate", errors);
        var quantity = ParseDecimal(row.Quantity, $"{prefix}.quantity", errors);
        var unitPrice = ParseDecimal(row.UnitPrice, $"{prefix}.unitPrice", errors);
        var merchantName = Require(row.MerchantName, $"{prefix}.merchantName", errors);
        var externalReference = Require(row.ExternalReference, $"{prefix}.externalReference", errors);
        var vehicleLookupStatus = Require(row.VehicleLookupStatus, $"{prefix}.vehicleLookupStatus", errors);
        var duplicateStatus = Require(row.DuplicateStatus, $"{prefix}.duplicateStatus", errors);
        var canonicalKeyPresent = ParseBool(
            row.CanonicalTransactionKeyPresent,
            $"{prefix}.canonicalTransactionKeyPresent",
            errors);

        if (rowNumber is null
            || vehicleIdentifier is null
            || transactionDate is null
            || quantity is null
            || unitPrice is null
            || merchantName is null
            || externalReference is null
            || vehicleLookupStatus is null
            || duplicateStatus is null
            || canonicalKeyPresent is null)
        {
            return null;
        }

        return new FuelUploadRowDto(
            rowNumber.Value,
            vehicleIdentifier,
            transactionDate,
            quantity.Value,
            unitPrice.Value,
            merchantName,
            externalReference,
            vehicleLookupStatus,
            row.VehicleId,
            row.AmbiguousVehicleIds,
            row.VehicleLookupError,
            duplicateStatus,
            row.TransactionKey,
            row.PreviousOutcome,
            canonicalKeyPresent.Value,
            row.DuplicateError);
    }

    private static FuelImportError ToImportError(FuelUploadMappingError error)
    {
        var code = error.Code switch
        {
            FuelUploadMappingErrorCode.InvalidUploadMode => FuelImportErrorCode.InvalidUploadMode,
            FuelUploadMappingErrorCode.InvalidDate => FuelImportErrorCode.InvalidDate,
            FuelUploadMappingErrorCode.MissingRows => FuelImportErrorCode.MissingRows,
            _ => FuelImportErrorCode.MissingRequiredCell
        };

        return new FuelImportError(code, error.Field, error.Detail);
    }

    private static string? ParseUploadMode(string? value, string field, List<FuelImportError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        return Normalize(required) switch
        {
            "normal" or "retry" or "conservativerecovery" or "aggressiverecovery" => required,
            _ => InvalidUploadMode(required, field, errors)
        };
    }

    private static string? InvalidUploadMode(string value, string field, List<FuelImportError> errors)
    {
        errors.Add(new FuelImportError(
            FuelImportErrorCode.InvalidUploadMode,
            field,
            $"Unsupported upload mode '{value}'."));
        return null;
    }

    private static string? ParseDate(string? value, string field, List<FuelImportError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(required, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return required;
        }

        errors.Add(new FuelImportError(
            FuelImportErrorCode.InvalidDate,
            field,
            "Date must use yyyy-MM-dd format."));
        return null;
    }

    private static decimal? ParseDecimal(string? value, string field, List<FuelImportError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        if (decimal.TryParse(required, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add(new FuelImportError(FuelImportErrorCode.InvalidNumber, field, "Cell must be a decimal number."));
        return null;
    }

    private static int? ParseInt(string? value, string field, List<FuelImportError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        if (int.TryParse(required, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add(new FuelImportError(FuelImportErrorCode.InvalidNumber, field, "Cell must be an integer."));
        return null;
    }

    private static bool? ParseBool(string? value, string field, List<FuelImportError> errors)
    {
        var required = Require(value, field, errors);
        if (required is null)
        {
            return null;
        }

        return Normalize(required) switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => InvalidBool(field, errors)
        };
    }

    private static bool? InvalidBool(string field, List<FuelImportError> errors)
    {
        errors.Add(new FuelImportError(FuelImportErrorCode.InvalidBoolean, field, "Cell must be true or false."));
        return null;
    }

    private static string? Require(string? value, string field, List<FuelImportError> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        errors.Add(new FuelImportError(FuelImportErrorCode.MissingRequiredCell, field, "A non-empty cell is required."));
        return null;
    }

    private static string Normalize(string value)
    {
        return value.Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
    }
}
