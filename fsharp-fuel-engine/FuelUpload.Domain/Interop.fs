namespace FuelUpload.Domain

open System
open System.Globalization

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

[<CLIMutable>]
type ImportedFuelRow =
    { RowNumber: string
      VehicleKey: string
      OccurredAt: string
      OdometerMiles: string
      FuelVolumeGallons: string
      TotalCost: string
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
type ImportBatchRequest =
    { UploadMode: string
      RequireExternalReference: string
      MinFuelVolumeGallons: string
      MaxFuelVolumeGallons: string
      MinTotalCost: string
      MaxTotalCost: string
      AllowFutureTransactions: string
      ProcessingDate: string
      HighFuelVolumeWarningGallons: string
      HighCostPerGallonWarning: string
      StaleTransactionWarningDays: string
      SuspiciousFuelVolumeGallons: string
      SuspiciousTotalCost: string
      Rows: ImportedFuelRow array }

[<RequireQualifiedAccess>]
type FuelImportErrorCode =
    | MissingRows
    | MissingRequiredCell
    | InvalidNumber
    | InvalidDate
    | InvalidUploadMode
    | InvalidBoolean

[<CLIMutable>]
type FuelImportError =
    { Code: FuelImportErrorCode
      Field: string
      Detail: string }

[<RequireQualifiedAccess>]
type VehicleRepositoryErrorCode =
    | Unavailable
    | TimedOut

[<CLIMutable>]
type VehicleRepositoryError =
    { Code: VehicleRepositoryErrorCode
      Detail: string }

[<RequireQualifiedAccess>]
type DuplicateRepositoryErrorCode =
    | Unavailable
    | TimedOut

[<CLIMutable>]
type DuplicateRepositoryError =
    { Code: DuplicateRepositoryErrorCode
      Detail: string }

[<CLIMutable>]
type DuplicateRepositoryLookup =
    { RowNumber: int
      VehicleKey: string
      ExternalReference: string }

type IVehicleRepository =
    abstract Lookup: vehicleKey: string -> Result<VehicleLookupResult, VehicleRepositoryError>

type IDuplicateRepository =
    abstract Lookup: lookup: DuplicateRepositoryLookup -> Result<DuplicateCheckResult, DuplicateRepositoryError>

module FuelUploadInterop =
    let private normalize (value: string) =
        if isNull value then
            ""
        else
            value.Replace("_", "").Trim().ToLowerInvariant()

    let private missing field : FuelUploadMappingError =
        { Code = FuelUploadMappingErrorCode.MissingRequiredField
          Field = field
          Detail = "A non-empty value is required." }

    let private require field value =
        if String.IsNullOrWhiteSpace value then
            Error [ missing field ]
        else
            Ok(value.Trim())

    let private parseDate field value : Result<DateTimeOffset, FuelUploadMappingError list> =
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

    let private parseUploadMode field value : Result<UploadMode, FuelUploadMappingError list> =
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

    let private parsePreviousAttempt field value : Result<PreviousAttemptState, FuelUploadMappingError list> =
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

    let private importError (code: FuelImportErrorCode) field detail : FuelImportError =
        { Code = code
          Field = field
          Detail = detail }

    let private requireCell field value =
        if String.IsNullOrWhiteSpace value then
            Error [ importError FuelImportErrorCode.MissingRequiredCell field "A non-empty cell is required." ]
        else
            Ok(value.Trim())

    let private parseImportUploadMode field value =
        match requireCell field value with
        | Error errors -> Error errors
        | Ok value ->
            match normalize value with
            | "normal"
            | "retry"
            | "conservativerecovery"
            | "aggressiverecovery" -> Ok value
            | _ ->
                Error
                    [ importError
                          FuelImportErrorCode.InvalidUploadMode
                          field
                          $"Unsupported upload mode '{value}'." ]

    let private parseImportDate field value =
        match requireCell field value with
        | Error errors -> Error errors
        | Ok value ->
            match DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, _ -> Ok value
            | false, _ ->
                Error [ importError FuelImportErrorCode.InvalidDate field "Cell must be a parseable date." ]

    let private parseImportDecimal field value =
        match requireCell field value with
        | Error errors -> Error errors
        | Ok value ->
            match Decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture) with
            | true, parsed -> Ok parsed
            | false, _ ->
                Error [ importError FuelImportErrorCode.InvalidNumber field "Cell must be a decimal number." ]

    let private parseImportInt field value =
        match requireCell field value with
        | Error errors -> Error errors
        | Ok value ->
            match Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, parsed -> Ok parsed
            | false, _ -> Error [ importError FuelImportErrorCode.InvalidNumber field "Cell must be an integer." ]

    let private parseImportBool field value =
        match requireCell field value with
        | Error errors -> Error errors
        | Ok value ->
            match normalize value with
            | "true"
            | "yes"
            | "1" -> Ok true
            | "false"
            | "no"
            | "0" -> Ok false
            | _ -> Error [ importError FuelImportErrorCode.InvalidBoolean field "Cell must be true or false." ]

    let private mapImportedRow index (row: ImportedFuelRow) : Result<FuelUploadRowDto, FuelImportError list> =
        let prefix = $"rows[{index}]"
        let rowNumber = parseImportInt $"{prefix}.rowNumber" row.RowNumber
        let vehicleKey = requireCell $"{prefix}.vehicleKey" row.VehicleKey
        let occurredAt = parseImportDate $"{prefix}.occurredAt" row.OccurredAt
        let odometer = parseImportDecimal $"{prefix}.odometerMiles" row.OdometerMiles
        let fuelVolume = parseImportDecimal $"{prefix}.fuelVolumeGallons" row.FuelVolumeGallons
        let totalCost = parseImportDecimal $"{prefix}.totalCost" row.TotalCost
        let merchant = requireCell $"{prefix}.merchantName" row.MerchantName
        let externalReference = requireCell $"{prefix}.externalReference" row.ExternalReference
        let lookupStatus = requireCell $"{prefix}.vehicleLookupStatus" row.VehicleLookupStatus
        let duplicateStatus = requireCell $"{prefix}.duplicateStatus" row.DuplicateStatus

        match
            rowNumber,
            vehicleKey,
            occurredAt,
            odometer,
            fuelVolume,
            totalCost,
            merchant,
            externalReference,
            lookupStatus,
            duplicateStatus
        with
        | Ok rowNumber,
          Ok vehicleKey,
          Ok occurredAt,
          Ok odometer,
          Ok fuelVolume,
          Ok totalCost,
          Ok merchant,
          Ok externalReference,
          Ok lookupStatus,
          Ok duplicateStatus ->
            Ok
                ({ RowNumber = rowNumber
                   VehicleKey = vehicleKey
                   OccurredAt = occurredAt
                   OdometerMiles = odometer
                   FuelVolumeGallons = fuelVolume
                   TotalCost = totalCost
                   MerchantName = merchant
                   ExternalReference = externalReference
                   VehicleLookupStatus = lookupStatus
                   VehicleId = row.VehicleId
                   VehicleRegistration = row.VehicleRegistration
                   AmbiguousVehicleIds = row.AmbiguousVehicleIds
                   VehicleLookupError = row.VehicleLookupError
                   DuplicateStatus = duplicateStatus
                   PreviousAttempt = row.PreviousAttempt
                   DuplicateError = row.DuplicateError }
                 : FuelUploadRowDto)
        | _ ->
            Error(
                errorsOf rowNumber
                @ errorsOf vehicleKey
                @ errorsOf occurredAt
                @ errorsOf odometer
                @ errorsOf fuelVolume
                @ errorsOf totalCost
                @ errorsOf merchant
                @ errorsOf externalReference
                @ errorsOf lookupStatus
                @ errorsOf duplicateStatus
            )

    let toApplicationRequest (request: ImportBatchRequest) : Result<FuelUploadRequestDto, FuelImportError list> =
        let uploadMode = parseImportUploadMode "uploadMode" request.UploadMode
        let requireExternalReference = parseImportBool "requireExternalReference" request.RequireExternalReference
        let minFuelVolume = parseImportDecimal "minFuelVolumeGallons" request.MinFuelVolumeGallons
        let maxFuelVolume = parseImportDecimal "maxFuelVolumeGallons" request.MaxFuelVolumeGallons
        let minTotalCost = parseImportDecimal "minTotalCost" request.MinTotalCost
        let maxTotalCost = parseImportDecimal "maxTotalCost" request.MaxTotalCost
        let allowFuture = parseImportBool "allowFutureTransactions" request.AllowFutureTransactions
        let processingDate = parseImportDate "processingDate" request.ProcessingDate
        let highFuelVolume = parseImportDecimal "highFuelVolumeWarningGallons" request.HighFuelVolumeWarningGallons
        let highCost = parseImportDecimal "highCostPerGallonWarning" request.HighCostPerGallonWarning
        let staleDays = parseImportInt "staleTransactionWarningDays" request.StaleTransactionWarningDays
        let suspiciousFuelVolume = parseImportDecimal "suspiciousFuelVolumeGallons" request.SuspiciousFuelVolumeGallons
        let suspiciousTotalCost = parseImportDecimal "suspiciousTotalCost" request.SuspiciousTotalCost

        let rowResults, missingRows =
            if isNull request.Rows then
                [],
                [ importError FuelImportErrorCode.MissingRows "rows" "Rows are required." ]
            else
                request.Rows |> Array.mapi mapImportedRow |> Array.toList, []

        let errors =
            errorsOf uploadMode
            @ errorsOf requireExternalReference
            @ errorsOf minFuelVolume
            @ errorsOf maxFuelVolume
            @ errorsOf minTotalCost
            @ errorsOf maxTotalCost
            @ errorsOf allowFuture
            @ errorsOf processingDate
            @ errorsOf highFuelVolume
            @ errorsOf highCost
            @ errorsOf staleDays
            @ errorsOf suspiciousFuelVolume
            @ errorsOf suspiciousTotalCost
            @ (rowResults |> List.collect errorsOf)
            @ missingRows

        match
            uploadMode,
            requireExternalReference,
            minFuelVolume,
            maxFuelVolume,
            minTotalCost,
            maxTotalCost,
            allowFuture,
            processingDate,
            highFuelVolume,
            highCost,
            staleDays,
            suspiciousFuelVolume,
            suspiciousTotalCost,
            errors
        with
        | Ok uploadMode,
          Ok requireExternalReference,
          Ok minFuelVolume,
          Ok maxFuelVolume,
          Ok minTotalCost,
          Ok maxTotalCost,
          Ok allowFuture,
          Ok processingDate,
          Ok highFuelVolume,
          Ok highCost,
          Ok staleDays,
          Ok suspiciousFuelVolume,
          Ok suspiciousTotalCost,
          [] ->
            let rows =
                rowResults
                |> List.choose (function
                    | Ok row -> Some row
                    | Error _ -> None)
                |> List.toArray

            Ok
                ({ UploadMode = uploadMode
                   RequireExternalReference = requireExternalReference
                   MinFuelVolumeGallons = minFuelVolume
                   MaxFuelVolumeGallons = maxFuelVolume
                   MinTotalCost = minTotalCost
                   MaxTotalCost = maxTotalCost
                   AllowFutureTransactions = allowFuture
                   ProcessingDate = processingDate
                   HighFuelVolumeWarningGallons = highFuelVolume
                   HighCostPerGallonWarning = highCost
                   StaleTransactionWarningDays = staleDays
                   SuspiciousFuelVolumeGallons = suspiciousFuelVolume
                   SuspiciousTotalCost = suspiciousTotalCost
                   Rows = rows }
                 : FuelUploadRequestDto)
        | _ -> Error errors

    let private parseVehicleLookup prefix (row: FuelUploadRowDto) : Result<VehicleLookupResult, FuelUploadMappingError list> =
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

    let private parseDuplicateCheck prefix (row: FuelUploadRowDto) : Result<DuplicateCheckResult, FuelUploadMappingError list> =
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

    let private mapRow index (row: FuelUploadRowDto) : Result<FuelRowContext, FuelUploadMappingError list> =
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

    let private mapRepositoryRow
        (vehicleRepository: IVehicleRepository)
        (duplicateRepository: IDuplicateRepository)
        index
        (row: FuelUploadRowDto)
        : Result<FuelRowContext, FuelUploadMappingError list> =
        let prefix = $"rows[{index}]"
        let occurredAt = parseDate $"{prefix}.occurredAt" row.OccurredAt
        let vehicleKey = require $"{prefix}.vehicleKey" row.VehicleKey
        let externalReference = require $"{prefix}.externalReference" row.ExternalReference

        match occurredAt, vehicleKey, externalReference with
        | Ok occurredAt, Ok vehicleKey, Ok externalReference ->
            let vehicleLookup =
                match vehicleRepository.Lookup vehicleKey with
                | Ok lookup -> lookup
                | Error error -> VehicleLookupResult.Fatal(FatalProcessingError.VehicleLookupUnavailable error.Detail)

            let duplicateCheck =
                match vehicleLookup with
                | VehicleLookupResult.Fatal _ -> DuplicateCheckResult.NoDuplicate
                | _ ->
                    match
                        duplicateRepository.Lookup
                            { RowNumber = row.RowNumber
                              VehicleKey = vehicleKey
                              ExternalReference = externalReference }
                    with
                    | Ok lookup -> lookup
                    | Error error -> DuplicateCheckResult.Fatal(FatalProcessingError.DuplicateCheckUnavailable error.Detail)

            Ok
                { Row =
                    { RowNumber = row.RowNumber
                      VehicleKey = vehicleKey
                      OccurredAt = occurredAt
                      OdometerMiles = row.OdometerMiles
                      FuelVolumeGallons = row.FuelVolumeGallons
                      TotalCost = row.TotalCost
                      MerchantName = row.MerchantName
                      ExternalReference = externalReference }
                  VehicleLookup = vehicleLookup
                  DuplicateCheck = duplicateCheck }
        | _ -> Error(errorsOf occurredAt @ errorsOf vehicleKey @ errorsOf externalReference)

    let toDomainRequest (request: FuelUploadRequestDto) : Result<ValidationConfig * UploadMode * FuelRowContext list, FuelUploadMappingError list> =
        let mode = parseUploadMode "uploadMode" request.UploadMode
        let processingDate = parseDate "processingDate" request.ProcessingDate

        let rowResults, missingRowErrors =
            if isNull request.Rows then
                [],
                [ ({ Code = FuelUploadMappingErrorCode.MissingRequiredField
                     Field = "rows"
                     Detail = "Rows are required." }
                   : FuelUploadMappingError) ]
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

    let toRepositoryDomainRequest
        (vehicleRepository: IVehicleRepository)
        (duplicateRepository: IDuplicateRepository)
        (request: FuelUploadRequestDto)
        : Result<ValidationConfig * UploadMode * FuelRowContext list, FuelUploadMappingError list> =
        let mode = parseUploadMode "uploadMode" request.UploadMode
        let processingDate = parseDate "processingDate" request.ProcessingDate

        let rowResults, missingRowErrors =
            if isNull request.Rows then
                [],
                [ ({ Code = FuelUploadMappingErrorCode.MissingRequiredField
                     Field = "rows"
                     Detail = "Rows are required." }
                   : FuelUploadMappingError) ]
            else
                request.Rows
                |> Array.mapi (mapRepositoryRow vehicleRepository duplicateRepository)
                |> Array.toList,
                []

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

    let classify (request: FuelUploadRequestDto) =
        toDomainRequest request
        |> Result.map (fun (config, mode, rows) ->
            rows
            |> DecisionEngine.classifyBatch config mode
            |> toResponseDto)

    let classifyWithRepositories
        (vehicleRepository: IVehicleRepository)
        (duplicateRepository: IDuplicateRepository)
        (request: FuelUploadRequestDto) =
        toRepositoryDomainRequest vehicleRepository duplicateRepository request
        |> Result.map (fun (config, mode, rows) ->
            rows
            |> DecisionEngine.classifyBatch config mode
            |> toResponseDto)

    let private importErrorOfApplicationError (error: FuelUploadMappingError) : FuelImportError =
        let code =
            match error.Code with
            | FuelUploadMappingErrorCode.InvalidUploadMode -> FuelImportErrorCode.InvalidUploadMode
            | FuelUploadMappingErrorCode.InvalidDate -> FuelImportErrorCode.InvalidDate
            | FuelUploadMappingErrorCode.MissingRequiredField -> FuelImportErrorCode.MissingRequiredCell
            | _ -> FuelImportErrorCode.MissingRequiredCell

        importError code error.Field error.Detail

    let classifyImported (request: ImportBatchRequest) =
        match toApplicationRequest request with
        | Error errors -> Error errors
        | Ok applicationRequest ->
            match classify applicationRequest with
            | Ok response -> Ok response
            | Error errors -> Error(errors |> List.map importErrorOfApplicationError)

type FuelUploadFacade() =
    member _.Classify(request: FuelUploadRequestDto) = FuelUploadInterop.classify request
    member _.ClassifyImported(request: ImportBatchRequest) = FuelUploadInterop.classifyImported request

type RepositoryFuelUploadFacade(vehicleRepository: IVehicleRepository, duplicateRepository: IDuplicateRepository) =
    member _.Classify(request: FuelUploadRequestDto) =
        FuelUploadInterop.classifyWithRepositories vehicleRepository duplicateRepository request
