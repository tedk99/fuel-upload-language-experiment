namespace FuelUploadEngine;

public sealed record FuelRow(
    RowNumber RowNumber,
    VehicleIdentifier VehicleIdentifier,
    DateOnly TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    string MerchantName,
    ExternalReference ExternalReference);
