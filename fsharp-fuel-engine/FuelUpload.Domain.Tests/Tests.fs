module FuelUpload.Domain.Tests

open System
open FuelUpload.Domain
open Xunit

let processingDate = DateTimeOffset(2026, 05, 21, 12, 0, 0, TimeSpan.Zero)

let config =
    { RequireExternalReference = true
      MinFuelVolumeGallons = 0.01m
      MaxFuelVolumeGallons = 80m
      MinTotalCost = 0.01m
      MaxTotalCost = 500m
      AllowFutureTransactions = false
      ProcessingDate = processingDate
      HighFuelVolumeWarningGallons = 60m
      HighCostPerGallonWarning = 9m
      StaleTransactionWarningDays = 45
      SuspiciousFuelVolumeGallons = 33m
      SuspiciousTotalCost = 99m }

let row number =
    { RowNumber = number
      VehicleKey = $"truck-{number}"
      OccurredAt = processingDate.AddDays(-1)
      OdometerMiles = 12000m + decimal number
      FuelVolumeGallons = 20m
      TotalCost = 70m
      MerchantName = "Depot Fuel"
      ExternalReference = $"ext-{number}" }

let vehicle =
    { VehicleId = "veh-1"
      Registration = "REG-1" }

let matched = VehicleLookupResult.Matched vehicle
let noDuplicate = DuplicateCheckResult.NoDuplicate

let classify mode row vehicleLookup duplicate =
    DecisionEngine.classifyRow config mode row vehicleLookup duplicate

let assertAccepted decision =
    match decision with
    | RowDecision.Accepted transaction -> transaction
    | RowDecision.AcceptedWithWarnings(transaction, _) -> transaction
    | other -> failwith $"Expected accepted transaction, got %A{other}"

let context rowNumber vehicleLookup duplicate =
    { Row = row rowNumber
      VehicleLookup = vehicleLookup
      DuplicateCheck = duplicate }

[<Fact>]
let ``valid normal row is accepted without warnings`` () =
    let decision = classify UploadMode.Normal (row 1) matched noDuplicate

    match decision with
    | RowDecision.Accepted transaction ->
        Assert.Equal(1, transaction.SourceRowNumber)
        Assert.Equal(vehicle, transaction.Vehicle)
        Assert.Equal(UploadMode.Normal, transaction.Mode)
    | other -> failwith $"Expected accepted row, got %A{other}"

[<Fact>]
let ``validation errors reject row and never create transaction`` () =
    let invalidRow =
        { row 2 with
            VehicleKey = " "
            FuelVolumeGallons = 0m
            TotalCost = 20m
            ExternalReference = "" }

    let decision = classify UploadMode.Normal invalidRow matched noDuplicate

    match decision with
    | RowDecision.Rejected rejected ->
        match rejected.Reasons with
        | [ RejectionReason.ValidationFailed errors ] ->
            Assert.Contains(ValidationError.MissingVehicleKey, errors)
            Assert.Contains(ValidationError.MissingExternalReference, errors)

            Assert.Contains(
                ValidationError.InvalidFuelVolume(0m, config.MinFuelVolumeGallons, config.MaxFuelVolumeGallons),
                errors
            )
        | reasons -> failwith $"Expected validation rejection, got %A{reasons}"
    | other -> failwith $"Expected rejected row, got %A{other}"

[<Fact>]
let ``vehicle lookup miss rejects with typed vehicle reason`` () =
    let decision = classify UploadMode.Normal (row 3) VehicleLookupResult.NotFound noDuplicate

    match decision with
    | RowDecision.Rejected rejected ->
        Assert.Equal<RejectionReason list>(
            [ RejectionReason.VehicleRejected VehicleRejectionReason.UnknownVehicle ],
            rejected.Reasons
        )
    | other -> failwith $"Expected vehicle rejection, got %A{other}"

[<Fact>]
let ``normal mode skips every duplicate`` () =
    let states =
        [ PreviousAttemptState.Finalized
          PreviousAttemptState.RetryableFailure
          PreviousAttemptState.NonRetryableFailure
          PreviousAttemptState.FailedBeforeCanonicalFinalization
          PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey
          PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey ]

    for state in states do
        let decision = classify UploadMode.Normal (row 4) matched (DuplicateCheckResult.Duplicate state)

        match decision with
        | RowDecision.SkippedDuplicate skipped ->
            Assert.Equal(state, skipped.PreviousAttempt)
            Assert.Equal(DuplicateSkipReason.NormalModeDuplicate, skipped.Reason)
        | other -> failwith $"Expected normal duplicate skip for %A{state}, got %A{other}"

