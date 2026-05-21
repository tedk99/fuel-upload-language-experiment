//> using scala "3.8.3"

package fuel.engine

final case class NonEmptyList[+A](head: A, tail: List[A] = Nil):
  def toList: List[A] = head :: tail
  def map[B](f: A => B): NonEmptyList[B] = NonEmptyList(f(head), tail.map(f))

object NonEmptyList:
  def of[A](head: A, tail: A*): NonEmptyList[A] = NonEmptyList(head, tail.toList)

final case class RowId(value: String) extends AnyVal
final case class ExternalTransactionId(value: String) extends AnyVal
final case class VehicleKey(value: String) extends AnyVal
final case class VehicleId(value: String) extends AnyVal
final case class CanonicalTransactionId(value: String) extends AnyVal
final case class UploadAttemptId(value: String) extends AnyVal
final case class ProcessingFailureId(value: String) extends AnyVal
final case class IsoInstantText(value: String) extends AnyVal
final case class MerchantName(value: String) extends AnyVal

enum UploadMode:
  case Normal, Retry, Recovery

enum FuelKind:
  case Diesel, Gasoline, Electric, Hydrogen

enum OdometerReading:
  case Provided(kilometres: BigDecimal)
  case NotProvided

final case class ParsedFuelRow(
    rowId: RowId,
    sourceLineNumber: Int,
    externalTransactionId: ExternalTransactionId,
    vehicleKey: VehicleKey,
    purchasedAt: IsoInstantText,
    merchantName: MerchantName,
    fuelKind: FuelKind,
    fuelVolumeLitres: BigDecimal,
    totalCostMinorUnits: Long,
    odometer: OdometerReading
)

enum VehicleInactiveReason:
  case Retired, Suspended

enum VehicleStatus:
  case Active
  case Inactive(reason: VehicleInactiveReason)

final case class Vehicle(
    vehicleId: VehicleId,
    vehicleKey: VehicleKey,
    status: VehicleStatus,
    minimumAllowedOdometer: OdometerReading
)

enum VehicleLookupFailure:
  case VehicleLookupUnavailable(lookupName: VehicleKey)

enum VehicleLookupResult:
  case VehicleFound(vehicle: Vehicle)
  case VehicleNotFound(vehicleKey: VehicleKey)
  case VehicleLookupFailed(error: VehicleLookupFailure)

enum PreviousDuplicateState:
  case CanonicalFinalized(canonicalTransactionId: CanonicalTransactionId)
  case RetryableFailure(attemptId: UploadAttemptId)
  case NonRetryableFailure(attemptId: UploadAttemptId)
  case FailedBeforeCanonicalFinalization(failureId: ProcessingFailureId)
  case FailedAfterCanonicalFinalization(failureId: ProcessingFailureId)

final case class DuplicateRecord(
    externalTransactionId: ExternalTransactionId,
    previousState: PreviousDuplicateState
)

enum DuplicateCheckFailure:
  case DuplicateCheckUnavailable(externalTransactionId: ExternalTransactionId)

enum DuplicateCheckResult:
  case NoDuplicate
  case DuplicateFound(duplicate: DuplicateRecord)
  case DuplicateCheckFailed(error: DuplicateCheckFailure)

enum OdometerPolicy:
  case OdometerRequired, OdometerOptional

final case class NumericRange[A](minimumInclusive: A, maximumInclusive: A)

enum WarningLimit[+A]:
  case Enabled(thresholdExclusive: A)
  case Disabled

final case class WarningLimits(
    fuelVolumeLitres: WarningLimit[BigDecimal],
    totalCostMinorUnits: WarningLimit[Long]
)

final case class ValidationConfig(
    allowedFuelKinds: NonEmptyList[FuelKind],
    fuelVolumeLitres: NumericRange[BigDecimal],
    totalCostMinorUnits: NumericRange[Long],
    odometerPolicy: OdometerPolicy,
    warningLimits: WarningLimits
)

