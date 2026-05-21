module FuelUpload.Report
  ( OperationalBatchStatus (..)
  , OperationalQuarantinedRow (..)
  , OperationalBatchReport (..)
  , projectOperationalReport
  ) where

import FuelUpload.Domain.Decision
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row

data OperationalBatchStatus
  = OperationalReady
  | OperationalFatal
  deriving stock (Eq, Show)

data OperationalQuarantinedRow = OperationalQuarantinedRow
  { operationalQuarantinedRowNumber :: RowNumber
  , operationalQuarantineReasons :: [QuarantineReason]
  }
  deriving stock (Eq, Show)

data OperationalBatchReport = OperationalBatchReport
  { operationalStatus :: OperationalBatchStatus
  , operationalCounts :: BatchSummary
  , operationalUploadedTransactionIds :: [TransactionId]
  , operationalRejectedRowNumbers :: [RowNumber]
  , operationalQuarantinedRows :: [OperationalQuarantinedRow]
  , operationalSkippedDuplicateRowNumbers :: [RowNumber]
  , operationalFatalErrors :: [FatalError]
  }
  deriving stock (Eq, Show)

projectOperationalReport :: BatchDecision -> OperationalBatchReport
projectOperationalReport decision =
  OperationalBatchReport
    { operationalStatus = status
    , operationalCounts = batchSummary decision
    , operationalUploadedTransactionIds =
        case status of
          OperationalReady -> acceptedTransactionIds (batchRows decision)
          OperationalFatal -> []
    , operationalRejectedRowNumbers = rejectedRowNumbers (batchRows decision)
    , operationalQuarantinedRows = quarantinedRows (batchRows decision)
    , operationalSkippedDuplicateRowNumbers = skippedDuplicateRowNumbers (batchRows decision)
    , operationalFatalErrors = fatalErrors
    }
  where
    (status, fatalErrors) =
      case batchOutcome decision of
        BatchUploadable -> (OperationalReady, [])
        BatchBlockedByFatal errors -> (OperationalFatal, nonEmptyToList errors)

acceptedTransactionIds :: [RowDecision] -> [TransactionId]
acceptedTransactionIds =
  foldr collect []
  where
    collect row ids =
      case row of
        Accepted transaction -> transactionId transaction : ids
        AcceptedWithWarnings transaction _ -> transactionId transaction : ids
        Quarantined _ _ -> ids
        SkippedDuplicate _ -> ids
        Rejected _ -> ids
        Fatal _ -> ids

rejectedRowNumbers :: [RowDecision] -> [RowNumber]
rejectedRowNumbers =
  foldr collect []
  where
    collect row numbers =
      case row of
        Rejected rejected -> parsedRowNumber (rejectedRow rejected) : numbers
        Accepted _ -> numbers
        AcceptedWithWarnings _ _ -> numbers
        Quarantined _ _ -> numbers
        SkippedDuplicate _ -> numbers
        Fatal _ -> numbers

quarantinedRows :: [RowDecision] -> [OperationalQuarantinedRow]
quarantinedRows =
  foldr collect []
  where
    collect row quarantines =
      case row of
        Quarantined transaction reasons ->
          OperationalQuarantinedRow
            { operationalQuarantinedRowNumber = transactionRowNumber transaction
            , operationalQuarantineReasons = nonEmptyToList reasons
            }
            : quarantines
        Accepted _ -> quarantines
        AcceptedWithWarnings _ _ -> quarantines
        SkippedDuplicate _ -> quarantines
        Rejected _ -> quarantines
        Fatal _ -> quarantines

skippedDuplicateRowNumbers :: [RowDecision] -> [RowNumber]
skippedDuplicateRowNumbers =
  foldr collect []
  where
    collect row numbers =
      case row of
        SkippedDuplicate skipped -> parsedRowNumber (skippedRow skipped) : numbers
        Accepted _ -> numbers
        AcceptedWithWarnings _ _ -> numbers
        Quarantined _ _ -> numbers
        Rejected _ -> numbers
        Fatal _ -> numbers

nonEmptyToList :: Foldable f => f a -> [a]
nonEmptyToList = foldr (:) []
