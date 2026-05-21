namespace FuelUpload.Domain

open System

[<CLIMutable>]
type ParsedFuelRow =
    { RowNumber: int
      VehicleKey: string
      OccurredAt: DateTimeOffset
      OdometerMiles: decimal
      FuelVolumeGallons: decimal
      TotalCost: decimal
      MerchantName: string
      ExternalReference: string }
