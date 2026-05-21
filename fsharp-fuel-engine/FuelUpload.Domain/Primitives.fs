namespace FuelUpload.Domain

open System

[<RequireQualifiedAccess>]
type UploadMode =
    | Normal
    | Retry
    | Recovery

[<RequireQualifiedAccess>]
type FatalProcessingError =
    | InvalidValidationConfig of detail: string
    | VehicleLookupUnavailable of detail: string
    | DuplicateCheckUnavailable of detail: string
