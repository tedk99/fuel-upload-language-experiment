module FuelUpload.Engine
  ( -- * Input domain
    ParsedFuelRow (..)
  , RowNumber (..)
  , ExternalRowId (..)
  , VehicleId (..)
  , Registration (..)
  , FuelQuantity (..)
  , MoneyAmount (..)
  , OdometerReading (..)
  , TransactionId (..)
  , UploadMode (..)
  , ValidationConfig (..)
  , Vehicle (..)
  , VehicleLookupResult (..)
  , DuplicateCheckResult (..)
  , DuplicateState (..)
  , PreviousAttempt (..)
  , CanonicalizationState (..)
  , FinalizationState (..)

    -- * Output domain
  , FuelTransaction (..)
  , ValidationError (..)
  , ValidationWarning (..)
  , RejectionReason (..)
  , DuplicateSkipReason (..)
  , FatalError (..)
  , RejectedRow (..)
  , SkippedDuplicate (..)
  , RowDecision (..)
  , BatchOutcome (..)
  , BatchDecision (..)
  , BatchSummary (..)

    -- * Engine
  , RowContext (..)
  , classifyRow
  , classifyBatch
  , summarizeRows
  ) where

import Data.List.NonEmpty (NonEmpty (..))

newtype RowNumber = RowNumber Int
  deriving stock (Eq, Ord, Show)

newtype ExternalRowId = ExternalRowId String
  deriving stock (Eq, Ord, Show)

newtype VehicleId = VehicleId String
  deriving stock (Eq, Ord, Show)

newtype Registration = Registration String
  deriving stock (Eq, Ord, Show)

newtype FuelQuantity = FuelQuantity Rational
  deriving stock (Eq, Ord, Show)

newtype MoneyAmount = MoneyAmount Rational
  deriving stock (Eq, Ord, Show)

newtype OdometerReading = OdometerReading Integer
  deriving stock (Eq, Ord, Show)

newtype TransactionId = TransactionId String
  deriving stock (Eq, Ord, Show)

data ParsedFuelRow = ParsedFuelRow
  { parsedRowNumber :: RowNumber
  , parsedExternalRowId :: ExternalRowId
  , parsedRegistration :: Registration
  , parsedQuantity :: FuelQuantity
  , parsedAmount :: MoneyAmount
  , parsedOdometer :: OdometerReading
  }
  deriving stock (Eq, Show)

data UploadMode
  = Normal
  | Retry
  | Recovery
  deriving stock (Eq, Show)

data ValidationConfig = ValidationConfig
  { maximumQuantity :: FuelQuantity
  , maximumAmount :: MoneyAmount
  , highQuantityWarning :: FuelQuantity
  , highAmountWarning :: MoneyAmount
  , highOdometerWarning :: OdometerReading
  }
  deriving stock (Eq, Show)

data Vehicle = Vehicle
  { vehicleId :: VehicleId
  , vehicleRegistration :: Registration
  }
  deriving stock (Eq, Show)

data VehicleLookupResult
  = VehicleFound Vehicle
  | VehicleMissing Registration
  | VehicleLookupFatal FatalError
  deriving stock (Eq, Show)

data DuplicateCheckResult
  = DuplicateCheckSucceeded DuplicateState
  | DuplicateCheckFatal FatalError
  deriving stock (Eq, Show)

data DuplicateState
  = UniqueRow
  | DuplicateOf PreviousAttempt
  deriving stock (Eq, Show)

data PreviousAttempt = PreviousAttempt
  { previousTransactionId :: TransactionId
  , previousCanonicalizationState :: CanonicalizationState
  , previousFinalizationState :: FinalizationState
  }
  deriving stock (Eq, Show)

data CanonicalizationState
  = Canonicalized
  | FailedBeforeCanonicalization
  deriving stock (Eq, Show)

data FinalizationState
  = Finalized
  | FailedRetryable
  | FailedNotRetryable
  deriving stock (Eq, Show)

