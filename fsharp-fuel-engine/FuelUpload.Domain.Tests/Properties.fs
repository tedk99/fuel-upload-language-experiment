module FuelUpload.Domain.PropertyTests

open System
open FuelUpload.Domain
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit

let private processingDate = DateTimeOffset(2026, 05, 21, 12, 0, 0, TimeSpan.Zero)

let private config =
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

let private vehicle =
    { VehicleId = "veh-1"
      Registration = "REG-1" }

let private baseRow n =
    { RowNumber = n
      VehicleKey = $"truck-{n}"
      OccurredAt = processingDate.AddDays(-1)
      OdometerMiles = 12000m + decimal n
      FuelVolumeGallons = 20m
      TotalCost = 70m
      MerchantName = "Depot Fuel"
      ExternalReference = $"ext-{n}" }

let private baseTransaction n =
    { TransactionId = $"txn-{n}"
      SourceRowNumber = n
      Vehicle = vehicle
      OccurredAt = processingDate
      OdometerMiles = 10m
      FuelVolumeGallons = 10m
      TotalCost = 20m
      MerchantName = "Depot Fuel"
      ExternalReference = $"ext-{n}"
      Mode = UploadMode.Normal }

let private rowNumberGen = Gen.choose (1, 100000)

let private fatalGen : Gen<FatalProcessingError> =
    Gen.elements
        [ FatalProcessingError.VehicleLookupUnavailable "lookup down"
          FatalProcessingError.DuplicateCheckUnavailable "duplicate store timed out" ]

let private warningGen : Gen<Warning> =
    Gen.elements
        [ Warning.HighFuelVolume(70m, 60m)
          Warning.HighCostPerGallon(10m, 9m)
          Warning.StaleTransaction(60, 45) ]

let private quarantineReasonGen : Gen<QuarantineReason> =
    Gen.elements
        [ QuarantineReason.SuspiciousMerchantName
          QuarantineReason.SuspiciousQuantityPattern
          QuarantineReason.SuspiciousCostPattern ]

let private quarantineReasonsGen : Gen<QuarantineReasons> =
    gen {
        let! first = quarantineReasonGen
        let! rest = Gen.listOf quarantineReasonGen
        return QuarantineReasons.create (first :: rest) |> Option.get
    }

let private previousAttemptGen : Gen<PreviousAttemptState> =
    Gen.elements
        [ PreviousAttemptState.Finalized
          PreviousAttemptState.RetryableFailure
          PreviousAttemptState.NonRetryableFailure
          PreviousAttemptState.FailedBeforeCanonicalFinalization
          PreviousAttemptState.FailedAfterCanonicalizationWithCanonicalTransactionKey
          PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey ]

let private uploadModeGen : Gen<UploadMode> =
    Gen.elements
        [ UploadMode.Normal
          UploadMode.Retry
          UploadMode.ConservativeRecovery
          UploadMode.AggressiveRecovery ]

let private rejectionGen : Gen<RejectionReason> =
    Gen.elements
        [ RejectionReason.VehicleRejected VehicleRejectionReason.UnknownVehicle
          RejectionReason.ValidationFailed [ ValidationError.MissingVehicleKey ]
          RejectionReason.ValidationFailed [ ValidationError.MissingMerchantName ] ]

let private decisionGen : Gen<RowDecision> =
    gen {
        let! n = rowNumberGen
        let row = baseRow n
        let txn = baseTransaction n

        return!
            Gen.oneof
                [ Gen.constant (RowDecision.Accepted txn)
                  gen {
                      let! first = warningGen
                      let! rest = Gen.listOf warningGen
                      return RowDecision.AcceptedWithWarnings(txn, first :: rest)
                  }
                  gen {
                      let! reasons = quarantineReasonsGen
                      let! warnings = Gen.listOf warningGen

                      return
                          RowDecision.Quarantined
                              { Transaction = txn
                                Reasons = reasons
                                Warnings = warnings }
                  }
                  gen {
                      let! mode = uploadModeGen
                      let! prev = previousAttemptGen

                      return
                          RowDecision.SkippedDuplicate
                              { Row = row
                                Mode = mode
                                PreviousAttempt = prev
                                Reason = DuplicateSkipReason.NormalModeDuplicate }
                  }
                  gen {
                      let! reason = rejectionGen
                      return RowDecision.Rejected { Row = row; Reasons = [ reason ] }
                  }
                  gen {
                      let! fatal = fatalGen
                      return RowDecision.Fatal fatal
                  } ]
    }

