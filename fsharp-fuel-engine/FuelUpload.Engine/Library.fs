namespace FuelUpload.Engine

open System

[<RequireQualifiedAccess>]
type UploadMode =
    | Normal
    | Retry
    | Recovery

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

[<CLIMutable>]
type Vehicle =
    { VehicleId: string
      Registration: string }

[<RequireQualifiedAccess>]
type FatalProcessingError =
    | InvalidValidationConfig of detail: string
    | VehicleLookupUnavailable of detail: string
    | DuplicateCheckUnavailable of detail: string

[<RequireQualifiedAccess>]
type VehicleLookupResult =
    | Matched of Vehicle
    | NotFound
    | Ambiguous of Vehicle list
    | Fatal of FatalProcessingError

[<RequireQualifiedAccess>]
type PreviousAttemptState =
    | Finalized
    | RetryableFailure
    | NonRetryableFailure
    | FailedBeforeCanonicalFinalization

[<RequireQualifiedAccess>]
type DuplicateCheckResult =
    | NoDuplicate
    | Duplicate of PreviousAttemptState
    | Fatal of FatalProcessingError

[<CLIMutable>]
type ValidationConfig =
    { RequireExternalReference: bool
      MinFuelVolumeGallons: decimal
      MaxFuelVolumeGallons: decimal
      MinTotalCost: decimal
      MaxTotalCost: decimal
      AllowFutureTransactions: bool
      ProcessingDate: DateTimeOffset
      HighFuelVolumeWarningGallons: decimal
      HighCostPerGallonWarning: decimal
      StaleTransactionWarningDays: int }

[<RequireQualifiedAccess>]
type ValidationError =
    | MissingVehicleKey
    | MissingMerchantName
    | MissingExternalReference
    | InvalidFuelVolume of actual: decimal * minimum: decimal * maximum: decimal
    | InvalidTotalCost of actual: decimal * minimum: decimal * maximum: decimal
    | InvalidOdometer of actual: decimal
    | FutureTransaction of occurredAt: DateTimeOffset * processingDate: DateTimeOffset

[<RequireQualifiedAccess>]
type Warning =
    | HighFuelVolume of actual: decimal * threshold: decimal
    | HighCostPerGallon of actual: decimal * threshold: decimal
    | StaleTransaction of ageDays: int * thresholdDays: int

[<RequireQualifiedAccess>]
type VehicleRejectionReason =
    | UnknownVehicle
    | AmbiguousVehicle of candidates: Vehicle list

[<RequireQualifiedAccess>]
type RejectionReason =
    | ValidationFailed of ValidationError list
    | VehicleRejected of VehicleRejectionReason

[<RequireQualifiedAccess>]
type DuplicateSkipReason =
    | NormalModeDuplicate
    | RetryModeDuplicateAlreadyFinalized
    | RetryModeDuplicateNotRetryable of PreviousAttemptState
    | RecoveryModeDuplicateAlreadyCanonicalized of PreviousAttemptState

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

[<CLIMutable>]
type BatchSummary =
    { TotalRows: int
      AcceptedRows: int
      AcceptedWithWarningRows: int
      WarningCount: int
      SkippedDuplicateRows: int
      RejectedRows: int
      FatalErrorRows: int }

[<RequireQualifiedAccess>]
type BatchDecision =
    | Ready of rows: ClassifiedRow list * summary: BatchSummary
    | Blocked of rows: ClassifiedRow list * summary: BatchSummary * fatalErrors: FatalProcessingError list