enum ValidationConfigProblem:
  case FuelVolumeRangeInvalid(range: NumericRange[BigDecimal])
  case TotalCostRangeInvalid(range: NumericRange[Long])

enum ValidationError:
  case FuelKindNotAllowed(fuelKind: FuelKind, allowedFuelKinds: NonEmptyList[FuelKind])
  case FuelVolumeOutOfRange(actualLitres: BigDecimal, allowedRange: NumericRange[BigDecimal])
  case TotalCostOutOfRange(actualMinorUnits: Long, allowedRange: NumericRange[Long])
  case VehicleMissing(vehicleKey: VehicleKey)
  case VehicleInactive(vehicleId: VehicleId, reason: VehicleInactiveReason)
  case OdometerMissing(policy: OdometerPolicy.OdometerRequired.type)
  case OdometerBelowVehicleMinimum(actualKilometres: BigDecimal, minimumKilometres: BigDecimal)

enum RejectionReason:
  case ValidationFailed(errors: NonEmptyList[ValidationError])

enum UploadWarning:
  case FuelVolumeAboveWarningLimit(actualLitres: BigDecimal, thresholdExclusive: BigDecimal)
  case TotalCostAboveWarningLimit(actualMinorUnits: Long, thresholdExclusive: Long)
  case OdometerMissingButAllowed

enum FatalProcessingError:
  case InvalidValidationConfig(problems: NonEmptyList[ValidationConfigProblem])
  case VehicleLookupFatal(error: VehicleLookupFailure)
  case DuplicateCheckFatal(error: DuplicateCheckFailure)

final case class FuelTransaction(
    externalTransactionId: ExternalTransactionId,
    vehicleId: VehicleId,
    purchasedAt: IsoInstantText,
    merchantName: MerchantName,
    fuelKind: FuelKind,
    fuelVolumeLitres: BigDecimal,
    totalCostMinorUnits: Long,
    odometer: OdometerReading
)

enum DuplicateSkipReason:
  case DuplicateInNormalMode
  case DuplicateNotRetryable
  case DuplicateNotRecoverable

enum RowDecision:
  case AcceptedTransaction(rowId: RowId, transaction: FuelTransaction)
  case SkippedDuplicate(
      rowId: RowId,
      mode: UploadMode,
      duplicate: DuplicateRecord,
      reason: DuplicateSkipReason
  )
  case RejectedRow(rowId: RowId, reasons: NonEmptyList[RejectionReason])
  case WarningWithTransaction(
      rowId: RowId,
      transaction: FuelTransaction,
      warnings: NonEmptyList[UploadWarning]
  )
  case FatalProcessingError(rowId: RowId, error: fuel.engine.FatalProcessingError)

final case class RowClassificationInput(
    row: ParsedFuelRow,
    vehicleLookup: VehicleLookupResult,
    duplicateCheck: DuplicateCheckResult
)

final case class BatchClassificationInput(
    mode: UploadMode,
    config: ValidationConfig,
    rows: List[RowClassificationInput]
)

final case class BatchSummary(
    totalRows: Int,
    acceptedRows: Int,
    warningRows: Int,
    skippedDuplicateRows: Int,
    rejectedRows: Int,
    fatalRows: Int,
    transactionRows: Int
)

enum BatchDecision:
  case BatchUploadable(
      rowDecisions: List[RowDecision],
      summary: BatchSummary,
      uploadableTransactions: List[FuelTransaction]
  )
  case BatchBlockedByFatalError(
      rowDecisions: List[RowDecision],
      summary: BatchSummary,
      fatalErrors: NonEmptyList[FatalProcessingError],
      uploadableTransactions: List[FuelTransaction]
  )

