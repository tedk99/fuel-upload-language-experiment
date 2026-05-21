module Fuel.Engine
  ( RowId(..)
  , VehicleKey(..)
  , VehicleId(..)
  , ExternalReference(..)
  , CanonicalTimestamp(..)
  , TransactionId(..)
  , ParsedFuelRow(..)
  , Vehicle(..)
  , VehicleStatus(..)
  , VehicleLookupResult(..)
  , DuplicateCheckResult(..)
  , PreviousAttempt(..)
  , RetryableFailure(..)
  , NonRetryableFailure(..)
  , ValidationConfig(..)
  , QuantityPolicy(..)
  , CostPolicy(..)
  , VehicleStatusPolicy(..)
  , VolumeWarningPolicy(..)
  , CostWarningPolicy(..)
  , ValidationError(..)
  , ValidationErrors(..)
  , Warning(..)
  , Warnings(..)
  , RejectionReason(..)
  , DuplicateSkipReason(..)
  , FatalError(..)
  , UploadMode(..)
  , Transaction(..)
  , RejectedRow(..)
  , SkippedDuplicate(..)
  , RowDecision(..)
  , BatchOutcome(..)
  , BatchSummary(..)
  , BatchDecision(..)
  , classifyRow
  , classifyBatch
  , defaultValidationConfig
  , warningsToArray
  , validationErrorsToArray
  ) where

import Prelude

import Data.Foldable (foldl)

newtype RowId = RowId Int

derive newtype instance eqRowId :: Eq RowId

newtype VehicleKey = VehicleKey String

derive newtype instance eqVehicleKey :: Eq VehicleKey

newtype VehicleId = VehicleId String

derive newtype instance eqVehicleId :: Eq VehicleId

newtype ExternalReference = ExternalReference String

derive newtype instance eqExternalReference :: Eq ExternalReference

newtype CanonicalTimestamp = CanonicalTimestamp String

derive newtype instance eqCanonicalTimestamp :: Eq CanonicalTimestamp

newtype TransactionId = TransactionId String

derive newtype instance eqTransactionId :: Eq TransactionId

newtype ParsedFuelRow = ParsedFuelRow
  { rowId :: RowId
  , vehicleKey :: VehicleKey
  , externalReference :: ExternalReference
  , occurredAt :: CanonicalTimestamp
  , liters :: Number
  , totalCost :: Number
  }

derive newtype instance eqParsedFuelRow :: Eq ParsedFuelRow

newtype Vehicle = Vehicle
  { vehicleId :: VehicleId
  , vehicleKey :: VehicleKey
  , status :: VehicleStatus
  }

derive newtype instance eqVehicle :: Eq Vehicle

data VehicleStatus
  = ActiveVehicle
  | InactiveVehicle

derive instance eqVehicleStatus :: Eq VehicleStatus

data VehicleLookupResult
  = VehicleFound Vehicle
  | VehicleNotFound VehicleKey
  | VehicleLookupFatal FatalError

derive instance eqVehicleLookupResult :: Eq VehicleLookupResult

data UploadMode
  = Normal
  | Retry
  | Recovery

derive instance eqUploadMode :: Eq UploadMode

data DuplicateCheckResult
  = UniqueTransaction
  | DuplicateTransaction PreviousAttempt
  | DuplicateCheckFatal FatalError

derive instance eqDuplicateCheckResult :: Eq DuplicateCheckResult

data PreviousAttempt
  = PreviousCanonicalTransaction TransactionId
  | PreviousRetryableFailure RetryableFailure
  | PreviousNonRetryableFailure NonRetryableFailure
  | PreviousFailedBeforeCanonicalFinalization FatalError
  | PreviousFailedAfterCanonicalFinalization FatalError

derive instance eqPreviousAttempt :: Eq PreviousAttempt

data RetryableFailure
  = RetryableValidationOutage
  | RetryableWriteTimeout

derive instance eqRetryableFailure :: Eq RetryableFailure

data NonRetryableFailure
  = NonRetryableValidationFailure ValidationErrors
  | NonRetryableDuplicateFinalized

