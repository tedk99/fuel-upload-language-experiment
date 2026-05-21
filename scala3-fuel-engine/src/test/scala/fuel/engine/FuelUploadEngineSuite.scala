//> using scala "3.8.3"
//> using dep "org.scalameta::munit:1.2.0"

package fuel.engine

import fuel.engine.FuelUploadDecisionEngine.*

final class FuelUploadEngineSuite extends munit.FunSuite:
  private val diesel = FuelKind.Diesel
  private val gasoline = FuelKind.Gasoline

  private val config =
    ValidationConfig(
      allowedFuelKinds = NonEmptyList.of(diesel, gasoline),
      fuelVolumeLitres = NumericRange(BigDecimal(1), BigDecimal(500)),
      totalCostMinorUnits = NumericRange(1L, 200_000L),
      odometerPolicy = OdometerPolicy.OdometerRequired,
      warningLimits = WarningLimits(
        fuelVolumeLitres = WarningLimit.Enabled(BigDecimal(250)),
        totalCostMinorUnits = WarningLimit.Enabled(100_000L)
      )
    )

  private val vehicle =
    Vehicle(
      vehicleId = VehicleId("vehicle-1"),
      vehicleKey = VehicleKey("fleet-1"),
      status = VehicleStatus.Active,
      minimumAllowedOdometer = OdometerReading.Provided(BigDecimal(10_000))
    )

  private val foundVehicle = VehicleLookupResult.VehicleFound(vehicle)
  private val noDuplicate = DuplicateCheckResult.NoDuplicate

  test("classifyRow accepts a valid non-duplicate row without warnings"):
    val decision =
      classifyRow(RowClassificationInput(row(), foundVehicle, noDuplicate), UploadMode.Normal, config)

    decision match
      case RowDecision.AcceptedTransaction(rowId, transaction) =>
        assertEquals(rowId, RowId("row-1"))
        assertEquals(transaction.vehicleId, VehicleId("vehicle-1"))
        assertEquals(transaction.externalTransactionId, ExternalTransactionId("external-1"))
      case other =>
        fail(s"expected accepted transaction, got $other")

  test("classifyRow rejects validation errors and never returns a transaction"):
    val decision =
      classifyRow(
        RowClassificationInput(row(_.copy(fuelVolumeLitres = BigDecimal(0))), foundVehicle, noDuplicate),
        UploadMode.Normal,
        config
      )

    decision match
      case RowDecision.RejectedRow(_, reasons) =>
        assertEquals(reasons.toList.size, 1)
        reasons.head match
          case RejectionReason.ValidationFailed(errors) =>
            assert(errors.toList.exists:
              case ValidationError.FuelVolumeOutOfRange(_, _) => true
              case _ => false
            )
      case RowDecision.AcceptedTransaction(_, _) | RowDecision.WarningWithTransaction(_, _, _) =>
        fail(s"validation errors must not produce a transaction: $decision")
      case other =>
        fail(s"expected rejected row, got $other")

  test("classifyRow reports missing vehicles as typed validation rejections"):
    val missing = VehicleLookupResult.VehicleNotFound(VehicleKey("missing"))
    val decision =
      classifyRow(RowClassificationInput(row(), missing, noDuplicate), UploadMode.Normal, config)

    decision match
      case RowDecision.RejectedRow(_, NonEmptyList(RejectionReason.ValidationFailed(errors), Nil)) =>
        assertEquals(errors.head, ValidationError.VehicleMissing(VehicleKey("missing")))
      case other =>
        fail(s"expected typed vehicle-missing rejection, got $other")

  test("classifyRow returns fatal errors for unavailable processing dependencies"):
    val decision =
      classifyRow(
        RowClassificationInput(
          row(),
          foundVehicle,
          DuplicateCheckResult.DuplicateCheckFailed(
            DuplicateCheckFailure.DuplicateCheckUnavailable(ExternalTransactionId("external-1"))
          )
        ),
        UploadMode.Normal,
        config
      )

    assertEquals(
      decision,
      RowDecision.FatalProcessingError(
        RowId("row-1"),
        FatalProcessingError.DuplicateCheckFatal(
          DuplicateCheckFailure.DuplicateCheckUnavailable(ExternalTransactionId("external-1"))
        )
      )
    )

  test("classifyRow treats invalid validation config as a fatal processing error"):
    val invalidConfig =
      config.copy(fuelVolumeLitres = NumericRange(BigDecimal(500), BigDecimal(1)))

    val decision =
      classifyRow(RowClassificationInput(row(), foundVehicle, noDuplicate), UploadMode.Normal, invalidConfig)

    decision match
      case RowDecision.FatalProcessingError(_, FatalProcessingError.InvalidValidationConfig(problems)) =>
        assertEquals(
          problems.head,
          ValidationConfigProblem.FuelVolumeRangeInvalid(NumericRange(BigDecimal(500), BigDecimal(1)))
        )
      case other =>
        fail(s"expected invalid-config fatal error, got $other")

  test("duplicate behavior is mode-specific for all previous states"):
    val states = List(
      PreviousDuplicateState.CanonicalFinalized(CanonicalTransactionId("canonical-1")),
      PreviousDuplicateState.RetryableFailure(UploadAttemptId("attempt-retryable")),
      PreviousDuplicateState.NonRetryableFailure(UploadAttemptId("attempt-final")),
      PreviousDuplicateState.FailedBeforeCanonicalFinalization(ProcessingFailureId("failure-before")),
      PreviousDuplicateState.FailedAfterCanonicalFinalization(ProcessingFailureId("failure-after"))
    )

    states.foreach: state =>
      assertSkipped(
        classifyDuplicateState(UploadMode.Normal, state),
        DuplicateSkipReason.DuplicateInNormalMode
      )

    states.foreach: state =>
      val decision = classifyDuplicateState(UploadMode.Retry, state)
      state match
        case PreviousDuplicateState.RetryableFailure(_) =>
          assertTransactionDecision(decision)
        case PreviousDuplicateState.CanonicalFinalized(_) |
            PreviousDuplicateState.NonRetryableFailure(_) |
            PreviousDuplicateState.FailedBeforeCanonicalFinalization(_) |
            PreviousDuplicateState.FailedAfterCanonicalFinalization(_) =>
          assertSkipped(decision, DuplicateSkipReason.DuplicateNotRetryable)

    states.foreach: state =>
      val decision = classifyDuplicateState(UploadMode.Recovery, state)
      state match
        case PreviousDuplicateState.FailedBeforeCanonicalFinalization(_) =>
          assertTransactionDecision(decision)
        case PreviousDuplicateState.CanonicalFinalized(_) |
            PreviousDuplicateState.RetryableFailure(_) |
            PreviousDuplicateState.NonRetryableFailure(_) |
            PreviousDuplicateState.FailedAfterCanonicalFinalization(_) =>
          assertSkipped(decision, DuplicateSkipReason.DuplicateNotRecoverable)

  test("retryable duplicates with validation errors are rejected, not accepted"):
    val decision =
      classifyRow(
        RowClassificationInput(
          row(_.copy(fuelVolumeLitres = BigDecimal(0))),
          foundVehicle,
          duplicate(PreviousDuplicateState.RetryableFailure(UploadAttemptId("attempt-1")))
        ),
        UploadMode.Retry,
        config
      )

    assert(decision.isInstanceOf[RowDecision.RejectedRow])

  test("warnings travel with transactions and do not block row acceptance"):
    val optionalConfig = config.copy(odometerPolicy = OdometerPolicy.OdometerOptional)
    val decision =
      classifyRow(
        RowClassificationInput(
          row(
            _.copy(
              fuelVolumeLitres = BigDecimal(300),
              totalCostMinorUnits = 120_000L,
              odometer = OdometerReading.NotProvided
            )
          ),
          foundVehicle,
          noDuplicate
        ),
        UploadMode.Normal,
        optionalConfig
      )

    decision match
      case RowDecision.WarningWithTransaction(_, transaction, warnings) =>
        assertEquals(transaction.externalTransactionId, ExternalTransactionId("external-1"))
        assertEquals(
          warnings.toList,
          List(
            UploadWarning.FuelVolumeAboveWarningLimit(BigDecimal(300), BigDecimal(250)),
            UploadWarning.TotalCostAboveWarningLimit(120_000L, 100_000L),
            UploadWarning.OdometerMissingButAllowed
          )
        )
      case other =>
        fail(s"expected warning with transaction, got $other")

  test("classifyBatch derives summary counts from row decisions"):
    val optionalConfig = config.copy(odometerPolicy = OdometerPolicy.OdometerOptional)
    val batch =
      classifyBatch(
        BatchClassificationInput(
          mode = UploadMode.Normal,
          config = optionalConfig,
          rows = List(
            RowClassificationInput(row(_.copy(rowId = RowId("row-accepted"))), foundVehicle, noDuplicate),
            RowClassificationInput(
              row(_.copy(rowId = RowId("row-warning"), odometer = OdometerReading.NotProvided)),
              foundVehicle,
              noDuplicate
            ),
            RowClassificationInput(
              row(_.copy(rowId = RowId("row-rejected"), fuelVolumeLitres = BigDecimal(0))),
              foundVehicle,
              noDuplicate
            ),
            RowClassificationInput(
              row(_.copy(rowId = RowId("row-skipped"))),
              foundVehicle,
              duplicate(PreviousDuplicateState.CanonicalFinalized(CanonicalTransactionId("canonical-1")))
            )
          )
        )
      )

    batch match
      case BatchDecision.BatchUploadable(_, summary, uploadableTransactions) =>
        assertEquals(
          summary,
          BatchSummary(
            totalRows = 4,
            acceptedRows = 1,
            warningRows = 1,
            skippedDuplicateRows = 1,
            rejectedRows = 1,
            fatalRows = 0,
            transactionRows = 2
          )
        )
        assertEquals(uploadableTransactions.size, 2)
      case other =>
        fail(s"expected uploadable batch, got $other")

  test("classifyBatch blocks the entire batch when any row has a fatal processing error"):
    val batch =
      classifyBatch(
        BatchClassificationInput(
          mode = UploadMode.Normal,
          config = config,
          rows = List(
            RowClassificationInput(row(_.copy(rowId = RowId("row-accepted"))), foundVehicle, noDuplicate),
            RowClassificationInput(
              row(_.copy(rowId = RowId("row-fatal"))),
              VehicleLookupResult.VehicleLookupFailed(
                VehicleLookupFailure.VehicleLookupUnavailable(VehicleKey("fleet-1"))
              ),
              noDuplicate
            )
          )
        )
      )

    batch match
      case BatchDecision.BatchBlockedByFatalError(_, summary, fatalErrors, uploadableTransactions) =>
        assertEquals(summary.totalRows, 2)
        assertEquals(summary.acceptedRows, 1)
        assertEquals(summary.fatalRows, 1)
        assertEquals(summary.transactionRows, 1)
        assertEquals(uploadableTransactions, Nil)
        assertEquals(
          fatalErrors.head,
          FatalProcessingError.VehicleLookupFatal(
            VehicleLookupFailure.VehicleLookupUnavailable(VehicleKey("fleet-1"))
          )
        )
      case other =>
        fail(s"expected fatal-blocked batch, got $other")

  test("classifyBatch is uploadable when accepted rows only have warnings"):
    val optionalConfig = config.copy(odometerPolicy = OdometerPolicy.OdometerOptional)
    val batch =
      classifyBatch(
        BatchClassificationInput(
          mode = UploadMode.Normal,
          config = optionalConfig,
          rows = List(
            RowClassificationInput(
              row(_.copy(rowId = RowId("row-warning"), odometer = OdometerReading.NotProvided)),
              foundVehicle,
              noDuplicate
            )
          )
        )
      )

    batch match
      case BatchDecision.BatchUploadable(_, summary, uploadableTransactions) =>
        assertEquals(summary.warningRows, 1)
        assertEquals(summary.transactionRows, 1)
        assertEquals(uploadableTransactions.size, 1)
      case other =>
        fail(s"warnings should not block batch upload, got $other")

  private def row(update: ParsedFuelRow => ParsedFuelRow = identity): ParsedFuelRow =
    update(
      ParsedFuelRow(
        rowId = RowId("row-1"),
        sourceLineNumber = 2,
        externalTransactionId = ExternalTransactionId("external-1"),
        vehicleKey = VehicleKey("fleet-1"),
        purchasedAt = IsoInstantText("2026-05-21T09:00:00Z"),
        merchantName = MerchantName("Depot A"),
        fuelKind = diesel,
        fuelVolumeLitres = BigDecimal(100),
        totalCostMinorUnits = 50_000L,
        odometer = OdometerReading.Provided(BigDecimal(12_000))
      )
    )

  private def duplicate(previousState: PreviousDuplicateState): DuplicateCheckResult =
    DuplicateCheckResult.DuplicateFound(
      DuplicateRecord(ExternalTransactionId("external-1"), previousState)
    )

  private def classifyDuplicateState(mode: UploadMode, state: PreviousDuplicateState): RowDecision =
    classifyRow(RowClassificationInput(row(), foundVehicle, duplicate(state)), mode, config)

  private def assertTransactionDecision(decision: RowDecision): Unit =
    decision match
      case RowDecision.AcceptedTransaction(_, _) | RowDecision.WarningWithTransaction(_, _, _) => ()
      case other => fail(s"expected transaction decision, got $other")

  private def assertSkipped(decision: RowDecision, reason: DuplicateSkipReason): Unit =
    decision match
      case RowDecision.SkippedDuplicate(_, _, _, actualReason) =>
        assertEquals(actualReason, reason)
      case other =>
        fail(s"expected skipped duplicate with $reason, got $other")
