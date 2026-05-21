type rowId = RowId(string)
type externalTransactionId = ExternalTransactionId(string)
type vehicleKey = VehicleKey(string)
type vehicleId = VehicleId(string)
type canonicalTransactionId = CanonicalTransactionId(string)
type uploadAttemptId = UploadAttemptId(string)
type processingFailureId = ProcessingFailureId(string)
type isoInstantText = IsoInstantText(string)
type merchantName = MerchantName(string)

type nonEmpty<'a> = {
  first: 'a,
  rest: list<'a>,
}

type uploadMode =
  | Normal
  | Retry
  | Recovery

type fuelKind =
  | Diesel
  | Gasoline
  | Electric
  | Hydrogen

type odometerReading =
  | OdometerProvided(int)
  | OdometerNotProvided

type parsedFuelRow = {
  rowId: rowId,
  sourceLineNumber: int,
  externalTransactionId: externalTransactionId,
  vehicleKey: vehicleKey,
  purchasedAt: isoInstantText,
  merchantName: merchantName,
  fuelKind: fuelKind,
  fuelVolumeLitres: float,
  totalCostMinorUnits: int,
  odometer: odometerReading,
}

type vehicleInactiveReason =
  | Retired
  | Suspended

type vehicleStatus =
  | VehicleActive
  | VehicleInactive(vehicleInactiveReason)

type vehicle = {
  vehicleId: vehicleId,
  vehicleKey: vehicleKey,
  status: vehicleStatus,
  minimumAllowedOdometer: odometerReading,
}

type vehicleLookupFailure = VehicleLookupUnavailable(vehicleKey)

type vehicleLookupResult =
  | VehicleFound(vehicle)
  | VehicleNotFound(vehicleKey)
  | VehicleLookupFailed(vehicleLookupFailure)

type previousDuplicateState =
  | CanonicalFinalized(canonicalTransactionId)
  | RetryableFailure(uploadAttemptId)
  | NonRetryableFailure(uploadAttemptId)
  | FailedBeforeCanonicalFinalization(processingFailureId)
  | FailedAfterCanonicalFinalization(processingFailureId)

type duplicateRecord = {
  externalTransactionId: externalTransactionId,
  previousState: previousDuplicateState,
}

type duplicateCheckFailure = DuplicateCheckUnavailable(externalTransactionId)

type duplicateCheckResult =
  | NoDuplicate
  | DuplicateFound(duplicateRecord)
  | DuplicateCheckFailed(duplicateCheckFailure)

type odometerPolicy =
  | OdometerRequired
  | OdometerOptional

type numericRange = {
  minimumLitresInclusive: float,
  maximumLitresInclusive: float,
}

type intRange = {
  minimumMinorUnitsInclusive: int,
  maximumMinorUnitsInclusive: int,
}

type warningLimit =
  | WarningEnabled(float)
  | WarningDisabled

type costWarningLimit =
  | CostWarningEnabled(int)
  | CostWarningDisabled

type validationConfig = {
  allowedFuelKinds: nonEmpty<fuelKind>,
  fuelVolumeLitres: numericRange,
  totalCostMinorUnits: intRange,
  odometerPolicy: odometerPolicy,
  warningFuelVolumeLitres: warningLimit,
  warningTotalCostMinorUnits: costWarningLimit,
}

type validationConfigProblem =
  | FuelVolumeRangeInvalid(numericRange)
  | TotalCostRangeInvalid(intRange)

type validationError =
  | FuelKindNotAllowed({fuelKind: fuelKind, allowedFuelKinds: nonEmpty<fuelKind>})
  | FuelVolumeOutOfRange({actualLitres: float, allowedRange: numericRange})
  | TotalCostOutOfRange({actualMinorUnits: int, allowedRange: intRange})
  | VehicleMissing(vehicleKey)
  | VehicleInactiveError({vehicleId: vehicleId, reason: vehicleInactiveReason})
  | OdometerMissing(odometerPolicy)
  | OdometerBelowVehicleMinimum({actualKilometres: int, minimumKilometres: int})