[<Fact>]
let ``retry mode accepts only explicitly retryable duplicates`` () =
    let retryable =
        classify
            UploadMode.Retry
            (row 5)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.RetryableFailure)

    Assert.Equal(UploadMode.Retry, (assertAccepted retryable).Mode)

    let finalized =
        classify
            UploadMode.Retry
            (row 6)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.Finalized)

    match finalized with
    | RowDecision.SkippedDuplicate skipped ->
        Assert.Equal(DuplicateSkipReason.RetryModeDuplicateAlreadyFinalized, skipped.Reason)
    | other -> failwith $"Expected retry finalized duplicate skip, got %A{other}"

    let notRetryable =
        classify
            UploadMode.Retry
            (row 7)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.NonRetryableFailure)

    match notRetryable with
    | RowDecision.SkippedDuplicate skipped ->
        Assert.Equal(
            DuplicateSkipReason.RetryModeDuplicateNotRetryable PreviousAttemptState.NonRetryableFailure,
            skipped.Reason
        )
    | other -> failwith $"Expected retry non-retryable duplicate skip, got %A{other}"

[<Fact>]
let ``conservative recovery preserves old recovery behavior`` () =
    let recoverable =
        classify
            UploadMode.ConservativeRecovery
            (row 8)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.FailedBeforeCanonicalFinalization)

    Assert.Equal(UploadMode.ConservativeRecovery, (assertAccepted recoverable).Mode)

    let canonicalizedStates =
        [ PreviousAttemptState.Finalized
          PreviousAttemptState.RetryableFailure
          PreviousAttemptState.NonRetryableFailure
          PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey
          PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey ]

    for state in canonicalizedStates do
        let decision =
            classify
                UploadMode.ConservativeRecovery
                (row 9)
                matched
                (DuplicateCheckResult.Duplicate state)

        match decision with
        | RowDecision.SkippedDuplicate skipped ->
            Assert.Equal(
                DuplicateSkipReason.RecoveryModeDuplicateAlreadyCanonicalized state,
                skipped.Reason
            )
        | other -> failwith $"Expected recovery duplicate skip for %A{state}, got %A{other}"

[<Fact>]
let ``aggressive recovery accepts failed after canonicalization only without canonical transaction key`` () =
    let accepted =
        classify
            UploadMode.AggressiveRecovery
            (row 24)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey)

    Assert.Equal(UploadMode.AggressiveRecovery, (assertAccepted accepted).Mode)

    let skipped =
        classify
            UploadMode.AggressiveRecovery
            (row 25)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey)

    match skipped with
    | RowDecision.SkippedDuplicate skipped ->
        Assert.Equal(
            DuplicateSkipReason.RecoveryModeDuplicateAlreadyCanonicalized
                PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey,
            skipped.Reason
        )
    | other -> failwith $"Expected aggressive recovery duplicate skip, got %A{other}"

[<Fact>]
let ``recovery modes still quarantine accepted suspicious duplicates`` () =
    let cases =
        [ UploadMode.ConservativeRecovery, PreviousAttemptState.FailedBeforeCanonicalFinalization
          UploadMode.AggressiveRecovery, PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey ]

    for mode, state in cases do
        let decision =
            classify
                mode
                { row 26 with MerchantName = "manual review" }
                matched
                (DuplicateCheckResult.Duplicate state)

        match decision with
        | RowDecision.Quarantined _ -> ()
        | other -> failwith $"Expected quarantined recovery duplicate, got %A{other}"

[<Fact>]
let ``warnings are returned with accepted transactions and do not block upload`` () =
    let warningRow =
        { row 10 with
            OccurredAt = processingDate.AddDays(-60)
            FuelVolumeGallons = 70m
            TotalCost = 700m }

    let warningConfig = { config with MaxTotalCost = 1000m }
    let decision = DecisionEngine.classifyRow warningConfig UploadMode.Normal warningRow matched noDuplicate

    match decision with
    | RowDecision.AcceptedWithWarnings(transaction, warnings) ->
        Assert.Equal(10, transaction.SourceRowNumber)
        Assert.Contains(Warning.HighFuelVolume(70m, 60m), warnings)
        Assert.Contains(Warning.HighCostPerGallon(10m, 9m), warnings)
        Assert.Contains(Warning.StaleTransaction(60, 45), warnings)
    | other -> failwith $"Expected accepted row with warnings, got %A{other}"

[<Fact>]
let ``suspicious row is quarantined with typed reason`` () =
    let suspiciousRow = { row 18 with MerchantName = "Manual fuel entry" }

    let decision = classify UploadMode.Normal suspiciousRow matched noDuplicate

    match decision with
    | RowDecision.Quarantined quarantined ->
        Assert.Equal(18, quarantined.Transaction.SourceRowNumber)
        Assert.Contains(QuarantineReason.SuspiciousMerchantName, QuarantineReasons.toList quarantined.Reasons)
    | other -> failwith $"Expected quarantined row, got %A{other}"

