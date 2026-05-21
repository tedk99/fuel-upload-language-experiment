use super::primitives::*;
use super::validation::FatalError;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DuplicateCheckResult {
    Unique,
    Duplicate(DuplicateState),
    Fatal(FatalError),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DuplicateState {
    CanonicalFinalized {
        transaction_id: TransactionId,
    },
    PreviousAttempt {
        attempt_id: AttemptId,
        retry: RetryEligibility,
        finalization: FinalizationState,
        canonical_transaction: CanonicalTransactionKey,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RetryEligibility {
    ExplicitlyRetryable,
    NotRetryable,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FinalizationState {
    FailedBeforeCanonicalFinalization,
    FailedAfterCanonicalFinalization,
    Unknown,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CanonicalTransactionKey {
    Present(TransactionId),
    Missing,
}