let private uploadableContextGen : Gen<FuelRowContext> =
    gen {
        let! n = rowNumberGen

        return
            { Row = baseRow n
              VehicleLookup = VehicleLookupResult.Matched vehicle
              DuplicateCheck = DuplicateCheckResult.NoDuplicate }
    }

let private fatalContextGen : Gen<FuelRowContext> =
    gen {
        let! n = rowNumberGen
        let! fatal = fatalGen
        let! viaDuplicate = Gen.elements [ true; false ]
        let row = baseRow n

        if viaDuplicate then
            return
                { Row = row
                  VehicleLookup = VehicleLookupResult.Matched vehicle
                  DuplicateCheck = DuplicateCheckResult.Fatal fatal }
        else
            return
                { Row = row
                  VehicleLookup = VehicleLookupResult.Fatal fatal
                  DuplicateCheck = DuplicateCheckResult.NoDuplicate }
    }

let private nonEmptyFatalContextsGen : Gen<FuelRowContext list> =
    gen {
        let! prefix = Gen.listOf uploadableContextGen
        let! suffix = Gen.listOf uploadableContextGen
        let! fatal = fatalContextGen
        return prefix @ [ fatal ] @ suffix
    }

let private acceptedRowGen : Gen<ParsedFuelRow> =
    gen {
        let! n = rowNumberGen
        let! volume = Gen.choose (1, 30)
        let! cost = Gen.choose (1, 80)
        let! odometer = Gen.choose (0, 300000)

        return
            { baseRow n with
                FuelVolumeGallons = decimal volume
                TotalCost = decimal cost
                OdometerMiles = decimal odometer
                MerchantName = "Depot Fuel" }
    }

type DomainGenerators =
    static member Decisions() = Arb.fromGen (Gen.listOf decisionGen)
    static member NonEmptyFatalContexts() = Arb.fromGen nonEmptyFatalContextsGen
    static member AcceptedRow() = Arb.fromGen acceptedRowGen

[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``summary total is the number of row decisions`` (decisions: RowDecision list) =
    let classified =
        decisions
        |> List.mapi (fun i d -> { Row = baseRow (i + 1); Decision = d })

    (BatchSummary.summarize classified).TotalRows = decisions.Length

[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``summary count partitions always add up to total`` (decisions: RowDecision list) =
    let classified =
        decisions
        |> List.mapi (fun i d -> { Row = baseRow (i + 1); Decision = d })

    let s = BatchSummary.summarize classified

    s.AcceptedRows
    + s.QuarantinedRows
    + s.SkippedDuplicateRows
    + s.RejectedRows
    + s.FatalErrorRows = s.TotalRows

[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``fatal row decisions block the batch`` (contexts: FuelRowContext list) =
    match DecisionEngine.classifyBatch config UploadMode.Normal contexts with
    | BatchDecision.Blocked _ -> true
    | BatchDecision.Ready _ -> false

[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``accepted row decisions never contain validation errors`` (row: ParsedFuelRow) =
    let decision =
        DecisionEngine.classifyRow
            config
            UploadMode.Normal
            row
            (VehicleLookupResult.Matched vehicle)
            DuplicateCheckResult.NoDuplicate

    match decision with
    | RowDecision.Accepted _ -> true
    | RowDecision.AcceptedWithWarnings _ -> true
    | _ -> false
