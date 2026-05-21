namespace FuelUpload.Domain

module DecisionEngine =
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
        let warnings = Validation.warningsFor config row

        match Validation.quarantineReasonsFor config row |> QuarantineReasons.create with
        | Some reasons ->
            RowDecision.Quarantined
                { Transaction = transaction
                  Reasons = reasons
                  Warnings = warnings }
        | None ->
            match warnings with
            | [] -> RowDecision.Accepted transaction
            | warnings -> RowDecision.AcceptedWithWarnings(transaction, warnings)


    let classifyRow
        (config: ValidationConfig)
        (mode: UploadMode)
        (row: ParsedFuelRow)
        (vehicleLookup: VehicleLookupResult)
        (duplicateCheck: DuplicateCheckResult)
        : RowDecision =
        match Validation.validateConfig config, vehicleLookup, duplicateCheck with
        | fatal :: _, _, _ -> RowDecision.Fatal fatal
        | _, VehicleLookupResult.Fatal fatal, _ -> RowDecision.Fatal fatal
        | _, _, DuplicateCheckResult.Fatal fatal -> RowDecision.Fatal fatal
        | [], _, _ ->
            match Validation.validateRow config row with
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

        let summary = BatchSummary.summarize classified

        let fatalErrors =
            classified
            |> List.choose (fun row ->
                match row.Decision with
                | RowDecision.Fatal fatal -> Some fatal
                | _ -> None)

        match fatalErrors with
        | [] -> BatchDecision.Ready(classified, summary)
        | errors -> BatchDecision.Blocked(classified, summary, errors)