derive instance eqNonRetryableFailure :: Eq NonRetryableFailure

newtype ValidationConfig = ValidationConfig
  { quantityPolicy :: QuantityPolicy
  , costPolicy :: CostPolicy
  , vehicleStatusPolicy :: VehicleStatusPolicy
  , volumeWarningPolicy :: VolumeWarningPolicy
  , costWarningPolicy :: CostWarningPolicy
  }

derive newtype instance eqValidationConfig :: Eq ValidationConfig

data QuantityPolicy
  = PositiveLitersRequired

derive instance eqQuantityPolicy :: Eq QuantityPolicy

data CostPolicy
  = NonNegativeTotalCostRequired

derive instance eqCostPolicy :: Eq CostPolicy

data VehicleStatusPolicy
  = RejectInactiveVehicles
  | WarnInactiveVehicles

derive instance eqVehicleStatusPolicy :: Eq VehicleStatusPolicy

data VolumeWarningPolicy
  = NoVolumeWarning
  | WarnWhenLitersExceed Number

derive instance eqVolumeWarningPolicy :: Eq VolumeWarningPolicy

data CostWarningPolicy
  = NoCostWarning
  | WarnWhenTotalCostExceeds Number

derive instance eqCostWarningPolicy :: Eq CostWarningPolicy

data ValidationError
  = InvalidFuelQuantity Number
  | InvalidTotalCost Number
  | VehicleIsInactive VehicleId

derive instance eqValidationError :: Eq ValidationError

data Warning
  = FuelQuantityAboveReviewLimit Number
  | TotalCostAboveReviewLimit Number
  | InactiveVehicleAllowed VehicleId

derive instance eqWarning :: Eq Warning

data ValidationErrors = ValidationErrors ValidationError (Array ValidationError)

derive instance eqValidationErrors :: Eq ValidationErrors

data Warnings = Warnings Warning (Array Warning)

derive instance eqWarnings :: Eq Warnings

data ValidationAccumulation
  = NoValidationErrors
  | HasValidationErrors ValidationErrors

derive instance eqValidationAccumulation :: Eq ValidationAccumulation

data WarningAccumulation
  = NoWarnings
  | HasWarnings Warnings

derive instance eqWarningAccumulation :: Eq WarningAccumulation

data RejectionReason
  = RejectedByValidation ValidationErrors
  | RejectedVehicleMissing VehicleKey

derive instance eqRejectionReason :: Eq RejectionReason

data DuplicateSkipReason
  = DuplicateInNormalMode PreviousAttempt
  | DuplicateInRetryModeNotRetryable PreviousAttempt
  | DuplicateInRecoveryModeNotPreCanonicalFailure PreviousAttempt

derive instance eqDuplicateSkipReason :: Eq DuplicateSkipReason

data FatalError
  = VehicleLookupUnavailable String
  | DuplicateCheckUnavailable String
  | TransactionConstructionFailed String

derive instance eqFatalError :: Eq FatalError

newtype Transaction = Transaction
  { transactionId :: TransactionId
  , sourceRowId :: RowId
  , vehicleId :: VehicleId
  , externalReference :: ExternalReference
  , occurredAt :: CanonicalTimestamp
  , liters :: Number
  , totalCost :: Number
  , uploadMode :: UploadMode
  }

derive newtype instance eqTransaction :: Eq Transaction

newtype RejectedRow = RejectedRow
  { row :: ParsedFuelRow
  , reason :: RejectionReason
  }

derive newtype instance eqRejectedRow :: Eq RejectedRow

newtype SkippedDuplicate = SkippedDuplicate
  { row :: ParsedFuelRow
  , reason :: DuplicateSkipReason
  }

derive newtype instance eqSkippedDuplicate :: Eq SkippedDuplicate

data RowDecision
  = AcceptedTransaction Transaction
  | WarningWithTransaction Transaction Warnings
  | SkippedDuplicateRow SkippedDuplicate
  | RejectedRowDecision RejectedRow
  | FatalProcessingError FatalError

