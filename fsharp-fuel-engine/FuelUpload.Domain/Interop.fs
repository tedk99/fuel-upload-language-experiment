namespace FuelUpload.Domain

open System

[<CLIMutable>]
type FuelUploadRowDto =
    { RowNumber: int
      VehicleKey: string
      OccurredAt: string
      OdometerMiles: decimal
      FuelVolumeGallons: decimal
      TotalCost: decimal
      MerchantName: string
      ExternalReference: string
      VehicleLookupStatus: string
      VehicleId: string
      VehicleRegistration: string
      AmbiguousVehicleIds: string array
      VehicleLookupError: string
      DuplicateStatus: string
      PreviousAttempt: string
      DuplicateError: string }

[<CLIMutable>]
type FuelUploadRequestDto =
    { UploadMode: string
      RequireExternalReference: bool
      MinFuelVolumeGallons: decimal
      MaxFuelVolumeGallons: decimal
      MinTotalCost: decimal
      MaxTotalCost: decimal
      AllowFutureTransactions: bool
      ProcessingDate: string
      HighFuelVolumeWarningGallons: decimal
      HighCostPerGallonWarning: decimal
      StaleTransactionWarningDays: int
      SuspiciousFuelVolumeGallons: decimal
      SuspiciousTotalCost: decimal
      Rows: FuelUploadRowDto array }

[<CLIMutable>]
type FuelUploadDecisionDto =
    { RowNumber: int
      Outcome: string
      TransactionId: string
      VehicleId: string
      Warnings: string array
      QuarantineReasons: string array
      RejectionReasons: string array
      DuplicateSkipReason: string
      FatalError: string }

[<CLIMutable>]
type FuelUploadResponseDto =
    { Decisions: FuelUploadDecisionDto array
      TotalRows: int
      AcceptedRows: int
      AcceptedWithWarningRows: int
      WarningCount: int
      QuarantinedRows: int
      SkippedDuplicateRows: int
      RejectedRows: int
      FatalErrorRows: int
      IsBlocked: bool }

[<RequireQualifiedAccess>]
type FuelUploadMappingErrorCode =
    | MissingRequiredField
    | InvalidDate
    | InvalidUploadMode
    | InvalidVehicleLookupStatus
    | MissingVehicleLookupPayload
    | InvalidDuplicateStatus
    | InvalidPreviousAttempt

[<CLIMutable>]
type FuelUploadMappingError =
    { Code: FuelUploadMappingErrorCode
      Field: string
      Detail: string }

