use crate::domain::*;

pub(crate) enum DuplicateGate {
    Continue,
    Skip(SkippedDuplicate),
    Fatal(FatalError),
}

pub(crate) fn duplicate_gate(
    row: &ParsedFuelRow,
    duplicate_check: &DuplicateCheckResult,
    mode: UploadMode,
) -> DuplicateGate {
    match (mode, duplicate_check) {
        (_, DuplicateCheckResult::Unique) => DuplicateGate::Continue,
        (_, DuplicateCheckResult::Fatal(error)) => DuplicateGate::Fatal(error.clone()),
        (UploadMode::Normal, DuplicateCheckResult::Duplicate(state)) => {
            DuplicateGate::Skip(skipped_duplicate(row, state, mode))
        }
        (
            UploadMode::Retry,
            DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
                retry: RetryEligibility::ExplicitlyRetryable,
                ..
            }),
        ) => DuplicateGate::Continue,
        (UploadMode::Retry, DuplicateCheckResult::Duplicate(state)) => {
            DuplicateGate::Skip(skipped_duplicate(row, state, mode))
        }
        (
            UploadMode::ConservativeRecovery,
            DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
                finalization: FinalizationState::FailedBeforeCanonicalFinalization,
                ..
            }),
        ) => DuplicateGate::Continue,
        (UploadMode::ConservativeRecovery, DuplicateCheckResult::Duplicate(state)) => {
            DuplicateGate::Skip(skipped_duplicate(row, state, mode))
        }
        (
            UploadMode::AggressiveRecovery,
            DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
                finalization: FinalizationState::FailedBeforeCanonicalFinalization,
                ..
            }),
        ) => DuplicateGate::Continue,
        (
            UploadMode::AggressiveRecovery,
            DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
                finalization: FinalizationState::FailedAfterCanonicalFinalization,
                canonical_transaction: CanonicalTransactionKey::Missing,
                ..
            }),
        ) => DuplicateGate::Continue,
        (UploadMode::AggressiveRecovery, DuplicateCheckResult::Duplicate(state)) => {
            DuplicateGate::Skip(skipped_duplicate(row, state, mode))
        }
    }
}

fn skipped_duplicate(
    row: &ParsedFuelRow,
    state: &DuplicateState,
    mode: UploadMode,
) -> SkippedDuplicate {
    SkippedDuplicate {
        row_number: row.row_number,
        source_id: row.source_id.clone(),
        state: state.clone(),
        mode,
    }
}