derive instance eqRowDecision :: Eq RowDecision

data BatchOutcome
  = BatchProcessable
  | BatchBlockedByFatals (Array FatalError)

derive instance eqBatchOutcome :: Eq BatchOutcome

newtype BatchSummary = BatchSummary
  { totalRows :: Int
  , acceptedTransactions :: Int
  , warningTransactions :: Int
  , skippedDuplicates :: Int
  , rejectedRows :: Int
  , fatalErrors :: Int
  }

derive newtype instance eqBatchSummary :: Eq BatchSummary

newtype BatchDecision = BatchDecision
  { summary :: BatchSummary
  , outcome :: BatchOutcome
  , rowDecisions :: Array RowDecision
  }

derive newtype instance eqBatchDecision :: Eq BatchDecision

defaultValidationConfig :: ValidationConfig
defaultValidationConfig =
  ValidationConfig
    { quantityPolicy: PositiveLitersRequired
    , costPolicy: NonNegativeTotalCostRequired
    , vehicleStatusPolicy: RejectInactiveVehicles
    , volumeWarningPolicy: NoVolumeWarning
    , costWarningPolicy: NoCostWarning
    }

classifyRow ::
  ValidationConfig ->
  UploadMode ->
  ParsedFuelRow ->
  VehicleLookupResult ->
  DuplicateCheckResult ->
  RowDecision
classifyRow config mode row vehicleLookup duplicateCheck =
  case vehicleLookup of
    VehicleLookupFatal fatal ->
      FatalProcessingError fatal

    VehicleNotFound vehicleKey ->
      RejectedRowDecision (RejectedRow { row, reason: RejectedVehicleMissing vehicleKey })

    VehicleFound vehicle ->
      case duplicateCheck of
        UniqueTransaction ->
          classifyCandidate config mode row vehicle

        DuplicateCheckFatal fatal ->
          FatalProcessingError fatal

        DuplicateTransaction previousAttempt ->
          case duplicateDisposition mode row previousAttempt of
            ContinueAsCandidate ->
              classifyCandidate config mode row vehicle

            StopForDuplicate skipped ->
              SkippedDuplicateRow skipped

classifyBatch ::
  ValidationConfig ->
  UploadMode ->
  Array
    { row :: ParsedFuelRow
    , vehicleLookup :: VehicleLookupResult
    , duplicateCheck :: DuplicateCheckResult
    } ->
  BatchDecision
classifyBatch config mode inputs =
  let
    decisions =
      inputs <#> \input ->
        classifyRow config mode input.row input.vehicleLookup input.duplicateCheck

    summary =
      summarize decisions

    outcome =
      batchOutcome decisions
  in
    BatchDecision { summary, outcome, rowDecisions: decisions }

data DuplicateDisposition
  = ContinueAsCandidate
  | StopForDuplicate SkippedDuplicate

duplicateDisposition :: UploadMode -> ParsedFuelRow -> PreviousAttempt -> DuplicateDisposition
duplicateDisposition mode row previousAttempt =
  case mode of
    Normal ->
      StopForDuplicate
        ( SkippedDuplicate
            { row
            , reason: DuplicateInNormalMode previousAttempt
            }
        )

    Retry ->
      case previousAttempt of
        PreviousRetryableFailure _ ->
          ContinueAsCandidate

        _ ->
          StopForDuplicate
            ( SkippedDuplicate
                { row
                , reason: DuplicateInRetryModeNotRetryable previousAttempt
                }
            )

    Recovery ->
      case previousAttempt of
        PreviousFailedBeforeCanonicalFinalization _ ->
          ContinueAsCandidate

        _ ->
          StopForDuplicate
            ( SkippedDuplicate
                { row
                , reason: DuplicateInRecoveryModeNotPreCanonicalFailure previousAttempt
                }
            )

