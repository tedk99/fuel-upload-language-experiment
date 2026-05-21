module FuelUpload.Domain.Duplicate
  ( DuplicateCheckResult (..)
  , DuplicateState (..)
  , PreviousAttempt (..)
  , CanonicalizationState (..)
  , FinalizationState (..)
  ) where

import FuelUpload.Domain.Primitive

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
  = CanonicalizedWithTransactionKey TransactionId
  | CanonicalizedWithoutTransactionKey
  | FailedBeforeCanonicalization
  deriving stock (Eq, Show)

data FinalizationState
  = Finalized
  | FailedRetryable
  | FailedNotRetryable
  deriving stock (Eq, Show)
