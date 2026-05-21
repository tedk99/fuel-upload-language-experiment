module FuelUpload.Domain.Decision
  ( FuelTransaction (..)
  , ValidationError (..)
  , ValidationWarning (..)
  , RejectionReason (..)
  , DuplicateSkipReason (..)
  , RejectedRow (..)
  , SkippedDuplicate (..)
  , RowDecision (..)
  , BatchOutcome (..)
  , BatchDecision (..)
  , BatchSummary (..)
  , RowContext (..)
  ) where

import Data.List.NonEmpty (NonEmpty)
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row
import FuelUpload.Domain.Vehicle

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
