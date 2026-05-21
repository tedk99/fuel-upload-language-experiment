module FuelUpload.DuplicatePolicy
  ( DuplicatePolicyDecision (..)
  , duplicateDecision
  , skipReasonForPreviousAttempt
  ) where

import FuelUpload.Domain.Decision
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive

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
duplicateDecision ConservativeRecovery (DuplicateOf previousAttempt)
  | previousCanonicalizationState previousAttempt == FailedBeforeCanonicalization =
      UploadDuplicate
  | otherwise =
      SkipDuplicate (skipReasonForPreviousAttempt previousAttempt)
duplicateDecision AggressiveRecovery (DuplicateOf previousAttempt)
  | previousCanonicalizationState previousAttempt == FailedBeforeCanonicalization =
      UploadDuplicate
  | previousCanonicalizationState previousAttempt == CanonicalizedWithoutTransactionKey
      && previousFinalizationState previousAttempt /= Finalized =
      UploadDuplicate
  | otherwise =
      SkipDuplicate (skipReasonForPreviousAttempt previousAttempt)

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