[<Fact>]
let ``validation error is rejected instead of quarantined`` () =
    let invalidSuspiciousRow =
        { row 19 with
            MerchantName = "manual review"
            FuelVolumeGallons = 0m }

    let decision = classify UploadMode.Normal invalidSuspiciousRow matched noDuplicate

    match decision with
    | RowDecision.Rejected _ -> ()
    | other -> failwith $"Expected rejected row, got %A{other}"

[<Fact>]
let ``normal duplicate is skipped instead of quarantined`` () =
    let suspiciousRow = { row 20 with MerchantName = "test depot" }

    let decision =
        classify
            UploadMode.Normal
            suspiciousRow
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.Finalized)

    match decision with
    | RowDecision.SkippedDuplicate _ -> ()
    | other -> failwith $"Expected duplicate skip, got %A{other}"

[<Fact>]
let ``warning does not become quarantine`` () =
    let warningRow =
        { row 21 with
            FuelVolumeGallons = 70m
            TotalCost = 700m }

    let warningConfig = { config with MaxTotalCost = 1000m }
    let decision = DecisionEngine.classifyRow warningConfig UploadMode.Normal warningRow matched noDuplicate

    match decision with
    | RowDecision.AcceptedWithWarnings _ -> ()
    | other -> failwith $"Expected warning decision, got %A{other}"

[<Fact>]
let ``quarantined row does not upload or block and appears in summary`` () =
    let quarantinedContext =
        { Row = { row 23 with FuelVolumeGallons = 33m }
          VehicleLookup = matched
          DuplicateCheck = noDuplicate }

    let batch =
        DecisionEngine.classifyBatch
            config
            UploadMode.Normal
            [ context 22 matched noDuplicate
              quarantinedContext ]

    match batch with
    | BatchDecision.Ready(rows, summary) ->
        Assert.Equal(2, rows.Length)
        Assert.Equal(1, summary.AcceptedRows)
        Assert.Equal(1, summary.QuarantinedRows)
        Assert.True(rows |> List.exists (fun classified ->
            match classified.Decision with
            | RowDecision.Quarantined _ -> true
            | _ -> false))
    | other -> failwith $"Expected ready batch, got %A{other}"

[<Fact>]
let ``batch summary is derived from per row decisions`` () =
    let warningConfig = { config with MaxTotalCost = 1000m }

    let warningContext =
        { Row =
            { row 12 with
                FuelVolumeGallons = 70m
                TotalCost = 700m }
          VehicleLookup = matched
          DuplicateCheck = noDuplicate }

    let skippedContext =
        { Row = row 13
          VehicleLookup = matched
          DuplicateCheck = DuplicateCheckResult.Duplicate PreviousAttemptState.Finalized }

    let rejectedContext =
        { Row = { row 14 with MerchantName = "" }
          VehicleLookup = matched
          DuplicateCheck = noDuplicate }

    let batch =
        DecisionEngine.classifyBatch
            warningConfig
            UploadMode.Normal
            [ context 11 matched noDuplicate
              warningContext
              skippedContext
              rejectedContext ]

    match batch with
    | BatchDecision.Ready(_, summary) ->
        Assert.Equal(4, summary.TotalRows)
        Assert.Equal(2, summary.AcceptedRows)
        Assert.Equal(1, summary.AcceptedWithWarningRows)
        Assert.Equal(2, summary.WarningCount)
        Assert.Equal(0, summary.QuarantinedRows)
        Assert.Equal(1, summary.SkippedDuplicateRows)
        Assert.Equal(1, summary.RejectedRows)
        Assert.Equal(0, summary.FatalErrorRows)
    | other -> failwith $"Expected ready batch, got %A{other}"

[<Fact>]
let ``fatal row blocks entire batch`` () =
    let fatal = FatalProcessingError.DuplicateCheckUnavailable "duplicate store timed out"

    let batch =
        DecisionEngine.classifyBatch
            config
            UploadMode.Normal
            [ context 15 matched noDuplicate
              context 16 matched (DuplicateCheckResult.Fatal fatal)
              context 17 VehicleLookupResult.NotFound noDuplicate ]

    match batch with
    | BatchDecision.Blocked(rows, summary, fatalErrors) ->
        Assert.Equal(3, rows.Length)
        Assert.Equal(1, summary.AcceptedRows)
        Assert.Equal(1, summary.RejectedRows)
        Assert.Equal(1, summary.FatalErrorRows)
        Assert.Equal<FatalProcessingError list>([ fatal ], fatalErrors)
    | other -> failwith $"Expected blocked batch, got %A{other}"