type rejectionReason = ValidationFailed(nonEmpty<validationError>)

type uploadWarning =
  | FuelVolumeAboveWarningLimit({actualLitres: float, thresholdExclusive: float})
  | TotalCostAboveWarningLimit({actualMinorUnits: int, thresholdExclusive: int})
  | OdometerMissingButAllowed

type fatalProcessingError =
  | InvalidValidationConfig(nonEmpty<validationConfigProblem>)
  | VehicleLookupFatal(vehicleLookupFailure)
  | DuplicateCheckFatal(duplicateCheckFailure)

type fuelTransaction = {
  externalTransactionId: externalTransactionId,
  vehicleId: vehicleId,
  purchasedAt: isoInstantText,
  merchantName: merchantName,
  fuelKind: fuelKind,
  fuelVolumeLitres: float,
  totalCostMinorUnits: int,
  odometer: odometerReading,
}

type duplicateSkipReason =
  | DuplicateInNormalMode
  | DuplicateNotRetryable
  | DuplicateNotRecoverable

type rowDecision =
  | AcceptedTransaction({rowId: rowId, transaction: fuelTransaction})
  | SkippedDuplicate({
      rowId: rowId,
      mode: uploadMode,
      duplicate: duplicateRecord,
      reason: duplicateSkipReason,
    })
  | RejectedRow({rowId: rowId, reasons: nonEmpty<rejectionReason>})
  | WarningWithTransaction({
      rowId: rowId,
      transaction: fuelTransaction,
      warnings: nonEmpty<uploadWarning>,
    })
  | FatalProcessingError({rowId: rowId, error: fatalProcessingError})

type rowClassificationInput = {
  row: parsedFuelRow,
  vehicleLookup: vehicleLookupResult,
  duplicateCheck: duplicateCheckResult,
}

type batchClassificationInput = {
  mode: uploadMode,
  config: validationConfig,
  rows: list<rowClassificationInput>,
}

type batchSummary = {
  totalRows: int,
  acceptedRows: int,
  warningRows: int,
  skippedDuplicateRows: int,
  rejectedRows: int,
  fatalRows: int,
  transactionRows: int,
}

type batchDecision =
  | BatchUploadable({
      rowDecisions: list<rowDecision>,
      summary: batchSummary,
      uploadableTransactions: list<fuelTransaction>,
    })
  | BatchBlockedByFatalError({
      rowDecisions: list<rowDecision>,
      summary: batchSummary,
      fatalErrors: nonEmpty<fatalProcessingError>,
      uploadableTransactions: list<fuelTransaction>,
    })

type duplicatePolicyDecision =
  | DuplicateAllowed
  | DuplicateSkipped({duplicate: duplicateRecord, reason: duplicateSkipReason})

let consIf = (items, condition, item) => condition ? list{item, ...items} : items

let nonEmptyFromMatchedList = (first, rest) => {first, rest}

let allFuelKinds = fuelKinds => list{fuelKinds.first, ...fuelKinds.rest}

let fuelKindEquals = (left, right) =>
  switch (left, right) {
  | (Diesel, Diesel)
  | (Gasoline, Gasoline)
  | (Electric, Electric)
  | (Hydrogen, Hydrogen) =>
    true
  | _ => false
  }

let rec containsFuelKind = (fuelKind, allowedFuelKinds) =>
  switch allowedFuelKinds {
  | list{} => false
  | list{allowed, ...rest} =>
    fuelKindEquals(allowed, fuelKind) || containsFuelKind(fuelKind, rest)
  }

let isFuelKindAllowed = (fuelKind, allowedFuelKinds) =>
  containsFuelKind(fuelKind, allFuelKinds(allowedFuelKinds))

let isFloatWithin = (value, range) =>
  value >= range.minimumLitresInclusive && value <= range.maximumLitresInclusive

let isIntWithin = (value, range) =>
  value >= range.minimumMinorUnitsInclusive && value <= range.maximumMinorUnitsInclusive