module FuelUploadInterop =
    let private normalize (value: string) =
        if isNull value then
            ""
        else
            value.Replace("_", "").Trim().ToLowerInvariant()

    let private missing field =
        { Code = FuelUploadMappingErrorCode.MissingRequiredField
          Field = field
          Detail = "A non-empty value is required." }

    let private require field value =
        if String.IsNullOrWhiteSpace value then
            Error [ missing field ]
        else
            Ok(value.Trim())

    let private parseDate field value =
        match require field value with
        | Error errors -> Error errors
        | Ok value ->
            match DateTimeOffset.TryParse(value) with
            | true, parsed -> Ok parsed
            | false, _ ->
                Error
                    [ { Code = FuelUploadMappingErrorCode.InvalidDate
                        Field = field
                        Detail = "Date must be parseable as a DateTimeOffset." } ]

    let private parseUploadMode field value =
        match normalize value with
        | "normal" -> Ok UploadMode.Normal
        | "retry" -> Ok UploadMode.Retry
        | "conservativerecovery" -> Ok UploadMode.ConservativeRecovery
        | "aggressiverecovery" -> Ok UploadMode.AggressiveRecovery
        | _ ->
            Error
                [ { Code = FuelUploadMappingErrorCode.InvalidUploadMode
                    Field = field
                    Detail = $"Unsupported upload mode '{value}'." } ]

    let private parsePreviousAttempt field value =
        match normalize value with
        | "finalized" -> Ok PreviousAttemptState.Finalized
        | "retryablefailure" -> Ok PreviousAttemptState.RetryableFailure
        | "nonretryablefailure" -> Ok PreviousAttemptState.NonRetryableFailure
        | "failedbeforecanonicalfinalization" -> Ok PreviousAttemptState.FailedBeforeCanonicalFinalization
        | "failedaftercanonicalizationwithcanonicaltransactionkey" ->
            Ok PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey
        | "failedaftercanonicalizationwithoutcanonicaltransactionkey" ->
            Ok PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey
        | _ ->
            Error
                [ { Code = FuelUploadMappingErrorCode.InvalidPreviousAttempt
                    Field = field
                    Detail = $"Unsupported previous attempt '{value}'." } ]

    let private errorsOf result =
        match result with
        | Ok _ -> []
        | Error errors -> errors

    let private parseVehicleLookup prefix row =
        match normalize row.VehicleLookupStatus with
        | "matched" ->
            let vehicleId = require $"{prefix}.vehicleId" row.VehicleId
            let registration = require $"{prefix}.vehicleRegistration" row.VehicleRegistration

            match vehicleId, registration with
            | Ok vehicleId, Ok registration ->
                Ok(
                    VehicleLookupResult.Matched
                        { VehicleId = vehicleId
                          Registration = registration }
                )
            | _ -> Error(errorsOf vehicleId @ errorsOf registration)
        | "notfound" -> Ok VehicleLookupResult.NotFound
        | "ambiguous" ->
            if isNull row.AmbiguousVehicleIds || row.AmbiguousVehicleIds.Length = 0 then
                Error
                    [ { Code = FuelUploadMappingErrorCode.MissingVehicleLookupPayload
                        Field = $"{prefix}.ambiguousVehicleIds"
                        Detail = "Ambiguous vehicle lookup requires at least one candidate id." } ]
            else
                Ok(
                    row.AmbiguousVehicleIds
                    |> Array.map (fun id ->
                        { VehicleId = id.Trim()
                          Registration = row.VehicleKey })
                    |> Array.toList
                    |> VehicleLookupResult.Ambiguous
                )
        | "fatal" ->
            match require $"{prefix}.vehicleLookupError" row.VehicleLookupError with
            | Ok detail -> Ok(VehicleLookupResult.Fatal(FatalProcessingError.VehicleLookupUnavailable detail))
            | Error errors -> Error errors
        | _ ->
            Error
                [ { Code = FuelUploadMappingErrorCode.InvalidVehicleLookupStatus
                    Field = $"{prefix}.vehicleLookupStatus"
                    Detail = $"Unsupported vehicle lookup status '{row.VehicleLookupStatus}'." } ]

    let private parseDuplicateCheck prefix row =
        match normalize row.DuplicateStatus with
        | "noduplicate" -> Ok DuplicateCheckResult.NoDuplicate
        | "duplicate" ->
            parsePreviousAttempt $"{prefix}.previousAttempt" row.PreviousAttempt
            |> Result.map DuplicateCheckResult.Duplicate
        | "fatal" ->
            match require $"{prefix}.duplicateError" row.DuplicateError with
            | Ok detail -> Ok(DuplicateCheckResult.Fatal(FatalProcessingError.DuplicateCheckUnavailable detail))
            | Error errors -> Error errors
        | _ ->
            Error
                [ { Code = FuelUploadMappingErrorCode.InvalidDuplicateStatus
                    Field = $"{prefix}.duplicateStatus"
                    Detail = $"Unsupported duplicate status '{row.DuplicateStatus}'." } ]

    let private mapRow index row =
        let prefix = $"rows[{index}]"
        let occurredAt = parseDate $"{prefix}.occurredAt" row.OccurredAt
        let vehicleLookup = parseVehicleLookup prefix row
        let duplicateCheck = parseDuplicateCheck prefix row

        match occurredAt, vehicleLookup, duplicateCheck with
        | Ok occurredAt, Ok vehicleLookup, Ok duplicateCheck ->
            Ok
                { Row =
                    { RowNumber = row.RowNumber
                      VehicleKey = row.VehicleKey
                      OccurredAt = occurredAt
                      OdometerMiles = row.OdometerMiles
                      FuelVolumeGallons = row.FuelVolumeGallons
                      TotalCost = row.TotalCost
                      MerchantName = row.MerchantName
                      ExternalReference = row.ExternalReference }
                  VehicleLookup = vehicleLookup
                  DuplicateCheck = duplicateCheck }
        | _ -> Error(errorsOf occurredAt @ errorsOf vehicleLookup @ errorsOf duplicateCheck)

    let toDomainRequest (request: FuelUploadRequestDto) =
        let mode = parseUploadMode "uploadMode" request.UploadMode
        let processingDate = parseDate "processingDate" request.ProcessingDate

        let rowResults, missingRowErrors =
            if isNull request.Rows then
                [],
                [ { Code = FuelUploadMappingErrorCode.MissingRequiredField
                    Field = "rows"
                    Detail = "Rows are required." } ]
            else
                request.Rows |> Array.mapi mapRow |> Array.toList, []

        let rowErrors = rowResults |> List.collect errorsOf

        match mode, processingDate, rowErrors, missingRowErrors with
        | Ok mode, Ok processingDate, [], [] ->
            let rows =
                rowResults
                |> List.choose (function
                    | Ok row -> Some row
                    | Error _ -> None)

            Ok(
                { RequireExternalReference = request.RequireExternalReference
                  MinFuelVolumeGallons = request.MinFuelVolumeGallons
                  MaxFuelVolumeGallons = request.MaxFuelVolumeGallons
                  MinTotalCost = request.MinTotalCost
                  MaxTotalCost = request.MaxTotalCost
                  AllowFutureTransactions = request.AllowFutureTransactions
                  ProcessingDate = processingDate
                  HighFuelVolumeWarningGallons = request.HighFuelVolumeWarningGallons
                  HighCostPerGallonWarning = request.HighCostPerGallonWarning
                  StaleTransactionWarningDays = request.StaleTransactionWarningDays
                  SuspiciousFuelVolumeGallons = request.SuspiciousFuelVolumeGallons
                  SuspiciousTotalCost = request.SuspiciousTotalCost },
                mode,
                rows
            )
        | _ -> Error(errorsOf mode @ errorsOf processingDate @ rowErrors @ missingRowErrors)

    let private warningText warning = $"%A{warning}"

    let private rejectionText reason = $"%A{reason}"

    let private quarantineText reason = $"%A{reason}"

    let private fatalText fatal = $"%A{fatal}"

    let private decisionDto rowNumber outcome transactionId vehicleId warnings quarantines rejections duplicateSkip fatal =
        { RowNumber = rowNumber
          Outcome = outcome
          TransactionId = transactionId
          VehicleId = vehicleId
          Warnings = warnings
          QuarantineReasons = quarantines
          RejectionReasons = rejections
          DuplicateSkipReason = duplicateSkip
          FatalError = fatal }

    let toDecisionDto classified =
        match classified.Decision with
        | RowDecision.Accepted transaction ->
            decisionDto
                transaction.SourceRowNumber
                "accepted"
                transaction.TransactionId
                transaction.Vehicle.VehicleId
                [||]
                [||]
                [||]
                ""
                ""
        | RowDecision.AcceptedWithWarnings(transaction, warnings) ->
            decisionDto
                transaction.SourceRowNumber
                "accepted_with_warnings"
                transaction.TransactionId
                transaction.Vehicle.VehicleId
                (warnings |> List.map warningText |> List.toArray)
                [||]
                [||]
                ""
                ""
        | RowDecision.Quarantined quarantined ->
            decisionDto
                quarantined.Transaction.SourceRowNumber
                "quarantined"
                quarantined.Transaction.TransactionId
                quarantined.Transaction.Vehicle.VehicleId
                (quarantined.Warnings |> List.map warningText |> List.toArray)
                (quarantined.Reasons |> QuarantineReasons.toList |> List.map quarantineText |> List.toArray)
                [||]
                ""
                ""
        | RowDecision.SkippedDuplicate skipped ->
            decisionDto
                skipped.Row.RowNumber
                "skipped_duplicate"
                ""
                ""
                [||]
                [||]
                [||]
                ($"%A{skipped.Reason}")
                ""
        | RowDecision.Rejected rejected ->
            decisionDto
                rejected.Row.RowNumber
                "rejected"
                ""
                ""
                [||]
                [||]
                (rejected.Reasons |> List.map rejectionText |> List.toArray)
                ""
                ""
        | RowDecision.Fatal fatal ->
            decisionDto classified.Row.RowNumber "fatal" "" "" [||] [||] [||] "" (fatalText fatal)

    let toResponseDto decision =
        let rows, summary, isBlocked =
            match decision with
            | BatchDecision.Ready(rows, summary) -> rows, summary, false
            | BatchDecision.Blocked(rows, summary, _) -> rows, summary, true

        { Decisions = rows |> List.map toDecisionDto |> List.toArray
          TotalRows = summary.TotalRows
          AcceptedRows = summary.AcceptedRows
          AcceptedWithWarningRows = summary.AcceptedWithWarningRows
          WarningCount = summary.WarningCount
          QuarantinedRows = summary.QuarantinedRows
          SkippedDuplicateRows = summary.SkippedDuplicateRows
          RejectedRows = summary.RejectedRows
          FatalErrorRows = summary.FatalErrorRows
          IsBlocked = isBlocked }

    let classify request =
        toDomainRequest request
        |> Result.map (fun (config, mode, rows) ->
            rows
            |> DecisionEngine.classifyBatch config mode
            |> toResponseDto)

type FuelUploadFacade() =
    member _.Classify(request: FuelUploadRequestDto) = FuelUploadInterop.classify request