data FuelTransaction = FuelTransaction
  { transactionId :: TransactionId
  , transactionRowNumber :: RowNumber
  , transactionVehicleId :: VehicleId
  , transactionExternalRowId :: ExternalRowId
  , transactionQuantity :: FuelQuantity
  , transactionAmount :: MoneyAmount
  , transactionOdometer :: OdometerReading
  }
  deriving stock (Eq, Show)

data ValidationError
  = QuantityMustBePositive FuelQuantity
  | AmountMustBePositive MoneyAmount
  | OdometerMustNotBeNegative OdometerReading
  | QuantityExceedsMaximum FuelQuantity FuelQuantity
  | AmountExceedsMaximum MoneyAmount MoneyAmount
  deriving stock (Eq, Show)

data ValidationWarning
  = QuantityAboveWarningThreshold FuelQuantity FuelQuantity
  | AmountAboveWarningThreshold MoneyAmount MoneyAmount
  | OdometerAboveWarningThreshold OdometerReading OdometerReading
  deriving stock (Eq, Show)

data RejectionReason
  = VehicleWasNotFound Registration
  | RowFailedValidation (NonEmpty ValidationError)
  | DuplicateCannotBeUploaded UploadMode DuplicateSkipReason
  deriving stock (Eq, Show)

data DuplicateSkipReason
  = AlreadyFinalized TransactionId
  | PreviousAttemptNotRetryable TransactionId
  | PreviousAttemptCanonicalized TransactionId
  deriving stock (Eq, Show)

data FatalError
  = VehicleLookupUnavailable RowNumber
  | DuplicateCheckUnavailable RowNumber
  | CorruptParsedRow RowNumber
  deriving stock (Eq, Show)

data RejectedRow = RejectedRow
  { rejectedRow :: ParsedFuelRow
  , rejectionReason :: RejectionReason
  }
  deriving stock (Eq, Show)

data SkippedDuplicate = SkippedDuplicateInfo
  { skippedRow :: ParsedFuelRow
  , duplicateSkipReason :: DuplicateSkipReason
  }
  deriving stock (Eq, Show)

data RowDecision
  = Accepted FuelTransaction
  | AcceptedWithWarnings FuelTransaction (NonEmpty ValidationWarning)
  | SkippedDuplicate SkippedDuplicate
  | Rejected RejectedRow
  | Fatal FatalError
  deriving stock (Eq, Show)

data BatchOutcome
  = BatchUploadable
  | BatchBlockedByFatal (NonEmpty FatalError)
  deriving stock (Eq, Show)

data BatchDecision = BatchDecision
  { batchRows :: [RowDecision]
  , batchSummary :: BatchSummary
  , batchOutcome :: BatchOutcome
  }
  deriving stock (Eq, Show)

data BatchSummary = BatchSummary
  { summaryAccepted :: Int
  , summaryAcceptedWithWarnings :: Int
  , summarySkippedDuplicates :: Int
  , summaryRejected :: Int
  , summaryFatal :: Int
  , summaryTotalRows :: Int
  }
  deriving stock (Eq, Show)

data RowContext = RowContext
  { contextRow :: ParsedFuelRow
  , contextVehicleLookup :: VehicleLookupResult
  , contextDuplicateCheck :: DuplicateCheckResult
  }
  deriving stock (Eq, Show)

classifyRow :: ValidationConfig -> UploadMode -> RowContext -> RowDecision
classifyRow config mode context =
  case (contextVehicleLookup context, contextDuplicateCheck context) of
    (VehicleLookupFatal fatalError, _) ->
      Fatal fatalError
    (_, DuplicateCheckFatal fatalError) ->
      Fatal fatalError
    (VehicleMissing registration, _) ->
      reject (VehicleWasNotFound registration)
    (VehicleFound vehicle, DuplicateCheckSucceeded duplicateState) ->
      case duplicateDecision mode duplicateState of
        UploadDuplicate ->
          classifyValidated vehicle
        SkipDuplicate reason ->
          SkippedDuplicate SkippedDuplicateInfo
            { skippedRow = row
            , duplicateSkipReason = reason
            }
        RejectDuplicate reason ->
          reject (DuplicateCannotBeUploaded mode reason)
  where
    row = contextRow context

    reject reason =
      Rejected RejectedRow
        { rejectedRow = row
        , rejectionReason = reason
        }

    classifyValidated vehicle =
      case validationErrors config row of
        firstError : remainingErrors ->
          reject (RowFailedValidation (firstError :| remainingErrors))
        [] ->
          case validationWarnings config row of
            firstWarning : remainingWarnings ->
              AcceptedWithWarnings
                (toTransaction row vehicle)
                (firstWarning :| remainingWarnings)
            [] ->
              Accepted (toTransaction row vehicle)