let validateConfig = (config: validationConfig) => {
  let problems = list{}
    ->consIf(
      config.totalCostMinorUnits.minimumMinorUnitsInclusive >
        config.totalCostMinorUnits.maximumMinorUnitsInclusive,
      TotalCostRangeInvalid(config.totalCostMinorUnits),
    )
    ->consIf(
      config.fuelVolumeLitres.minimumLitresInclusive >
        config.fuelVolumeLitres.maximumLitresInclusive,
      FuelVolumeRangeInvalid(config.fuelVolumeLitres),
    )

  problems
}

let validateVehicle = (
  row: parsedFuelRow,
  vehicleLookup: vehicleLookupResult,
  config: validationConfig,
  existingErrors,
) =>
  switch vehicleLookup {
  | VehicleLookupFailed(_) => existingErrors
  | VehicleNotFound(vehicleKey) => list{VehicleMissing(vehicleKey), ...existingErrors}
  | VehicleFound(vehicle) =>
    let statusErrors =
      switch vehicle.status {
      | VehicleActive => existingErrors
      | VehicleInactive(reason) =>
        list{VehicleInactiveError({vehicleId: vehicle.vehicleId, reason}), ...existingErrors}
      }

    let odometerErrors =
      switch (config.odometerPolicy, row.odometer) {
      | (OdometerRequired, OdometerNotProvided) =>
        list{OdometerMissing(OdometerRequired), ...statusErrors}
      | (OdometerRequired, OdometerProvided(_))
      | (OdometerOptional, _) =>
        statusErrors
      }

    switch (row.odometer, vehicle.minimumAllowedOdometer) {
    | (OdometerProvided(actualKilometres), OdometerProvided(minimumKilometres))
        if actualKilometres < minimumKilometres =>
      list{
        OdometerBelowVehicleMinimum({actualKilometres, minimumKilometres}),
        ...odometerErrors,
      }
    | _ => odometerErrors
    }
  }

let validateRow = (row: parsedFuelRow, vehicleLookup: vehicleLookupResult, config: validationConfig) => {
  let baseErrors = list{}
    ->consIf(
      !isIntWithin(row.totalCostMinorUnits, config.totalCostMinorUnits),
      TotalCostOutOfRange({
        actualMinorUnits: row.totalCostMinorUnits,
        allowedRange: config.totalCostMinorUnits,
      }),
    )
    ->consIf(
      !isFloatWithin(row.fuelVolumeLitres, config.fuelVolumeLitres),
      FuelVolumeOutOfRange({
        actualLitres: row.fuelVolumeLitres,
        allowedRange: config.fuelVolumeLitres,
      }),
    )
    ->consIf(
      !isFuelKindAllowed(row.fuelKind, config.allowedFuelKinds),
      FuelKindNotAllowed({fuelKind: row.fuelKind, allowedFuelKinds: config.allowedFuelKinds}),
    )

  validateVehicle(row, vehicleLookup, config, baseErrors)
}

let classifyDuplicateFound = (duplicate, mode) =>
  switch mode {
  | Normal => DuplicateSkipped({duplicate, reason: DuplicateInNormalMode})
  | Retry =>
    switch duplicate.previousState {
    | RetryableFailure(_) => DuplicateAllowed
    | CanonicalFinalized(_)
    | NonRetryableFailure(_)
    | FailedBeforeCanonicalFinalization(_)
    | FailedAfterCanonicalFinalization(_) =>
      DuplicateSkipped({duplicate, reason: DuplicateNotRetryable})
    }
  | Recovery =>
    switch duplicate.previousState {
    | FailedBeforeCanonicalFinalization(_) => DuplicateAllowed
    | CanonicalFinalized(_)
    | RetryableFailure(_)
    | NonRetryableFailure(_)
    | FailedAfterCanonicalFinalization(_) =>
      DuplicateSkipped({duplicate, reason: DuplicateNotRecoverable})
    }
  }

