namespace FuelUploadEngine;

public static class FuelRowValidator
{
    public static IReadOnlyList<ValidationError> Validate(FuelRow row, ValidationConfig config)
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
}
