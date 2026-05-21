module FuelUpload.Audit
  ( AuditEventKind (..)
  , AuditRecord (..)
  , AuditRecordDto (..)
  , projectAudit
  , toAuditRecordDto
  , auditStatus
  ) where

import FuelUpload.Domain.Decision
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row

data AuditEventKind
  = AuditAccepted
  | AuditAcceptedWithWarnings
  | AuditRejected
  | AuditSkippedDuplicate
  | AuditQuarantined
  | AuditFatalBatch
  deriving stock (Eq, Show)

data AuditRecord = AuditRecord
  { auditKind :: AuditEventKind
  , auditRowNumber :: Maybe RowNumber
  , auditExternalRowId :: Maybe ExternalRowId
  , auditTransactionId :: Maybe TransactionId
  , auditVehicleId :: Maybe VehicleId
  , auditWarnings :: [ValidationWarning]
  , auditQuarantineReasons :: [QuarantineReason]
  , auditRejectionReason :: Maybe RejectionReason
  , auditDuplicateSkipReason :: Maybe DuplicateSkipReason
  , auditFatalError :: Maybe FatalError
  }
  deriving stock (Eq, Show)

data AuditRecordDto = AuditRecordDto
  { auditDtoStatus :: String
  , auditDtoRowNumber :: Maybe Int
  , auditDtoExternalRowId :: Maybe String
  , auditDtoTransactionId :: Maybe String
  , auditDtoVehicleId :: Maybe String
  , auditDtoWarnings :: [String]
  , auditDtoQuarantineReasons :: [String]
  , auditDtoRejectionReason :: Maybe String
  , auditDtoDuplicateSkipReason :: Maybe String
  , auditDtoFatalError :: Maybe String
  }
  deriving stock (Eq, Show)

projectAudit :: BatchDecision -> [AuditRecord]
projectAudit =
  fmap projectRow . batchRows

toAuditRecordDto :: AuditRecord -> AuditRecordDto
toAuditRecordDto record =
  AuditRecordDto
    { auditDtoStatus = auditStatus (auditKind record)
    , auditDtoRowNumber = rowNumberValue <$> auditRowNumber record
    , auditDtoExternalRowId = externalRowValue <$> auditExternalRowId record
    , auditDtoTransactionId = transactionIdValue <$> auditTransactionId record
    , auditDtoVehicleId = vehicleIdValue <$> auditVehicleId record
    , auditDtoWarnings = show <$> auditWarnings record
    , auditDtoQuarantineReasons = show <$> auditQuarantineReasons record
    , auditDtoRejectionReason = show <$> auditRejectionReason record
    , auditDtoDuplicateSkipReason = show <$> auditDuplicateSkipReason record
    , auditDtoFatalError = show <$> auditFatalError record
    }

auditStatus :: AuditEventKind -> String
auditStatus kind =
  case kind of
    AuditAccepted -> "accepted"
    AuditAcceptedWithWarnings -> "accepted_with_warnings"
    AuditRejected -> "rejected"
    AuditSkippedDuplicate -> "skipped_duplicate"
    AuditQuarantined -> "quarantined"
    AuditFatalBatch -> "fatal_batch"

projectRow :: RowDecision -> AuditRecord
projectRow decision =
  case decision of
    Accepted transaction ->
      transactionRecord AuditAccepted [] [] transaction
    AcceptedWithWarnings transaction warnings ->
      transactionRecord AuditAcceptedWithWarnings (nonEmptyToList warnings) [] transaction
    Quarantined transaction reasons ->
      transactionRecord AuditQuarantined [] (nonEmptyToList reasons) transaction
    SkippedDuplicate skipped ->
      (emptyRecord AuditSkippedDuplicate)
        { auditRowNumber = Just (parsedRowNumber (skippedRow skipped))
        , auditExternalRowId = Just (parsedExternalRowId (skippedRow skipped))
        , auditDuplicateSkipReason = Just (duplicateSkipReason skipped)
        }
    Rejected rejected ->
      (emptyRecord AuditRejected)
        { auditRowNumber = Just (parsedRowNumber (rejectedRow rejected))
        , auditExternalRowId = Just (parsedExternalRowId (rejectedRow rejected))
        , auditRejectionReason = Just (rejectionReason rejected)
        }
    Fatal fatalError ->
      (emptyRecord AuditFatalBatch)
        { auditRowNumber = Just (fatalRowNumber fatalError)
        , auditFatalError = Just fatalError
        }

transactionRecord ::
  AuditEventKind ->
  [ValidationWarning] ->
  [QuarantineReason] ->
  FuelTransaction ->
  AuditRecord
transactionRecord kind warnings quarantines transaction =
  (emptyRecord kind)
    { auditRowNumber = Just (transactionRowNumber transaction)
    , auditExternalRowId = Just (transactionExternalRowId transaction)
    , auditTransactionId = Just (transactionId transaction)
    , auditVehicleId = Just (transactionVehicleId transaction)
    , auditWarnings = warnings
    , auditQuarantineReasons = quarantines
    }

emptyRecord :: AuditEventKind -> AuditRecord
emptyRecord kind =
  AuditRecord
    { auditKind = kind
    , auditRowNumber = Nothing
    , auditExternalRowId = Nothing
    , auditTransactionId = Nothing
    , auditVehicleId = Nothing
    , auditWarnings = []
    , auditQuarantineReasons = []
    , auditRejectionReason = Nothing
    , auditDuplicateSkipReason = Nothing
    , auditFatalError = Nothing
    }

fatalRowNumber :: FatalError -> RowNumber
fatalRowNumber fatalError =
  case fatalError of
    VehicleLookupUnavailable rowNumber -> rowNumber
    DuplicateCheckUnavailable rowNumber -> rowNumber
    CorruptParsedRow rowNumber -> rowNumber

rowNumberValue :: RowNumber -> Int
rowNumberValue (RowNumber value) = value

externalRowValue :: ExternalRowId -> String
externalRowValue (ExternalRowId value) = value

transactionIdValue :: TransactionId -> String
transactionIdValue (TransactionId value) = value

vehicleIdValue :: VehicleId -> String
vehicleIdValue (VehicleId value) = value

nonEmptyToList :: Foldable f => f a -> [a]
nonEmptyToList = foldr (:) []