let classifyDuplicate = (check, mode) =>
  switch check {
  | NoDuplicate => DuplicateAllowed
  | DuplicateFound(duplicate) => classifyDuplicateFound(duplicate, mode)
  | DuplicateCheckFailed(_) => DuplicateAllowed
  }

let buildTransaction = (row: parsedFuelRow, vehicle: vehicle): fuelTransaction => {
  externalTransactionId: row.externalTransactionId,
  vehicleId: vehicle.vehicleId,
  purchasedAt: row.purchasedAt,
  merchantName: row.merchantName,
  fuelKind: row.fuelKind,
  fuelVolumeLitres: row.fuelVolumeLitres,
  totalCostMinorUnits: row.totalCostMinorUnits,
  odometer: row.odometer,
}

let collectWarnings = (row: parsedFuelRow, config: validationConfig) => {
  let odometerWarnings =
    switch (config.odometerPolicy, row.odometer) {
    | (OdometerOptional, OdometerNotProvided) => list{OdometerMissingButAllowed}
    | _ => list{}
    }

  let costWarnings =
    switch config.warningTotalCostMinorUnits {
    | CostWarningEnabled(thresholdExclusive)
        if row.totalCostMinorUnits > thresholdExclusive =>
      list{
        TotalCostAboveWarningLimit({
          actualMinorUnits: row.totalCostMinorUnits,
          thresholdExclusive,
        }),
        ...odometerWarnings,
      }
    | CostWarningEnabled(_)
    | CostWarningDisabled =>
      odometerWarnings
    }

  switch config.warningFuelVolumeLitres {
  | WarningEnabled(thresholdExclusive) if row.fuelVolumeLitres > thresholdExclusive =>
    list{
      FuelVolumeAboveWarningLimit({
        actualLitres: row.fuelVolumeLitres,
        thresholdExclusive,
      }),
      ...costWarnings,
    }
  | WarningEnabled(_)
  | WarningDisabled =>
    costWarnings
  }
}

let classifyRow = (input: rowClassificationInput, mode: uploadMode, config: validationConfig) => {
  switch validateConfig(config) {
  | list{first, ...rest} =>
    FatalProcessingError({
      rowId: input.row.rowId,
      error: InvalidValidationConfig(nonEmptyFromMatchedList(first, rest)),
    })
  | list{} =>
    switch input.vehicleLookup {
    | VehicleLookupFailed(error) =>
      FatalProcessingError({rowId: input.row.rowId, error: VehicleLookupFatal(error)})
    | VehicleFound(_)
    | VehicleNotFound(_) =>
      switch input.duplicateCheck {
      | DuplicateCheckFailed(error) =>
        FatalProcessingError({rowId: input.row.rowId, error: DuplicateCheckFatal(error)})
      | NoDuplicate
      | DuplicateFound(_) =>
        switch validateRow(input.row, input.vehicleLookup, config) {
        | list{first, ...rest} =>
          RejectedRow({
            rowId: input.row.rowId,
            reasons: {first: ValidationFailed(nonEmptyFromMatchedList(first, rest)), rest: list{}},
          })
        | list{} =>
          switch classifyDuplicate(input.duplicateCheck, mode) {
          | DuplicateSkipped({duplicate, reason}) =>
            SkippedDuplicate({rowId: input.row.rowId, mode, duplicate, reason})
          | DuplicateAllowed =>
            switch input.vehicleLookup {
            | VehicleFound(vehicle) =>
              let transaction = buildTransaction(input.row, vehicle)
              switch collectWarnings(input.row, config) {
              | list{first, ...rest} =>
                WarningWithTransaction({
                  rowId: input.row.rowId,
                  transaction,
                  warnings: nonEmptyFromMatchedList(first, rest),
                })
              | list{} => AcceptedTransaction({rowId: input.row.rowId, transaction})
              }
            | VehicleNotFound(vehicleKey) =>
              RejectedRow({
                rowId: input.row.rowId,
                reasons: {
                  first: ValidationFailed({first: VehicleMissing(vehicleKey), rest: list{}}),
                  rest: list{},
                },
              })
            | VehicleLookupFailed(error) =>
              FatalProcessingError({rowId: input.row.rowId, error: VehicleLookupFatal(error)})
            }
          }
        }
      }
    }
  }
}

