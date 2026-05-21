namespace FuelUpload.Domain

[<CLIMutable>]
type Vehicle =
    { VehicleId: string
      Registration: string }

[<RequireQualifiedAccess>]
type VehicleLookupResult =
    | Matched of Vehicle
    | NotFound
    | Ambiguous of Vehicle list
    | Fatal of FatalProcessingError