classifyCandidate :: ValidationConfig -> UploadMode -> ParsedFuelRow -> Vehicle -> RowDecision
classifyCandidate config mode row vehicle =
  case validationErrors config row vehicle of
    HasValidationErrors errors ->
      RejectedRowDecision (RejectedRow { row, reason: RejectedByValidation errors })

    NoValidationErrors ->
      let
        transaction =
          buildTransaction mode row vehicle
      in
        case rowWarnings config row vehicle of
          NoWarnings ->
            AcceptedTransaction transaction

          HasWarnings warnings ->
            WarningWithTransaction transaction warnings

buildTransaction :: UploadMode -> ParsedFuelRow -> Vehicle -> Transaction
buildTransaction mode (ParsedFuelRow row) (Vehicle vehicle) =
  Transaction
    { transactionId: transactionIdFromReference row.externalReference
    , sourceRowId: row.rowId
    , vehicleId: vehicle.vehicleId
    , externalReference: row.externalReference
    , occurredAt: row.occurredAt
    , liters: row.liters
    , totalCost: row.totalCost
    , uploadMode: mode
    }

transactionIdFromReference :: ExternalReference -> TransactionId
transactionIdFromReference (ExternalReference reference) =
  TransactionId reference

validationErrors :: ValidationConfig -> ParsedFuelRow -> Vehicle -> ValidationAccumulation
validationErrors (ValidationConfig config) (ParsedFuelRow row) (Vehicle vehicle) =
  NoValidationErrors
    # requireValidQuantity config.quantityPolicy row.liters
    # requireValidTotalCost config.costPolicy row.totalCost
    # requireValidVehicleStatus config.vehicleStatusPolicy vehicle

requireValidQuantity :: QuantityPolicy -> Number -> ValidationAccumulation -> ValidationAccumulation
requireValidQuantity PositiveLitersRequired liters accumulation =
  if liters > 0.0 then
    accumulation
  else
    addValidationError (InvalidFuelQuantity liters) accumulation

requireValidTotalCost :: CostPolicy -> Number -> ValidationAccumulation -> ValidationAccumulation
requireValidTotalCost NonNegativeTotalCostRequired totalCost accumulation =
  if totalCost >= 0.0 then
    accumulation
  else
    addValidationError (InvalidTotalCost totalCost) accumulation

requireValidVehicleStatus :: forall r. VehicleStatusPolicy -> { vehicleId :: VehicleId, status :: VehicleStatus | r } -> ValidationAccumulation -> ValidationAccumulation
requireValidVehicleStatus RejectInactiveVehicles vehicle accumulation =
  case vehicle.status of
    ActiveVehicle ->
      accumulation

    InactiveVehicle ->
      addValidationError (VehicleIsInactive vehicle.vehicleId) accumulation

requireValidVehicleStatus WarnInactiveVehicles _ accumulation =
  accumulation

rowWarnings :: ValidationConfig -> ParsedFuelRow -> Vehicle -> WarningAccumulation
rowWarnings (ValidationConfig config) (ParsedFuelRow row) (Vehicle vehicle) =
  NoWarnings
    # warnForVolume config.volumeWarningPolicy row.liters
    # warnForCost config.costWarningPolicy row.totalCost
    # warnForVehicleStatus config.vehicleStatusPolicy vehicle

warnForVolume :: VolumeWarningPolicy -> Number -> WarningAccumulation -> WarningAccumulation
warnForVolume NoVolumeWarning _ accumulation =
  accumulation
warnForVolume (WarnWhenLitersExceed maximumLiters) liters accumulation =
  if liters > maximumLiters then
    addWarning (FuelQuantityAboveReviewLimit liters) accumulation
  else
    accumulation

warnForCost :: CostWarningPolicy -> Number -> WarningAccumulation -> WarningAccumulation
warnForCost NoCostWarning _ accumulation =
  accumulation
warnForCost (WarnWhenTotalCostExceeds maximumCost) totalCost accumulation =
  if totalCost > maximumCost then
    addWarning (TotalCostAboveReviewLimit totalCost) accumulation
  else
    accumulation

warnForVehicleStatus :: forall r. VehicleStatusPolicy -> { vehicleId :: VehicleId, status :: VehicleStatus | r } -> WarningAccumulation -> WarningAccumulation
warnForVehicleStatus RejectInactiveVehicles _ accumulation =
  accumulation
