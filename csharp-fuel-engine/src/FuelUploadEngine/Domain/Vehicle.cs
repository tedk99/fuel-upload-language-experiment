namespace FuelUploadEngine;

public sealed record Vehicle(VehicleId Id, VehicleIdentifier Identifier);

public sealed record FuelTransaction(
    TransactionKey Key,
    Vehicle Vehicle,
    DateOnly TransactionDate,
    decimal Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    ExternalReference SourceReference);
