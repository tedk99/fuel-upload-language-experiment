module FuelUpload.Engine.Tests

open System
open FuelUpload.Engine
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
      StaleTransactionWarningDays = 45 }

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
          PreviousAttemptState.FailedBeforeCanonicalFinalization ]

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
let ``recovery mode accepts only duplicates failed before canonical finalization`` () =
    let recoverable =
        classify
            UploadMode.Recovery
            (row 8)
            matched
            (DuplicateCheckResult.Duplicate PreviousAttemptState.FailedBeforeCanonicalFinalization)

    Assert.Equal(UploadMode.Recovery, (assertAccepted recoverable).Mode)

    let canonicalizedStates =
        [ PreviousAttemptState.Finalized
          PreviousAttemptState.RetryableFailure
          PreviousAttemptState.NonRetryableFailure ]

    for state in canonicalizedStates do
        let decision = classify UploadMode.Recovery (row 9) matched (DuplicateCheckResult.Duplicate state)

        match decision with
        | RowDecision.SkippedDuplicate skipped ->
            Assert.Equal(
                DuplicateSkipReason.RecoveryModeDuplicateAlreadyCanonicalized state,
                skipped.Reason
            )
        | other -> failwith $"Expected recovery duplicate skip for %A{state}, got %A{other}"

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

[<Fact>]
let ``static facade is callable and delegates to pure classifier`` () =
    let decision =
        FuelUploadDecisionEngine.ClassifyRow(config, UploadMode.Normal, row 18, matched, noDuplicate)

    Assert.Equal(18, (assertAccepted decision).SourceRowNumber)
