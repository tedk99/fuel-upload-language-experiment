namespace FuelUpload.Domain

open System

[<RequireQualifiedAccess>]
type UploadMode =
    | Normal
    | Retry
    | ConservativeRecovery
    | AggressiveRecovery

[<RequireQualifiedAccess>]
type FatalProcessingError =
    | InvalidValidationConfig of detail: string
    | VehicleLookupUnavailable of detail: string
    | DuplicateCheckUnavailable of detail: string