object FuelUploadDecisionEngine:
  def classifyRow(
      input: RowClassificationInput,
      mode: UploadMode,
      config: ValidationConfig
  ): RowDecision =
    validateConfig(config) match
      case Some(problems) =>
        RowDecision.FatalProcessingError(
          input.row.rowId,
          FatalProcessingError.InvalidValidationConfig(problems)
        )
      case None =>
        input.vehicleLookup match
          case VehicleLookupResult.VehicleLookupFailed(error) =>
            RowDecision.FatalProcessingError(
              input.row.rowId,
              FatalProcessingError.VehicleLookupFatal(error)
            )
          case vehicleLookup =>
            input.duplicateCheck match
              case DuplicateCheckResult.DuplicateCheckFailed(error) =>
                RowDecision.FatalProcessingError(
                  input.row.rowId,
                  FatalProcessingError.DuplicateCheckFatal(error)
                )
              case duplicateCheck =>
                val validationErrors = validateRow(input.row, vehicleLookup, config)

                validationErrors match
                  case Some(errors) =>
                    RowDecision.RejectedRow(
                      input.row.rowId,
                      NonEmptyList.of(RejectionReason.ValidationFailed(errors))
                    )
                  case None =>
                    classifyDuplicate(duplicateCheck, mode) match
                      case DuplicatePolicyDecision.DuplicateSkipped(duplicate, reason) =>
                        RowDecision.SkippedDuplicate(input.row.rowId, mode, duplicate, reason)
                      case DuplicatePolicyDecision.DuplicateAllowed =>
                        vehicleLookup match
                          case VehicleLookupResult.VehicleFound(vehicle) =>
                            val transaction = buildTransaction(input.row, vehicle)
                            collectWarnings(input.row, config) match
                              case Some(warnings) =>
                                RowDecision.WarningWithTransaction(
                                  input.row.rowId,
                                  transaction,
                                  warnings
                                )
                              case None =>
                                RowDecision.AcceptedTransaction(input.row.rowId, transaction)
                          case VehicleLookupResult.VehicleNotFound(vehicleKey) =>
                            // Defensive fallback; normal validation catches this branch.
                            RowDecision.RejectedRow(
                              input.row.rowId,
                              NonEmptyList.of(
                                RejectionReason.ValidationFailed(
                                  NonEmptyList.of(ValidationError.VehicleMissing(vehicleKey))
                                )
                              )
                            )
                          case VehicleLookupResult.VehicleLookupFailed(error) =>
                            RowDecision.FatalProcessingError(
                              input.row.rowId,
                              FatalProcessingError.VehicleLookupFatal(error)
                            )

  def classifyBatch(input: BatchClassificationInput): BatchDecision =
    val rowDecisions = input.rows.map(classifyRow(_, input.mode, input.config))
    val summary = summarize(rowDecisions)
    val fatalErrors = rowDecisions.collect:
      case RowDecision.FatalProcessingError(_, error) => error

    NonEmptyListFromList(fatalErrors) match
      case Some(errors) =>
        BatchDecision.BatchBlockedByFatalError(rowDecisions, summary, errors, Nil)
      case None =>
        BatchDecision.BatchUploadable(rowDecisions, summary, collectTransactions(rowDecisions))

  def summarize(rowDecisions: List[RowDecision]): BatchSummary =
    rowDecisions.foldLeft(BatchSummary(rowDecisions.size, 0, 0, 0, 0, 0, 0)):
      case (summary, RowDecision.AcceptedTransaction(_, _)) =>
        summary.copy(
          acceptedRows = summary.acceptedRows + 1,
          transactionRows = summary.transactionRows + 1
        )
      case (summary, RowDecision.WarningWithTransaction(_, _, _)) =>
        summary.copy(
          warningRows = summary.warningRows + 1,
          transactionRows = summary.transactionRows + 1
        )
      case (summary, RowDecision.SkippedDuplicate(_, _, _, _)) =>
        summary.copy(skippedDuplicateRows = summary.skippedDuplicateRows + 1)
      case (summary, RowDecision.RejectedRow(_, _)) =>
        summary.copy(rejectedRows = summary.rejectedRows + 1)
      case (summary, RowDecision.FatalProcessingError(_, _)) =>
        summary.copy(fatalRows = summary.fatalRows + 1)

  private enum DuplicatePolicyDecision:
    case DuplicateAllowed
    case DuplicateSkipped(duplicate: DuplicateRecord, reason: DuplicateSkipReason)

  private def validateConfig(config: ValidationConfig): Option[NonEmptyList[ValidationConfigProblem]] =
    val problems =
      List(
        Option.when(
          config.fuelVolumeLitres.minimumInclusive > config.fuelVolumeLitres.maximumInclusive
        )(ValidationConfigProblem.FuelVolumeRangeInvalid(config.fuelVolumeLitres)),
        Option.when(
          config.totalCostMinorUnits.minimumInclusive > config.totalCostMinorUnits.maximumInclusive
        )(ValidationConfigProblem.TotalCostRangeInvalid(config.totalCostMinorUnits))
      ).flatten

    NonEmptyListFromList(problems)

  private def validateRow(
      row: ParsedFuelRow,
      vehicleLookup: VehicleLookupResult.VehicleFound | VehicleLookupResult.VehicleNotFound,
      config: ValidationConfig
  ): Option[NonEmptyList[ValidationError]] =
    val baseErrors =
      List(
        Option.when(!config.allowedFuelKinds.toList.contains(row.fuelKind))(
          ValidationError.FuelKindNotAllowed(row.fuelKind, config.allowedFuelKinds)
        ),
        Option.when(!isWithin(row.fuelVolumeLitres, config.fuelVolumeLitres))(
          ValidationError.FuelVolumeOutOfRange(row.fuelVolumeLitres, config.fuelVolumeLitres)
        ),
        Option.when(!isWithin(row.totalCostMinorUnits, config.totalCostMinorUnits))(
          ValidationError.TotalCostOutOfRange(row.totalCostMinorUnits, config.totalCostMinorUnits)
        )
      ).flatten

    val vehicleErrors =
      vehicleLookup match
        case VehicleLookupResult.VehicleNotFound(vehicleKey) =>
          List(ValidationError.VehicleMissing(vehicleKey))
        case VehicleLookupResult.VehicleFound(vehicle) =>
          validateVehicle(row, vehicle, config)

    NonEmptyListFromList(baseErrors ++ vehicleErrors)

  private def validateVehicle(
      row: ParsedFuelRow,
      vehicle: Vehicle,
      config: ValidationConfig
  ): List[ValidationError] =
    val statusError =
      vehicle.status match
        case VehicleStatus.Active => None
        case VehicleStatus.Inactive(reason) =>
          Some(ValidationError.VehicleInactive(vehicle.vehicleId, reason))

    val missingOdometerError =
      (config.odometerPolicy, row.odometer) match
        case (OdometerPolicy.OdometerRequired, OdometerReading.NotProvided) =>
          Some(ValidationError.OdometerMissing(OdometerPolicy.OdometerRequired))
        case (OdometerPolicy.OdometerRequired, OdometerReading.Provided(_)) |
            (OdometerPolicy.OdometerOptional, _) =>
          None

    val belowMinimumOdometerError =
      (row.odometer, vehicle.minimumAllowedOdometer) match
        case (OdometerReading.Provided(actual), OdometerReading.Provided(minimum))
            if actual < minimum =>
          Some(ValidationError.OdometerBelowVehicleMinimum(actual, minimum))
        case (OdometerReading.Provided(_), OdometerReading.Provided(_)) |
            (OdometerReading.Provided(_), OdometerReading.NotProvided) |
            (OdometerReading.NotProvided, _) =>
          None

    List(statusError, missingOdometerError, belowMinimumOdometerError).flatten

  private def classifyDuplicate(
      check: DuplicateCheckResult.NoDuplicate.type | DuplicateCheckResult.DuplicateFound,
      mode: UploadMode
  ): DuplicatePolicyDecision =
    check match
      case DuplicateCheckResult.NoDuplicate =>
        DuplicatePolicyDecision.DuplicateAllowed
      case DuplicateCheckResult.DuplicateFound(duplicate) =>
        classifyDuplicateFound(duplicate, mode)

  private def classifyDuplicateFound(
      duplicate: DuplicateRecord,
      mode: UploadMode
  ): DuplicatePolicyDecision =
    mode match
      case UploadMode.Normal =>
        DuplicatePolicyDecision.DuplicateSkipped(
          duplicate,
          DuplicateSkipReason.DuplicateInNormalMode
        )
      case UploadMode.Retry =>
        duplicate.previousState match
          case PreviousDuplicateState.RetryableFailure(_) =>
            DuplicatePolicyDecision.DuplicateAllowed
          case PreviousDuplicateState.CanonicalFinalized(_) |
              PreviousDuplicateState.NonRetryableFailure(_) |
              PreviousDuplicateState.FailedBeforeCanonicalFinalization(_) |
              PreviousDuplicateState.FailedAfterCanonicalFinalization(_) =>
            DuplicatePolicyDecision.DuplicateSkipped(
              duplicate,
              DuplicateSkipReason.DuplicateNotRetryable
            )
      case UploadMode.Recovery =>
        duplicate.previousState match
          case PreviousDuplicateState.FailedBeforeCanonicalFinalization(_) =>
            DuplicatePolicyDecision.DuplicateAllowed
          case PreviousDuplicateState.CanonicalFinalized(_) |
              PreviousDuplicateState.RetryableFailure(_) |
              PreviousDuplicateState.NonRetryableFailure(_) |
              PreviousDuplicateState.FailedAfterCanonicalFinalization(_) =>
            DuplicatePolicyDecision.DuplicateSkipped(
              duplicate,
              DuplicateSkipReason.DuplicateNotRecoverable
            )

  private def buildTransaction(row: ParsedFuelRow, vehicle: Vehicle): FuelTransaction =
    FuelTransaction(
      externalTransactionId = row.externalTransactionId,
      vehicleId = vehicle.vehicleId,
      purchasedAt = row.purchasedAt,
      merchantName = row.merchantName,
      fuelKind = row.fuelKind,
      fuelVolumeLitres = row.fuelVolumeLitres,
      totalCostMinorUnits = row.totalCostMinorUnits,
      odometer = row.odometer
    )

  private def collectWarnings(
      row: ParsedFuelRow,
      config: ValidationConfig
  ): Option[NonEmptyList[UploadWarning]] =
    val warnings =
      List(
        config.warningLimits.fuelVolumeLitres match
          case WarningLimit.Enabled(threshold) if row.fuelVolumeLitres > threshold =>
            Some(UploadWarning.FuelVolumeAboveWarningLimit(row.fuelVolumeLitres, threshold))
          case WarningLimit.Enabled(_) | WarningLimit.Disabled =>
            None,
        config.warningLimits.totalCostMinorUnits match
          case WarningLimit.Enabled(threshold) if row.totalCostMinorUnits > threshold =>
            Some(UploadWarning.TotalCostAboveWarningLimit(row.totalCostMinorUnits, threshold))
          case WarningLimit.Enabled(_) | WarningLimit.Disabled =>
            None,
        (config.odometerPolicy, row.odometer) match
          case (OdometerPolicy.OdometerOptional, OdometerReading.NotProvided) =>
            Some(UploadWarning.OdometerMissingButAllowed)
          case (OdometerPolicy.OdometerRequired, _) |
              (OdometerPolicy.OdometerOptional, OdometerReading.Provided(_)) =>
            None
      ).flatten

    NonEmptyListFromList(warnings)

  private def collectTransactions(rowDecisions: List[RowDecision]): List[FuelTransaction] =
    rowDecisions.collect:
      case RowDecision.AcceptedTransaction(_, transaction) => transaction
      case RowDecision.WarningWithTransaction(_, transaction, _) => transaction

  private def isWithin[A](value: A, range: NumericRange[A])(using ordering: Ordering[A]): Boolean =
    import ordering.mkOrderingOps
    value >= range.minimumInclusive && value <= range.maximumInclusive

  private def NonEmptyListFromList[A](items: List[A]): Option[NonEmptyList[A]] =
    items match
      case head :: tail => Some(NonEmptyList(head, tail))
      case Nil => None
