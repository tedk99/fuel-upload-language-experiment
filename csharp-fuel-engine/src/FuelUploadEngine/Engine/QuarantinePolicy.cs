namespace FuelUploadEngine;

public static class QuarantinePolicy
{
    public static IReadOnlyList<QuarantineReason> ReasonsFor(FuelRow row, ValidationConfig config)
    {
        var reasons = new List<QuarantineReason>();

        var merchantName = row.MerchantName.Trim();
        if (merchantName.Contains("test", StringComparison.OrdinalIgnoreCase)
            || merchantName.Contains("unknown", StringComparison.OrdinalIgnoreCase)
            || merchantName.Contains("manual", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(new QuarantineReason(QuarantineReasonCode.SuspiciousMerchantName));
        }

        if (row.Quantity == config.SuspiciousQuantity)
        {
            reasons.Add(new QuarantineReason(QuarantineReasonCode.SuspiciousQuantityPattern));
        }

        var totalCost = Math.Round(row.Quantity * row.UnitPrice, 2, MidpointRounding.AwayFromZero);
        if (totalCost == config.SuspiciousTotalCost)
        {
            reasons.Add(new QuarantineReason(QuarantineReasonCode.SuspiciousCostPattern));
        }

        return reasons;
    }
}