let validDtoRow number =
    { RowNumber = number
      VehicleKey = $"truck-{number}"
      OccurredAt = "2026-05-20T12:00:00+00:00"
      OdometerMiles = 12000m + decimal number
      FuelVolumeGallons = 20m
      TotalCost = 70m
      MerchantName = "Depot Fuel"
      ExternalReference = $"ext-{number}"
      VehicleLookupStatus = "matched"
      VehicleId = "veh-1"
      VehicleRegistration = "REG-1"
      AmbiguousVehicleIds = [||]
      VehicleLookupError = ""
      DuplicateStatus = "no_duplicate"
      PreviousAttempt = ""
      DuplicateError = "" }

let validDtoRequest rows =
    { UploadMode = "normal"
      RequireExternalReference = true
      MinFuelVolumeGallons = 0.01m
      MaxFuelVolumeGallons = 80m
      MinTotalCost = 0.01m
      MaxTotalCost = 500m
      AllowFutureTransactions = false
      ProcessingDate = "2026-05-21T12:00:00+00:00"
      HighFuelVolumeWarningGallons = 60m
      HighCostPerGallonWarning = 9m
      StaleTransactionWarningDays = 45
      SuspiciousFuelVolumeGallons = 33m
      SuspiciousTotalCost = 99m
      Rows = rows }

[<Fact>]
let ``valid DTO maps and classifies through facade`` () =
    match FuelUploadFacade().Classify(validDtoRequest [| validDtoRow 1 |]) with
    | Ok response ->
        Assert.Equal(1, response.TotalRows)
        Assert.Equal(1, response.AcceptedRows)
        Assert.Equal("accepted", response.Decisions[0].Outcome)
    | Error errors -> failwith $"Expected DTO to classify, got %A{errors}"

[<Fact>]
let ``invalid DTO produces typed mapping error`` () =
    let request = { validDtoRequest [| validDtoRow 1 |] with UploadMode = "eventual" }

    match FuelUploadInterop.toDomainRequest request with
    | Error errors ->
        Assert.Contains(
            errors,
            fun error ->
                error.Code = FuelUploadMappingErrorCode.InvalidUploadMode
                && error.Field = "uploadMode"
        )
    | Ok _ -> failwith "Expected invalid upload mode mapping error"

[<Fact>]
let ``response DTO represents all decision outcomes`` () =
    let rows =
        [| validDtoRow 1
           { validDtoRow 2 with
                FuelVolumeGallons = 70m }
           { validDtoRow 3 with
                FuelVolumeGallons = 33m }
           { validDtoRow 4 with
                DuplicateStatus = "duplicate"
                PreviousAttempt = "finalized" }
           { validDtoRow 5 with
                MerchantName = "" }
           { validDtoRow 6 with
                DuplicateStatus = "fatal"
                DuplicateError = "duplicate store timed out" } |]

    match FuelUploadFacade().Classify(validDtoRequest rows) with
    | Ok response ->
        let outcomes = response.Decisions |> Array.map _.Outcome |> Set.ofArray
        Assert.Contains("accepted", outcomes)
        Assert.Contains("accepted_with_warnings", outcomes)
        Assert.Contains("quarantined", outcomes)
        Assert.Contains("skipped_duplicate", outcomes)
        Assert.Contains("rejected", outcomes)
        Assert.Contains("fatal", outcomes)
    | Error errors -> failwith $"Expected DTO to classify, got %A{errors}"

[<Fact>]
let ``response DTO uses domain summary without recomputing it`` () =
    let classified =
        [ { Row = row 1
            Decision =
                RowDecision.Accepted
                    { TransactionId = "txn-1"
                      SourceRowNumber = 1
                      Vehicle = vehicle
                      OccurredAt = processingDate
                      OdometerMiles = 10m
                      FuelVolumeGallons = 10m
                      TotalCost = 20m
                      MerchantName = "Depot"
                      ExternalReference = "ext-1"
                      Mode = UploadMode.Normal } } ]

    let summary =
        { TotalRows = 99
          AcceptedRows = 42
          AcceptedWithWarningRows = 5
          WarningCount = 7
          QuarantinedRows = 6
          SkippedDuplicateRows = 4
          RejectedRows = 3
          FatalErrorRows = 2 }

    let response = FuelUploadInterop.toResponseDto (BatchDecision.Ready(classified, summary))

    Assert.Equal(99, response.TotalRows)
    Assert.Equal(42, response.AcceptedRows)
    Assert.Equal(6, response.QuarantinedRows)
    Assert.Equal(7, response.WarningCount)
