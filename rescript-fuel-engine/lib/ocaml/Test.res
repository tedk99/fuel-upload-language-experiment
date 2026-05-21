@module("node:assert/strict") external equal: ('a, 'a) => unit = "equal"
@module("node:assert/strict") external deepEqual: ('a, 'a) => unit = "deepEqual"

let assertTrue = (condition, message) => {
  if !condition {
    JsError.throwWithMessage(message)
  }
}

let diesel = Engine.Diesel
let gasoline = Engine.Gasoline
let normal = Engine.Normal
let retry = Engine.Retry
let recovery = Engine.Recovery

let config: Engine.validationConfig = {
  allowedFuelKinds: {first: diesel, rest: list{gasoline}},
  fuelVolumeLitres: {minimumLitresInclusive: 1.0, maximumLitresInclusive: 500.0},
  totalCostMinorUnits: {minimumMinorUnitsInclusive: 1, maximumMinorUnitsInclusive: 200000},
  odometerPolicy: Engine.OdometerRequired,
  warningFuelVolumeLitres: Engine.WarningEnabled(250.0),
  warningTotalCostMinorUnits: Engine.CostWarningEnabled(100000),
}

let vehicle: Engine.vehicle = {
  vehicleId: Engine.VehicleId("vehicle-1"),
  vehicleKey: Engine.VehicleKey("fleet-1"),
  status: Engine.VehicleActive,
  minimumAllowedOdometer: Engine.OdometerProvided(10000),
}

let foundVehicle = Engine.VehicleFound(vehicle)
let noDuplicate = Engine.NoDuplicate

let baseRow: Engine.parsedFuelRow = {
  rowId: Engine.RowId("row-1"),
  sourceLineNumber: 2,
  externalTransactionId: Engine.ExternalTransactionId("external-1"),
  vehicleKey: Engine.VehicleKey("fleet-1"),
  purchasedAt: Engine.IsoInstantText("2026-05-21T09:00:00Z"),
  merchantName: Engine.MerchantName("Depot A"),
  fuelKind: diesel,
  fuelVolumeLitres: 100.0,
  totalCostMinorUnits: 50000,
  odometer: Engine.OdometerProvided(12000),
}

let duplicate = previousState =>
  Engine.DuplicateFound({
    externalTransactionId: Engine.ExternalTransactionId("external-1"),
    previousState,
  })

let listCount = items => {
  let rec loop = (remaining, count) =>
    switch remaining {
    | list{} => count
    | list{_, ...rest} => loop(rest, count + 1)
    }

  loop(items, 0)
}

let warningCount = warnings => 1 + listCount(warnings.Engine.rest)
let rejectionCount = reasons => 1 + listCount(reasons.Engine.rest)

let testAcceptedValidRow = () => {
  let decision = Engine.classifyRow(
    {row: baseRow, vehicleLookup: foundVehicle, duplicateCheck: noDuplicate},
    normal,
    config,
  )

  switch decision {
  | Engine.AcceptedTransaction({transaction}) =>
    equal(transaction.vehicleId, vehicle.vehicleId)
    equal(transaction.externalTransactionId, Engine.ExternalTransactionId("external-1"))
  | _ => assertTrue(false, "expected AcceptedTransaction")
  }
}

let testValidationRejectsWithoutTransaction = () => {
  let decision = Engine.classifyRow(
    {
      row: {...baseRow, fuelVolumeLitres: 0.0},
      vehicleLookup: foundVehicle,
      duplicateCheck: noDuplicate,
    },
    normal,
    config,
  )

  switch decision {
  | Engine.RejectedRow({reasons}) =>
    equal(rejectionCount(reasons), 1)
    switch reasons.first {
    | Engine.ValidationFailed(errors) =>
      switch errors.first {
      | Engine.FuelVolumeOutOfRange(_) => ()
      | _ => assertTrue(false, "expected FuelVolumeOutOfRange")
      }
    }
  | Engine.AcceptedTransaction(_)
  | Engine.WarningWithTransaction(_) =>
    assertTrue(false, "validation errors must not produce a transaction")
  | _ => assertTrue(false, "expected RejectedRow")
  }
}

let testNormalDuplicateSkipped = () => {
  let decision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: duplicate(Engine.CanonicalFinalized(Engine.CanonicalTransactionId("canonical-1"))),
    },
    normal,
    config,
  )

  switch decision {
  | Engine.SkippedDuplicate({reason}) => equal(reason, Engine.DuplicateInNormalMode)
  | _ => assertTrue(false, "expected duplicate skip in normal mode")
  }
}

let testRetryDuplicatePolicy = () => {
  let retryableDecision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: duplicate(Engine.RetryableFailure(Engine.UploadAttemptId("attempt-1"))),
    },
    retry,
    config,
  )

  let nonRetryableDecision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: duplicate(Engine.NonRetryableFailure(Engine.UploadAttemptId("attempt-2"))),
    },
    retry,
    config,
  )

  switch retryableDecision {
  | Engine.AcceptedTransaction(_) => ()
  | _ => assertTrue(false, "retry mode should accept explicitly retryable duplicate")
  }

  switch nonRetryableDecision {
  | Engine.SkippedDuplicate({reason}) => equal(reason, Engine.DuplicateNotRetryable)
  | _ => assertTrue(false, "retry mode should skip non-retryable duplicate")
  }
}

