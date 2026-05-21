namespace FuelUploadEngine;

public sealed record FuelRow(
    RowNumber RowNumber,
    VehicleIdentifier VehicleIdentifier,
    DateOnly TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    ExternalReference ExternalReference);