module DecisionEngine =
    let private isBlank (value: string) =
        String.IsNullOrWhiteSpace value

    let private validateConfig (config: ValidationConfig) =
        [ if config.MinFuelVolumeGallons < 0m then
              FatalProcessingError.InvalidValidationConfig "Minimum fuel volume cannot be negative."
          if config.MaxFuelVolumeGallons < config.MinFuelVolumeGallons then
              FatalProcessingError.InvalidValidationConfig "Maximum fuel volume cannot be lower than minimum fuel volume."
          if config.MinTotalCost < 0m then
              FatalProcessingError.InvalidValidationConfig "Minimum total cost cannot be negative."
          if config.MaxTotalCost < config.MinTotalCost then
              FatalProcessingError.InvalidValidationConfig "Maximum total cost cannot be lower than minimum total cost."
          if config.HighFuelVolumeWarningGallons < 0m then
              FatalProcessingError.InvalidValidationConfig "High fuel volume warning threshold cannot be negative."
          if config.HighCostPerGallonWarning < 0m then
              FatalProcessingError.InvalidValidationConfig "High cost per gallon warning threshold cannot be negative."
          if config.StaleTransactionWarningDays < 0 then
              FatalProcessingError.InvalidValidationConfig "Stale transaction warning days cannot be negative." ]

    let private validateRow (config: ValidationConfig) (row: ParsedFuelRow) =
        [ if isBlank row.VehicleKey then
              ValidationError.MissingVehicleKey
          if isBlank row.MerchantName then
              ValidationError.MissingMerchantName
          if config.RequireExternalReference && isBlank row.ExternalReference then
              ValidationError.MissingExternalReference
          if row.FuelVolumeGallons < config.MinFuelVolumeGallons
             || row.FuelVolumeGallons > config.MaxFuelVolumeGallons then
              ValidationError.InvalidFuelVolume(
                  row.FuelVolumeGallons,
                  config.MinFuelVolumeGallons,
                  config.MaxFuelVolumeGallons
              )
          if row.TotalCost < config.MinTotalCost || row.TotalCost > config.MaxTotalCost then
              ValidationError.InvalidTotalCost(row.TotalCost, config.MinTotalCost, config.MaxTotalCost)
          if row.OdometerMiles < 0m then
              ValidationError.InvalidOdometer row.OdometerMiles
          if not config.AllowFutureTransactions && row.OccurredAt > config.ProcessingDate then
              ValidationError.FutureTransaction(row.OccurredAt, config.ProcessingDate) ]

    let private warningsFor (config: ValidationConfig) (row: ParsedFuelRow) =
        [ if row.FuelVolumeGallons > config.HighFuelVolumeWarningGallons then
              Warning.HighFuelVolume(row.FuelVolumeGallons, config.HighFuelVolumeWarningGallons)
          if row.FuelVolumeGallons > 0m then
              let costPerGallon = row.TotalCost / row.FuelVolumeGallons

              if costPerGallon > config.HighCostPerGallonWarning then
                  Warning.HighCostPerGallon(costPerGallon, config.HighCostPerGallonWarning)

          let age = config.ProcessingDate.Date - row.OccurredAt.Date

          if age.TotalDays > float config.StaleTransactionWarningDays then
              Warning.StaleTransaction(int age.TotalDays, config.StaleTransactionWarningDays) ]

    let private transactionId (row: ParsedFuelRow) (vehicle: Vehicle) =
        let occurred = row.OccurredAt.ToUnixTimeMilliseconds()
        let reference = row.ExternalReference.Trim()
        $"fuel:{vehicle.VehicleId}:{occurred}:{reference}:{row.RowNumber}"

    let private toTransaction mode row vehicle =
        { TransactionId = transactionId row vehicle
          SourceRowNumber = row.RowNumber
          Vehicle = vehicle
          OccurredAt = row.OccurredAt
          OdometerMiles = row.OdometerMiles
          FuelVolumeGallons = row.FuelVolumeGallons
          TotalCost = row.TotalCost
          MerchantName = row.MerchantName.Trim()
          ExternalReference = row.ExternalReference.Trim()
          Mode = mode }

    let private skipped row mode previous reason =
        RowDecision.SkippedDuplicate
            { Row = row
              Mode = mode
              PreviousAttempt = previous
              Reason = reason }

    let private accepted config mode row vehicle =
        let transaction = toTransaction mode row vehicle

        match warningsFor config row with
        | [] -> RowDecision.Accepted transaction
        | warnings -> RowDecision.AcceptedWithWarnings(transaction, warnings)

    let classifyRow
        (config: ValidationConfig)
        (mode: UploadMode)
        (row: ParsedFuelRow)
        (vehicleLookup: VehicleLookupResult)
        (duplicateCheck: DuplicateCheckResult)
        : RowDecision =
        match validateConfig config, vehicleLookup, duplicateCheck with
        | fatal :: _, _, _ -> RowDecision.Fatal fatal
        | _, VehicleLookupResult.Fatal fatal, _ -> RowDecision.Fatal fatal
        | _, _, DuplicateCheckResult.Fatal fatal -> RowDecision.Fatal fatal
        | [], _, _ ->
            match validateRow config row with
            | [] ->
                match vehicleLookup with
                | VehicleLookupResult.NotFound ->
                    RowDecision.Rejected
                        { Row = row
                          Reasons = [ RejectionReason.VehicleRejected VehicleRejectionReason.UnknownVehicle ] }
                | VehicleLookupResult.Ambiguous candidates ->
                    RowDecision.Rejected
                        { Row = row
                          Reasons =
                            [ RejectionReason.VehicleRejected(
                                  VehicleRejectionReason.AmbiguousVehicle candidates
                              ) ] }
                | VehicleLookupResult.Matched vehicle ->
                    match mode, duplicateCheck with
                    | _, DuplicateCheckResult.NoDuplicate -> accepted config mode row vehicle
                    | UploadMode.Normal, DuplicateCheckResult.Duplicate previous ->
                        skipped row mode previous DuplicateSkipReason.NormalModeDuplicate
                    | UploadMode.Retry, DuplicateCheckResult.Duplicate PreviousAttemptState.RetryableFailure ->
                        accepted config mode row vehicle
                    | UploadMode.Retry, DuplicateCheckResult.Duplicate PreviousAttemptState.Finalized ->
                        skipped row mode PreviousAttemptState.Finalized DuplicateSkipReason.RetryModeDuplicateAlreadyFinalized
                    | UploadMode.Retry, DuplicateCheckResult.Duplicate previous ->
                        skipped row mode previous (DuplicateSkipReason.RetryModeDuplicateNotRetryable previous)
                    | UploadMode.Recovery,
                      DuplicateCheckResult.Duplicate PreviousAttemptState.FailedBeforeCanonicalFinalization ->
                        accepted config mode row vehicle
                    | UploadMode.Recovery, DuplicateCheckResult.Duplicate previous ->
                        skipped row mode previous (DuplicateSkipReason.RecoveryModeDuplicateAlreadyCanonicalized previous)
                    | _, DuplicateCheckResult.Fatal fatal -> RowDecision.Fatal fatal
                | VehicleLookupResult.Fatal fatal -> RowDecision.Fatal fatal
            | errors ->
                RowDecision.Rejected
                    { Row = row
                      Reasons = [ RejectionReason.ValidationFailed errors ] }

    let private summarize (rows: ClassifiedRow list) =
        let folder summary classified =
            match classified.Decision with
            | RowDecision.Accepted _ ->
                { summary with
                    AcceptedRows = summary.AcceptedRows + 1 }
            | RowDecision.AcceptedWithWarnings(_, warnings) ->
                { summary with
                    AcceptedRows = summary.AcceptedRows + 1
                    AcceptedWithWarningRows = summary.AcceptedWithWarningRows + 1
                    WarningCount = summary.WarningCount + warnings.Length }
            | RowDecision.SkippedDuplicate _ ->
                { summary with
                    SkippedDuplicateRows = summary.SkippedDuplicateRows + 1 }
            | RowDecision.Rejected _ ->
                { summary with
                    RejectedRows = summary.RejectedRows + 1 }
            | RowDecision.Fatal _ ->
                { summary with
                    FatalErrorRows = summary.FatalErrorRows + 1 }

        rows
        |> List.fold
            folder
            { TotalRows = rows.Length
              AcceptedRows = 0
              AcceptedWithWarningRows = 0
              WarningCount = 0
              SkippedDuplicateRows = 0
              RejectedRows = 0
              FatalErrorRows = 0 }

    let classifyBatch (config: ValidationConfig) (mode: UploadMode) (rows: FuelRowContext seq) : BatchDecision =
        let classified =
            rows
            |> Seq.map (fun context ->
                { Row = context.Row
                  Decision =
                    classifyRow
                        config
                        mode
                        context.Row
                        context.VehicleLookup
                        context.DuplicateCheck })
            |> Seq.toList

        let summary = summarize classified

        let fatalErrors =
            classified
            |> List.choose (fun row ->
                match row.Decision with
                | RowDecision.Fatal fatal -> Some fatal
                | _ -> None)

        match fatalErrors with
        | [] -> BatchDecision.Ready(classified, summary)
        | errors -> BatchDecision.Blocked(classified, summary, errors)

[<AbstractClass; Sealed>]
type FuelUploadDecisionEngine private () =
    static member ClassifyRow(config, mode, row, vehicleLookup, duplicateCheck) =
        DecisionEngine.classifyRow config mode row vehicleLookup duplicateCheck

    static member ClassifyBatch(config, mode, rows) =
        DecisionEngine.classifyBatch config mode rows