classifyBatch :: ValidationConfig -> UploadMode -> [RowContext] -> BatchDecision
classifyBatch config mode contexts =
  let decisions = fmap (classifyRow config mode) contexts
      summary = summarizeRows decisions
   in BatchDecision
        { batchRows = decisions
        , batchSummary = summary
        , batchOutcome = outcomeFromRows decisions
        }

summarizeRows :: [RowDecision] -> BatchSummary
summarizeRows =
  foldr addDecision emptySummary
  where
    emptySummary =
      BatchSummary
        { summaryAccepted = 0
        , summaryAcceptedWithWarnings = 0
        , summarySkippedDuplicates = 0
        , summaryRejected = 0
        , summaryFatal = 0
        , summaryTotalRows = 0
        }

    addDecision decision summary =
      case decision of
        Accepted _ ->
          countAccepted summary
        AcceptedWithWarnings _ _ ->
          countAcceptedWithWarnings summary
        SkippedDuplicate _ ->
          countSkipped summary
        Rejected _ ->
          countRejected summary
        Fatal _ ->
          countFatal summary

    countAccepted summary =
      bumpTotal summary
        { summaryAccepted = summaryAccepted summary + 1
        }

    countAcceptedWithWarnings summary =
      bumpTotal summary
        { summaryAccepted = summaryAccepted summary + 1
        , summaryAcceptedWithWarnings = summaryAcceptedWithWarnings summary + 1
        }

    countSkipped summary =
      bumpTotal summary
        { summarySkippedDuplicates = summarySkippedDuplicates summary + 1
        }

    countRejected summary =
      bumpTotal summary
        { summaryRejected = summaryRejected summary + 1
        }

    countFatal summary =
      bumpTotal summary
        { summaryFatal = summaryFatal summary + 1
        }

    bumpTotal summary =
      summary {summaryTotalRows = summaryTotalRows summary + 1}

data DuplicatePolicyDecision
  = UploadDuplicate
  | SkipDuplicate DuplicateSkipReason
  | RejectDuplicate DuplicateSkipReason
  deriving stock (Eq, Show)

duplicateDecision :: UploadMode -> DuplicateState -> DuplicatePolicyDecision
duplicateDecision _ UniqueRow =
  UploadDuplicate
duplicateDecision Normal (DuplicateOf previousAttempt) =
  SkipDuplicate (skipReasonForPreviousAttempt previousAttempt)
duplicateDecision Retry (DuplicateOf previousAttempt)
  | previousFinalizationState previousAttempt == FailedRetryable =
      UploadDuplicate
  | otherwise =
      SkipDuplicate (skipReasonForPreviousAttempt previousAttempt)
duplicateDecision Recovery (DuplicateOf previousAttempt)
  | previousCanonicalizationState previousAttempt == FailedBeforeCanonicalization =
      UploadDuplicate
  | otherwise =
      RejectDuplicate (skipReasonForPreviousAttempt previousAttempt)

skipReasonForPreviousAttempt :: PreviousAttempt -> DuplicateSkipReason
skipReasonForPreviousAttempt previousAttempt =
  case previousFinalizationState previousAttempt of
    Finalized ->
      AlreadyFinalized transaction
    FailedRetryable ->
      PreviousAttemptCanonicalized transaction
    FailedNotRetryable ->
      PreviousAttemptNotRetryable transaction
  where
    transaction = previousTransactionId previousAttempt

