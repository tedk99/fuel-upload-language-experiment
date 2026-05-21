use rust_fuel_engine::*;

#[test]
fn accepts_unique_valid_row_without_warnings() {
    let decision = classify_row(
        &row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(decision, RowDecision::Accepted(_)));
    match decision {
        RowDecision::Accepted(transaction) => {
            assert_eq!(transaction.vehicle_id, VehicleId("vehicle-1".to_string()));
            assert_eq!(transaction.unit_cost, 1.5);
        }
        _ => unreachable!("checked by matches"),
    }
}

#[test]
fn validation_errors_reject_before_transaction_creation() {
    let mut row = valid_row();
    row.quantity_liters = 0.0;
    row.total_cost = -1.0;

    let decision = classify_row(
        &row_input(row, found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert_eq!(
        decision,
        RowDecision::Rejected(RejectedRow {
            row_number: RowNumber(1),
            source_id: SourceRowId("row-1".to_string()),
            reason: RejectionReason::ValidationFailed(vec![
                ValidationError::QuantityNotPositive { value: 0.0 },
                ValidationError::CostNegative { value: -1.0 },
            ]),
        })
    );
}

#[test]
fn vehicle_not_found_is_typed_rejection() {
    let decision = classify_row(
        &row_input(
            valid_row(),
            VehicleLookupResult::NotFound {
                requested: VehicleRef("truck-1".to_string()),
            },
            DuplicateCheckResult::Unique,
        ),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(
        decision,
        RowDecision::Rejected(RejectedRow {
            reason: RejectionReason::VehicleNotFound { .. },
            ..
        })
    ));
}

#[test]
fn ambiguous_vehicle_is_typed_rejection() {
    let decision = classify_row(
        &row_input(
            valid_row(),
            VehicleLookupResult::Ambiguous {
                requested: VehicleRef("truck-1".to_string()),
                matches: vec![
                    VehicleId("vehicle-1".to_string()),
                    VehicleId("vehicle-2".to_string()),
                ],
            },
            DuplicateCheckResult::Unique,
        ),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(
        decision,
        RowDecision::Rejected(RejectedRow {
            reason: RejectionReason::AmbiguousVehicle { .. },
            ..
        })
    ));
}

#[test]
fn warnings_do_not_block_accepted_transactions() {
    let config = ValidationConfig {
        large_quantity_warning: WarningLimit::Above(10.0),
        high_unit_cost_warning: WarningLimit::Above(1.0),
        odometer_rule: OdometerRule::OptionalWarnWhenMissing,
        ..quiet_config()
    };

    let decision = classify_row(
        &row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &config,
    );

    match decision {
        RowDecision::Warning {
            transaction,
            warnings,
        } => {
            assert_eq!(transaction.source_id, SourceRowId("row-1".to_string()));
            assert_eq!(warnings.len(), 3);
            assert!(warnings.contains(&Warning::MissingOdometer));
        }
        _ => panic!("expected accepted transaction with warnings"),
    }
}

#[test]
fn quarantined_row_has_typed_reason() {
    let mut row = valid_row();
    row.merchant = Merchant::Known("Manual fuel entry".to_string());

    let decision = classify_row(
        &row_input(row, found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &quiet_config(),
    );

    match decision {
        RowDecision::Quarantined {
            transaction,
            reasons,
            ..
        } => {
            assert_eq!(transaction.source_id, SourceRowId("row-1".to_string()));
            assert!(
                reasons
                    .as_slice()
                    .contains(&QuarantineReason::SuspiciousMerchantName)
            );
        }
        _ => panic!("expected quarantined row"),
    }
}

#[test]
fn validation_error_is_rejected_not_quarantined() {
    let mut row = valid_row();
    row.quantity_liters = 0.0;
    row.merchant = Merchant::Known("manual review".to_string());

    let decision = classify_row(
        &row_input(row, found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(decision, RowDecision::Rejected(_)));
}

#[test]
fn duplicate_normal_mode_is_skipped_not_quarantined() {
    let mut row = valid_row();
    row.merchant = Merchant::Known("test merchant".to_string());

    let decision = classify_row(
        &row_input(row, found_vehicle(), finalized_duplicate()),
        UploadMode::Normal,
        &quiet_config(),
    );

    assert!(matches!(decision, RowDecision::SkippedDuplicate(_)));
}

#[test]
fn warning_does_not_become_quarantine() {
    let config = ValidationConfig {
        large_quantity_warning: WarningLimit::Above(10.0),
        high_unit_cost_warning: WarningLimit::Above(1.0),
        odometer_rule: OdometerRule::OptionalWarnWhenMissing,
        ..quiet_config()
    };

    let decision = classify_row(
        &row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique),
        UploadMode::Normal,
        &config,
    );

    assert!(matches!(decision, RowDecision::Warning { .. }));
}

#[test]
fn fatal_blocks_batch_and_suppresses_uploadable_transactions() {
    let good = row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique);
    let fatal = row_input(
        row_with_number(2),
        VehicleLookupResult::Fatal(FatalError::VehicleLookupUnavailable {
            row_number: RowNumber(2),
            message: "timeout".to_string(),
        }),
        DuplicateCheckResult::Unique,
    );

    let decision = classify_batch(&[good, fatal], UploadMode::Normal, &quiet_config());

    match &decision {
        BatchDecision::Blocked {
            rows,
            summary,
            fatal_errors,
        } => {
            assert_eq!(rows.len(), 2);
            assert_eq!(summary.accepted, 1);
            assert_eq!(summary.fatal_errors, 1);
            assert_eq!(fatal_errors.len(), 1);
        }
        BatchDecision::Ready { .. } => panic!("fatal row must block the batch"),
    }
    assert!(decision.uploadable_transactions().is_empty());
}

#[test]
fn quarantined_row_does_not_upload_or_block_and_appears_in_summary() {
    let mut quarantined = row_with_number(2);
    quarantined.quantity_liters = 33.0;

    let config = ValidationConfig {
        suspicious_quantity: 33.0,
        ..quiet_config()
    };
    let decision = classify_batch(
        &[
            row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique),
            row_input(quarantined, found_vehicle(), DuplicateCheckResult::Unique),
        ],
        UploadMode::Normal,
        &config,
    );

    match &decision {
        BatchDecision::Ready { rows, summary } => {
            assert_eq!(rows.len(), 2);
            assert_eq!(summary.accepted, 1);
            assert_eq!(summary.quarantined, 1);
            assert!(
                rows.iter()
                    .any(|row| matches!(row, RowDecision::Quarantined { reasons, .. }
                if reasons.as_slice().contains(&QuarantineReason::SuspiciousQuantityPattern)))
            );
        }
        BatchDecision::Blocked { .. } => panic!("quarantined rows must not block the batch"),
    }

    assert_eq!(decision.uploadable_transactions().len(), 1);
}

#[test]
fn summary_is_derived_from_per_row_decisions() {
    let mut warning_row = valid_row();
    warning_row.quantity_liters = 400.0;

    let config = ValidationConfig {
        large_quantity_warning: WarningLimit::Above(300.0),
        ..quiet_config()
    };
    let rejected = {
        let mut row = row_with_number(3);
        row.source_id = SourceRowId("".to_string());
        row
    };
    let inputs = vec![
        row_input(valid_row(), found_vehicle(), DuplicateCheckResult::Unique),
        row_input(warning_row, found_vehicle(), DuplicateCheckResult::Unique),
        row_input(rejected, found_vehicle(), DuplicateCheckResult::Unique),
        row_input(row_with_number(4), found_vehicle(), finalized_duplicate()),
    ];

    let decision = classify_batch(&inputs, UploadMode::Normal, &config);

    assert_eq!(
        decision.summary(),
        &Summary {
            total_rows: 4,
            accepted: 2,
            accepted_with_warnings: 1,
            quarantined: 0,
            skipped_duplicates: 1,
            rejected: 1,
            fatal_errors: 0,
            warnings: 1,
        }
    );
    assert_eq!(decision.uploadable_transactions().len(), 2);
}

#[test]
fn empty_batch_is_fatal() {
    let decision = classify_batch(&[], UploadMode::Normal, &quiet_config());

    assert!(matches!(
        decision,
        BatchDecision::Blocked {
            fatal_errors,
            ..
        } if fatal_errors == vec![FatalError::EmptyBatch]
    ));
}

fn valid_row() -> ParsedFuelRow {
    row_with_number(1)
}

fn row_with_number(row_number: u32) -> ParsedFuelRow {
    ParsedFuelRow {
        row_number: RowNumber(row_number),
        source_id: SourceRowId(format!("row-{row_number}")),
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