warnForVehicleStatus WarnInactiveVehicles vehicle accumulation =
  case vehicle.status of
    ActiveVehicle ->
      accumulation

    InactiveVehicle ->
      addWarning (InactiveVehicleAllowed vehicle.vehicleId) accumulation

addValidationError :: ValidationError -> ValidationAccumulation -> ValidationAccumulation
addValidationError error NoValidationErrors =
  HasValidationErrors (ValidationErrors error [])
addValidationError error (HasValidationErrors errors) =
  HasValidationErrors (appendValidationError error errors)

appendValidationError :: ValidationError -> ValidationErrors -> ValidationErrors
appendValidationError error (ValidationErrors first rest) =
  ValidationErrors first (rest <> [ error ])

addWarning :: Warning -> WarningAccumulation -> WarningAccumulation
addWarning warning NoWarnings =
  HasWarnings (Warnings warning [])
addWarning warning (HasWarnings warnings) =
  HasWarnings (appendWarning warning warnings)

appendWarning :: Warning -> Warnings -> Warnings
appendWarning warning (Warnings first rest) =
  Warnings first (rest <> [ warning ])

validationErrorsToArray :: ValidationErrors -> Array ValidationError
validationErrorsToArray (ValidationErrors first rest) =
  [ first ] <> rest

warningsToArray :: Warnings -> Array Warning
warningsToArray (Warnings first rest) =
  [ first ] <> rest

summarize :: Array RowDecision -> BatchSummary
summarize decisions =
  BatchSummary (foldl addDecisionToSummary emptySummary decisions)

emptySummary ::
  { totalRows :: Int
  , acceptedTransactions :: Int
  , warningTransactions :: Int
  , skippedDuplicates :: Int
  , rejectedRows :: Int
  , fatalErrors :: Int
  }
emptySummary =
  { totalRows: 0
  , acceptedTransactions: 0
  , warningTransactions: 0
  , skippedDuplicates: 0
  , rejectedRows: 0
  , fatalErrors: 0
  }

addDecisionToSummary ::
  { totalRows :: Int
  , acceptedTransactions :: Int
  , warningTransactions :: Int
  , skippedDuplicates :: Int
  , rejectedRows :: Int
  , fatalErrors :: Int
  } ->
  RowDecision ->
  { totalRows :: Int
  , acceptedTransactions :: Int
  , warningTransactions :: Int
  , skippedDuplicates :: Int
  , rejectedRows :: Int
  , fatalErrors :: Int
  }
addDecisionToSummary summary decision =
  case decision of
    AcceptedTransaction _ ->
      summary
        { totalRows = summary.totalRows + 1
        , acceptedTransactions = summary.acceptedTransactions + 1
        }

    WarningWithTransaction _ _ ->
      summary
        { totalRows = summary.totalRows + 1
        , acceptedTransactions = summary.acceptedTransactions + 1
        , warningTransactions = summary.warningTransactions + 1
        }

    SkippedDuplicateRow _ ->
      summary
        { totalRows = summary.totalRows + 1
        , skippedDuplicates = summary.skippedDuplicates + 1
        }

    RejectedRowDecision _ ->
      summary
        { totalRows = summary.totalRows + 1
        , rejectedRows = summary.rejectedRows + 1
        }

    FatalProcessingError _ ->
      summary
        { totalRows = summary.totalRows + 1
        , fatalErrors = summary.fatalErrors + 1
        }

batchOutcome :: Array RowDecision -> BatchOutcome
batchOutcome decisions =
  case foldl collectFatal [] decisions of
    [] ->
      BatchProcessable

    fatalErrors ->
      BatchBlockedByFatals fatalErrors

collectFatal :: Array FatalError -> RowDecision -> Array FatalError
collectFatal fatalErrors decision =
  case decision of
    FatalProcessingError fatal ->
      fatalErrors <> [ fatal ]

    _ ->
      fatalErrors
