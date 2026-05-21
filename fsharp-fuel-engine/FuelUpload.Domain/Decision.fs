namespace FuelUpload.Domain

open System

[<CLIMutable>]
type AcceptedTransaction =
    { TransactionId: string
      SourceRowNumber: int
      Vehicle: Vehicle
      OccurredAt: DateTimeOffset
      OdometerMiles: decimal
      FuelVolumeGallons: decimal
      TotalCost: decimal
      MerchantName: string
      ExternalReference: string
      Mode: UploadMode }

[<CLIMutable>]
type RejectedRow =
    { Row: ParsedFuelRow
      Reasons: RejectionReason list }

[<CLIMutable>]
type SkippedDuplicate =
    { Row: ParsedFuelRow
      Mode: UploadMode
      PreviousAttempt: PreviousAttemptState
      Reason: DuplicateSkipReason }

[<RequireQualifiedAccess>]
type RowDecision =
    | Accepted of AcceptedTransaction
    | AcceptedWithWarnings of AcceptedTransaction * Warning list
    | SkippedDuplicate of SkippedDuplicate
    | Rejected of RejectedRow
    | Fatal of FatalProcessingError

[<CLIMutable>]
type FuelRowContext =
    { Row: ParsedFuelRow
      VehicleLookup: VehicleLookupResult
      DuplicateCheck: DuplicateCheckResult }

[<CLIMutable>]
type ClassifiedRow =
    { Row: ParsedFuelRow
      Decision: RowDecision }