let testRecoveryDuplicatePolicy = () => {
  let recoverableDecision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: duplicate(
        Engine.FailedBeforeCanonicalFinalization(Engine.ProcessingFailureId("failure-1")),
      ),
    },
    recovery,
    config,
  )

  let finalizedDecision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: duplicate(
        Engine.FailedAfterCanonicalFinalization(Engine.ProcessingFailureId("failure-2")),
      ),
    },
    recovery,
    config,
  )

  switch recoverableDecision {
  | Engine.AcceptedTransaction(_) => ()
  | _ => assertTrue(false, "recovery should accept pre-finalization failure")
  }

  switch finalizedDecision {
  | Engine.SkippedDuplicate({reason}) => equal(reason, Engine.DuplicateNotRecoverable)
  | _ => assertTrue(false, "recovery should skip post-finalization failure")
  }
}

let testWarningsDoNotBlockTransaction = () => {
  let optionalConfig = {...config, odometerPolicy: Engine.OdometerOptional}
  let decision = Engine.classifyRow(
    {
      row: {
        ...baseRow,
        fuelVolumeLitres: 300.0,
        totalCostMinorUnits: 120000,
        odometer: Engine.OdometerNotProvided,
      },
      vehicleLookup: foundVehicle,
      duplicateCheck: noDuplicate,
    },
    normal,
    optionalConfig,
  )

  switch decision {
  | Engine.WarningWithTransaction({transaction, warnings}) =>
    equal(transaction.externalTransactionId, Engine.ExternalTransactionId("external-1"))
    equal(warningCount(warnings), 3)
    switch warnings.first {
    | Engine.FuelVolumeAboveWarningLimit(_) => ()
    | _ => assertTrue(false, "expected fuel volume warning first")
    }
  | _ => assertTrue(false, "expected warning with transaction")
  }
}

let testFatalDependencyError = () => {
  let decision = Engine.classifyRow(
    {
      row: baseRow,
      vehicleLookup: foundVehicle,
      duplicateCheck: Engine.DuplicateCheckFailed(
        Engine.DuplicateCheckUnavailable(Engine.ExternalTransactionId("external-1")),
      ),
    },
    normal,
    config,
  )

  switch decision {
  | Engine.FatalProcessingError({error: Engine.DuplicateCheckFatal(_)}) => ()
  | _ => assertTrue(false, "expected duplicate fatal error")
  }
}

let testConfigFatalError = () => {
  let invalidConfig = {
    ...config,
    fuelVolumeLitres: {minimumLitresInclusive: 10.0, maximumLitresInclusive: 1.0},
  }

  let decision = Engine.classifyRow(
    {row: baseRow, vehicleLookup: foundVehicle, duplicateCheck: noDuplicate},
    normal,
    invalidConfig,
  )

  switch decision {
  | Engine.FatalProcessingError({error: Engine.InvalidValidationConfig(problems)}) =>
    equal(1 + listCount(problems.rest), 1)
  | _ => assertTrue(false, "expected config fatal error")
  }
}

let testBatchSummary = () => {
  let optionalConfig = {...config, odometerPolicy: Engine.OdometerOptional}
  let decision = Engine.classifyBatch({
    mode: normal,
    config: optionalConfig,
    rows: list{
      {
        row: {...baseRow, rowId: Engine.RowId("row-accepted")},
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      {
        row: {...baseRow, rowId: Engine.RowId("row-warning"), odometer: Engine.OdometerNotProvided},
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      {
        row: {...baseRow, rowId: Engine.RowId("row-rejected"), fuelVolumeLitres: 0.0},
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      {
        row: {...baseRow, rowId: Engine.RowId("row-skipped")},
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate(Engine.CanonicalFinalized(Engine.CanonicalTransactionId("canonical-1"))),
      },
    },
  })

  switch decision {
  | Engine.BatchUploadable({summary, uploadableTransactions}) =>
    deepEqual(
      summary,
      {
        totalRows: 4,
        acceptedRows: 1,
        warningRows: 1,
        skippedDuplicateRows: 1,
        rejectedRows: 1,
        fatalRows: 0,
        transactionRows: 2,
      },
    )
    equal(listCount(uploadableTransactions), 2)
  | _ => assertTrue(false, "expected uploadable batch")
  }
}

let testFatalBlocksBatch = () => {
  let decision = Engine.classifyBatch({
    mode: normal,
    config,
    rows: list{
      {
        row: {...baseRow, rowId: Engine.RowId("row-accepted")},
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      {
        row: {...baseRow, rowId: Engine.RowId("row-fatal")},
        vehicleLookup: Engine.VehicleLookupFailed(
          Engine.VehicleLookupUnavailable(Engine.VehicleKey("fleet-1")),
        ),
        duplicateCheck: noDuplicate,
      },
    },
  })

  switch decision {
  | Engine.BatchBlockedByFatalError({summary, fatalErrors, uploadableTransactions}) =>
    equal(summary.totalRows, 2)
    equal(summary.acceptedRows, 1)
    equal(summary.fatalRows, 1)
    equal(summary.transactionRows, 1)
    equal(listCount(uploadableTransactions), 0)
    switch fatalErrors.first {
    | Engine.VehicleLookupFatal(_) => ()
    | _ => assertTrue(false, "expected vehicle lookup fatal")
    }
  | _ => assertTrue(false, "expected blocked batch")
  }
}

let run = (name, test) => {
  test()
  Console.log("ok - " ++ name)
}

run("accepts a valid non-duplicate row", testAcceptedValidRow)
run("rejects validation errors without transaction", testValidationRejectsWithoutTransaction)
run("skips normal duplicates", testNormalDuplicateSkipped)
run("handles retry duplicate policy", testRetryDuplicatePolicy)
run("handles recovery duplicate policy", testRecoveryDuplicatePolicy)
run("keeps warnings non-blocking", testWarningsDoNotBlockTransaction)
run("returns fatal dependency errors", testFatalDependencyError)
run("returns fatal config errors", testConfigFatalError)
run("derives batch summary counts", testBatchSummary)
run("fatal errors block batch", testFatalBlocksBatch)

Console.log("10 tests passed")