let emptySummary = totalRows => {
  totalRows,
  acceptedRows: 0,
  warningRows: 0,
  skippedDuplicateRows: 0,
  rejectedRows: 0,
  fatalRows: 0,
  transactionRows: 0,
}

let incrementSummary = (summary, decision) =>
  switch decision {
  | AcceptedTransaction(_) => {
      ...summary,
      acceptedRows: summary.acceptedRows + 1,
      transactionRows: summary.transactionRows + 1,
    }
  | WarningWithTransaction(_) => {
      ...summary,
      warningRows: summary.warningRows + 1,
      transactionRows: summary.transactionRows + 1,
    }
  | SkippedDuplicate(_) => {
      ...summary,
      skippedDuplicateRows: summary.skippedDuplicateRows + 1,
    }
  | RejectedRow(_) => {...summary, rejectedRows: summary.rejectedRows + 1}
  | FatalProcessingError(_) => {...summary, fatalRows: summary.fatalRows + 1}
  }

let rec listLength = items =>
  switch items {
  | list{} => 0
  | list{_, ...rest} => 1 + listLength(rest)
  }

let reverseList = items => {
  let rec loop = (remaining, reversed) =>
    switch remaining {
    | list{} => reversed
    | list{item, ...rest} => loop(rest, list{item, ...reversed})
    }

  loop(items, list{})
}

let mapList = (items, mapper) => {
  let rec loop = (remaining, mapped) =>
    switch remaining {
    | list{} => reverseList(mapped)
    | list{item, ...rest} => loop(rest, list{mapper(item), ...mapped})
    }

  loop(items, list{})
}

let summarize = rowDecisions => {
  let rec loop = (remaining, summary) =>
    switch remaining {
    | list{} => summary
    | list{decision, ...rest} => loop(rest, incrementSummary(summary, decision))
    }

  loop(rowDecisions, emptySummary(listLength(rowDecisions)))
}

let collectTransactions = rowDecisions => {
  let rec loop = (remaining, transactions) =>
    switch remaining {
    | list{} => reverseList(transactions)
    | list{AcceptedTransaction({transaction}), ...rest}
    | list{WarningWithTransaction({transaction}), ...rest} =>
      loop(rest, list{transaction, ...transactions})
    | list{SkippedDuplicate(_), ...rest}
    | list{RejectedRow(_), ...rest}
    | list{FatalProcessingError(_), ...rest} =>
      loop(rest, transactions)
    }

  loop(rowDecisions, list{})
}

let collectFatalErrors = rowDecisions => {
  let rec loop = (remaining, errors) =>
    switch remaining {
    | list{} => reverseList(errors)
    | list{FatalProcessingError({error}), ...rest} => loop(rest, list{error, ...errors})
    | list{AcceptedTransaction(_), ...rest}
    | list{WarningWithTransaction(_), ...rest}
    | list{SkippedDuplicate(_), ...rest}
    | list{RejectedRow(_), ...rest} =>
      loop(rest, errors)
    }

  loop(rowDecisions, list{})
}

let classifyBatch = input => {
  let rowDecisions = input.rows->mapList(rowInput =>
    classifyRow(rowInput, input.mode, input.config)
  )
  let summary = summarize(rowDecisions)

  switch collectFatalErrors(rowDecisions) {
  | list{first, ...rest} =>
    BatchBlockedByFatalError({
      rowDecisions,
      summary,
      fatalErrors: nonEmptyFromMatchedList(first, rest),
      uploadableTransactions: list{},
    })
  | list{} =>
    BatchUploadable({
      rowDecisions,
      summary,
      uploadableTransactions: collectTransactions(rowDecisions),
    })
  }
}
