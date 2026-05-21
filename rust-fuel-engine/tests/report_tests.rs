use rust_fuel_engine::*;

#[test]
fn operational_report_includes_decision_derived_lists() {
    let rows = vec![
        RowDecision::Accepted(transaction("txn-1", 1)),
        RowDecision::Warning {
            transaction: transaction("txn-2", 2),
            warnings: vec![Warning::LargeQuantity {
                quantity_liters: 80.0,
                configured_limit: 60.0,
            }],
        },
        RowDecision::Rejected(RejectedRow {
            row_number: RowNumber(3),
            source_id: SourceRowId("row-3".to_string()),
            reason: RejectionReason::ValidationFailed(vec![ValidationError::EmptyMerchant]),
        }),
        RowDecision::SkippedDuplicate(SkippedDuplicate {
            row_number: RowNumber(4),
            source_id: SourceRowId("row-4".to_string()),
            state: DuplicateState::CanonicalFinalized {
                transaction_id: TransactionId("existing".to_string()),
            },
            mode: UploadMode::Normal,
        }),
        RowDecision::Quarantined {
            transaction: transaction("txn-quarantine", 5),
            reasons: QuarantineReasons::new(vec![QuarantineReason::SuspiciousMerchantName])
                .expect("non-empty reasons"),
            warnings: Vec::new(),
        },
    ];
    let decision = BatchDecision::Ready {
        summary: Summary::from_rows(&rows),
        rows,
    };

    let report = project_operational_report(&decision);

    assert_eq!(report.status, OperationalBatchStatus::Ready);
    assert_eq!(
        report.uploaded_transaction_ids,
        vec![
            TransactionId("txn-1".to_string()),
            TransactionId("txn-2".to_string())
        ]
    );
    assert_eq!(report.rejected_row_numbers, vec![RowNumber(3)]);
    assert_eq!(report.skipped_duplicate_row_numbers, vec![RowNumber(4)]);
    assert_eq!(
        report.quarantined_rows,
        vec![OperationalQuarantinedRow {
            row_number: RowNumber(5),
            reasons: vec![QuarantineReason::SuspiciousMerchantName],
        }]
    );
}

#[test]
fn fatal_operational_report_has_fatal_status_and_no_uploaded_transactions() {
    let fatal = FatalError::DuplicateCheckUnavailable {
        row_number: RowNumber(2),
        message: "duplicate service down".to_string(),
    };
    let rows = vec![
        RowDecision::Accepted(transaction("txn-blocked", 1)),
        RowDecision::Fatal(fatal.clone()),
    ];
    let decision = BatchDecision::Blocked {
        summary: Summary::from_rows(&rows),
        rows,
        fatal_errors: vec![fatal.clone()],
    };

    let report = project_operational_report(&decision);

    assert_eq!(report.status, OperationalBatchStatus::Fatal);
    assert!(report.uploaded_transaction_ids.is_empty());
    assert_eq!(report.fatal_errors, vec![fatal]);
}

#[test]
fn operational_report_counts_match_summary_and_projector_does_not_inspect_raw_rows() {
    let rows = vec![RowDecision::Accepted(transaction("txn-decision-only", 99))];
    let summary = Summary {
        total_rows: 99,
        accepted: 42,
        accepted_with_warnings: 5,
        quarantined: 4,
        skipped_duplicates: 3,
        rejected: 2,
        fatal_errors: 0,
        warnings: 7,
    };
    let decision = BatchDecision::Ready {
        rows,
        summary: summary.clone(),
    };

    let report = project_operational_report(&decision);

    assert_eq!(report.counts, summary);
    assert_eq!(
        report.uploaded_transaction_ids,
        vec![TransactionId("txn-decision-only".to_string())]
    );
}

fn transaction(transaction_id: &str, row_number: u32) -> FuelTransaction {
    FuelTransaction {
        transaction_id: TransactionId(transaction_id.to_string()),
        row_number: RowNumber(row_number),
        source_id: SourceRowId(format!("row-{row_number}")),
        vehicle_id: VehicleId("vehicle-1".to_string()),
        occurred_on: FuelDate("2026-05-21".to_string()),
        quantity_liters: 40.0,
        total_cost: 60.0,
        unit_cost: 1.5,
        odometer: OdometerReading::Missing,
        merchant: Merchant::Known("Depot".to_string()),
    }
}
