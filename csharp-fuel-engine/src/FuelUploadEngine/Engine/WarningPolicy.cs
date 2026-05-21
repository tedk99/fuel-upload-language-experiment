namespace FuelUploadEngine;

public static class WarningPolicy
{
    public static IReadOnlyList<UploadWarning> WarningsFor(FuelRow row, ValidationConfig config)
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
}