validationErrors :: ValidationConfig -> ParsedFuelRow -> [ValidationError]
validationErrors config row =
  quantityErrors <> amountErrors <> odometerErrors
  where
    quantity = parsedQuantity row
    amount = parsedAmount row
    odometer = parsedOdometer row

    quantityErrors =
      positiveQuantityError quantity
        <> maximumQuantityError (maximumQuantity config) quantity

    amountErrors =
      positiveAmountError amount
        <> maximumAmountError (maximumAmount config) amount

    odometerErrors =
      nonNegativeOdometerError odometer

validationWarnings :: ValidationConfig -> ParsedFuelRow -> [ValidationWarning]
validationWarnings config row =
  highQuantityWarningFor config (parsedQuantity row)
    <> highAmountWarningFor config (parsedAmount row)
    <> highOdometerWarningFor config (parsedOdometer row)

positiveQuantityError :: FuelQuantity -> [ValidationError]
positiveQuantityError quantity@(FuelQuantity value)
  | value <= 0 = [QuantityMustBePositive quantity]
  | otherwise = []

maximumQuantityError :: FuelQuantity -> FuelQuantity -> [ValidationError]
maximumQuantityError maximumAllowed quantity
  | quantity > maximumAllowed = [QuantityExceedsMaximum quantity maximumAllowed]
  | otherwise = []

positiveAmountError :: MoneyAmount -> [ValidationError]
positiveAmountError amount@(MoneyAmount value)
  | value <= 0 = [AmountMustBePositive amount]
  | otherwise = []

maximumAmountError :: MoneyAmount -> MoneyAmount -> [ValidationError]
maximumAmountError maximumAllowed amount
  | amount > maximumAllowed = [AmountExceedsMaximum amount maximumAllowed]
  | otherwise = []

nonNegativeOdometerError :: OdometerReading -> [ValidationError]
nonNegativeOdometerError odometer@(OdometerReading value)
  | value < 0 = [OdometerMustNotBeNegative odometer]
  | otherwise = []

highQuantityWarningFor :: ValidationConfig -> FuelQuantity -> [ValidationWarning]
highQuantityWarningFor config quantity
  | quantity > highQuantityWarning config =
      [QuantityAboveWarningThreshold quantity (highQuantityWarning config)]
  | otherwise =
      []

highAmountWarningFor :: ValidationConfig -> MoneyAmount -> [ValidationWarning]
highAmountWarningFor config amount
  | amount > highAmountWarning config =
      [AmountAboveWarningThreshold amount (highAmountWarning config)]
  | otherwise =
      []

highOdometerWarningFor :: ValidationConfig -> OdometerReading -> [ValidationWarning]
highOdometerWarningFor config odometer
  | odometer > highOdometerWarning config =
      [OdometerAboveWarningThreshold odometer (highOdometerWarning config)]
  | otherwise =
      []

toTransaction :: ParsedFuelRow -> Vehicle -> FuelTransaction
toTransaction row vehicle =
  FuelTransaction
    { transactionId = deriveTransactionId row (vehicleId vehicle)
    , transactionRowNumber = parsedRowNumber row
    , transactionVehicleId = vehicleId vehicle
    , transactionExternalRowId = parsedExternalRowId row
    , transactionQuantity = parsedQuantity row
    , transactionAmount = parsedAmount row
    , transactionOdometer = parsedOdometer row
    }

deriveTransactionId :: ParsedFuelRow -> VehicleId -> TransactionId
deriveTransactionId row (VehicleId vehicleValue) =
  TransactionId
    ( externalRowValue (parsedExternalRowId row)
        <> ":"
        <> vehicleValue
    )
  where
    externalRowValue (ExternalRowId value) = value

outcomeFromRows :: [RowDecision] -> BatchOutcome
outcomeFromRows rows =
  case fatalErrors rows of
    firstFatal : remainingFatals ->
      BatchBlockedByFatal (firstFatal :| remainingFatals)
    [] ->
      BatchUploadable

fatalErrors :: [RowDecision] -> [FatalError]
fatalErrors =
  foldr collectFatal []
  where
    collectFatal decision errors =
      case decision of
        Fatal fatalError -> fatalError : errors
        Accepted _ -> errors
        AcceptedWithWarnings _ _ -> errors
        SkippedDuplicate _ -> errors
        Rejected _ -> errors
