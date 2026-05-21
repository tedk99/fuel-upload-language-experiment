namespace FuelUploadEngine;

public static class TransactionFactory
{
    public static FuelTransaction Create(FuelRow row, Vehicle vehicle, TransactionKey transactionKey)
    {
        return new FuelTransaction(
            transactionKey,
            vehicle,
            row.TransactionDate,
            row.Quantity,
            row.UnitPrice,
            Math.Round(row.Quantity * row.UnitPrice, 2, MidpointRounding.AwayFromZero),
            row.ExternalReference);
    }
}
