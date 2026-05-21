use rust_fuel_engine::*;

#[test]
fn duplicate_normal_is_skipped() {
    let decision = classify_row(
        &row_input(valid_row(), found_vehicle(), finalized_duplicate()),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(
        decision,
        RowDecision::SkippedDuplicate(SkippedDuplicate {
            mode: UploadMode::Normal,
            ..
        })
    ));
}

#[test]
fn duplicate_retry_is_skipped_when_previous_attempt_not_explicitly_retryable() {
    let decision = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::NotRetryable,
                FinalizationState::FailedBeforeCanonicalFinalization,
            ),
        ),
        UploadMode::Retry,
        &quiet_config(),
    );

    assert!(matches!(
        decision,
        RowDecision::SkippedDuplicate(SkippedDuplicate {
            mode: UploadMode::Retry,
            ..
        })
    ));
}

#[test]
fn duplicate_retry_is_accepted_when_previous_attempt_explicitly_retryable() {
    let decision = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::ExplicitlyRetryable,
                FinalizationState::FailedAfterCanonicalFinalization,
            ),
        ),
        UploadMode::Retry,
        &quiet_config(),
    );

    assert!(matches!(decision, RowDecision::Accepted(_)));
}

#[test]
fn duplicate_conservative_recovery_preserves_old_recovery_behavior() {
    let accepted = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::NotRetryable,
                FinalizationState::FailedBeforeCanonicalFinalization,
            ),
        ),
        UploadMode::ConservativeRecovery,
        &quiet_config(),
    );
    let skipped = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::ExplicitlyRetryable,
                FinalizationState::FailedAfterCanonicalFinalization,
            ),
        ),
        UploadMode::ConservativeRecovery,
        &quiet_config(),
    );
    let finalized = classify_row(
        &row_input(valid_row(), found_vehicle(), finalized_duplicate()),
        UploadMode::ConservativeRecovery,
        &quiet_config(),
    );

    assert!(matches!(accepted, RowDecision::Accepted(_)));
    assert!(matches!(skipped, RowDecision::SkippedDuplicate(_)));
    assert!(matches!(finalized, RowDecision::SkippedDuplicate(_)));
}

#[test]
fn duplicate_aggressive_recovery_accepts_failed_after_canonicalization_only_without_key() {
    let accepted = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt_without_canonical_key(
                RetryEligibility::ExplicitlyRetryable,
                FinalizationState::FailedAfterCanonicalFinalization,
            ),
        ),
        UploadMode::AggressiveRecovery,
        &quiet_config(),
    );
    let skipped = classify_row(
        &row_input(
            valid_row(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::ExplicitlyRetryable,
                FinalizationState::FailedAfterCanonicalFinalization,
            ),
        ),
        UploadMode::AggressiveRecovery,
        &quiet_config(),
    );

    assert!(matches!(accepted, RowDecision::Accepted(_)));
    assert!(matches!(skipped, RowDecision::SkippedDuplicate(_)));
}

#[test]
fn recovery_modes_still_quarantine_accepted_suspicious_duplicates() {
    let mut suspicious = valid_row();
    suspicious.merchant = Merchant::Known("manual review".to_string());

    let conservative = classify_row(
        &row_input(
            suspicious.clone(),
            found_vehicle(),
            previous_attempt(
                RetryEligibility::NotRetryable,
                FinalizationState::FailedBeforeCanonicalFinalization,
            ),
        ),
        UploadMode::ConservativeRecovery,
        &quiet_config(),
    );
    let aggressive = classify_row(
        &row_input(
            suspicious,
            found_vehicle(),
            previous_attempt_without_canonical_key(
                RetryEligibility::ExplicitlyRetryable,
                FinalizationState::FailedAfterCanonicalFinalization,
            ),
        ),
        UploadMode::AggressiveRecovery,
        &quiet_config(),
    );

    assert!(matches!(conservative, RowDecision::Quarantined { .. }));
    assert!(matches!(aggressive, RowDecision::Quarantined { .. }));
}

fn valid_row() -> ParsedFuelRow {
    ParsedFuelRow {
        row_number: RowNumber(1),
        source_id: SourceRowId("row-1".to_string()),
        vehicle_ref: VehicleRef("truck-1".to_string()),
        occurred_on: FuelDate("2026-05-21".to_string()),
        quantity_liters: 40.0,
        total_cost: 60.0,
        odometer: OdometerReading::Missing,
        merchant: Merchant::Known("Depot".to_string()),
    }
}

fn found_vehicle() -> VehicleLookupResult {
    VehicleLookupResult::Found(Vehicle {
        id: VehicleId("vehicle-1".to_string()),
        reference: VehicleRef("truck-1".to_string()),
    })
}

fn row_input(
    row: ParsedFuelRow,
    vehicle_lookup: VehicleLookupResult,
    duplicate_check: DuplicateCheckResult,
) -> RowInput {
    RowInput {
        row,
        vehicle_lookup,
        duplicate_check,
    }
}

fn quiet_config() -> ValidationConfig {
    ValidationConfig {
        large_quantity_warning: WarningLimit::Disabled,
        high_unit_cost_warning: WarningLimit::Disabled,
        odometer_rule: OdometerRule::Optional,
        ..ValidationConfig::default()
    }
}

fn finalized_duplicate() -> DuplicateCheckResult {
    DuplicateCheckResult::Duplicate(DuplicateState::CanonicalFinalized {
        transaction_id: TransactionId("txn-1".to_string()),
    })
}

fn previous_attempt(
    retry: RetryEligibility,
    finalization: FinalizationState,
) -> DuplicateCheckResult {
    DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
        attempt_id: AttemptId("attempt-1".to_string()),
        retry,
        finalization,
        canonical_transaction: CanonicalTransactionKey::Present(TransactionId("txn-1".to_string())),
    })
}

fn previous_attempt_without_canonical_key(
    retry: RetryEligibility,
    finalization: FinalizationState,
) -> DuplicateCheckResult {
    DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
        attempt_id: AttemptId("attempt-1".to_string()),
        retry,
        finalization,
        canonical_transaction: CanonicalTransactionKey::Missing,
    })
}
