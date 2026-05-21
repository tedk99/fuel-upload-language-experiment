use rust_fuel_engine::*;

#[test]
fn accepted_row_projects_accepted_audit_event() {
    let audit = project_audit(&batch(RowDecision::Accepted(transaction("row-1"))));

    assert_eq!(audit[0].kind, AuditEventKind::Accepted);
    assert_eq!(audit[0].to_dto().status, "accepted");
    assert_eq!(audit[0].source_id, Some(SourceRowId("row-1".to_string())));
}

#[test]
fn warning_row_projects_accepted_with_warnings_audit_event() {
    let audit = project_audit(&batch(RowDecision::Warning {
        transaction: transaction("row-2"),
        warnings: vec![Warning::LargeQuantity {
            quantity_liters: 90.0,
            configured_limit: 80.0,
        }],
    }));

    assert_eq!(audit[0].kind, AuditEventKind::AcceptedWithWarnings);
    assert_eq!(audit[0].warnings.len(), 1);
    assert!(audit[0].quarantine_reasons.is_empty());
}

#[test]
fn rejected_row_projects_rejected_audit_event() {
    let audit = project_audit(&batch(RowDecision::Rejected(RejectedRow {
        row_number: RowNumber(3),
        source_id: SourceRowId("row-3".to_string()),
        reason: RejectionReason::ValidationFailed(vec![ValidationError::QuantityNotPositive {
            value: 0.0,
        }]),
    })));

    assert_eq!(audit[0].kind, AuditEventKind::Rejected);
    assert!(matches!(
        audit[0].rejection,
        Some(RejectionReason::ValidationFailed(_))
    ));
}

#[test]
fn skipped_duplicate_projects_skipped_audit_event() {
    let skipped = SkippedDuplicate {
        row_number: RowNumber(4),
        source_id: SourceRowId("row-4".to_string()),
        state: DuplicateState::CanonicalFinalized {
            transaction_id: TransactionId("existing".to_string()),
        },
        mode: UploadMode::Normal,
    };

    let audit = project_audit(&batch(RowDecision::SkippedDuplicate(skipped.clone())));

    assert_eq!(audit[0].kind, AuditEventKind::SkippedDuplicate);
    assert_eq!(audit[0].duplicate_skip, Some(skipped));
}

#[test]
fn quarantined_row_projects_quarantined_audit_event_with_reasons() {
    let audit = project_audit(&batch(RowDecision::Quarantined {
        transaction: transaction("row-5"),
        reasons: QuarantineReasons::new(vec![QuarantineReason::SuspiciousMerchantName]).unwrap(),
        warnings: vec![Warning::HighUnitCost {
            unit_cost: 12.0,
            configured_limit: 10.0,
        }],
    }));

    assert_eq!(audit[0].kind, AuditEventKind::Quarantined);
    assert_eq!(
        audit[0].quarantine_reasons,
        vec![QuarantineReason::SuspiciousMerchantName]
    );
    assert_eq!(audit[0].warnings.len(), 1);
}

#[test]
fn fatal_batch_projects_fatal_audit_event() {
    let fatal = FatalError::DuplicateCheckUnavailable {
        row_number: RowNumber(6),
        message: "duplicate store timed out".to_string(),
    };

    let audit = project_audit(&BatchDecision::Blocked {
        rows: vec![RowDecision::Fatal(fatal.clone())],
        summary: Summary::default(),
        fatal_errors: vec![fatal.clone()],
    });

    assert_eq!(audit[0].kind, AuditEventKind::FatalBatch);
    assert_eq!(audit[0].fatal, Some(fatal));
    assert_eq!(audit[0].to_dto().status, "fatal_batch");
}

#[test]
fn audit_projection_does_not_recompute_classification() {
    let decision = BatchDecision::Ready {
        rows: vec![RowDecision::Accepted(transaction("row-impossible"))],
        summary: Summary {
            total_rows: 99,
            accepted: 0,
            accepted_with_warnings: 0,
            quarantined: 0,
            skipped_duplicates: 0,
            rejected: 99,
            fatal_errors: 0,
            warnings: 0,
        },
    };

    let audit = project_audit(&decision);

    assert_eq!(audit.len(), 1);
    assert_eq!(audit[0].kind, AuditEventKind::Accepted);
    assert_eq!(
        audit[0].source_id,
        Some(SourceRowId("row-impossible".to_string()))
    );
}

fn batch(row: RowDecision) -> BatchDecision {
    BatchDecision::Ready {
        rows: vec![row],
        summary: Summary::default(),
    }
}

fn transaction(source_id: &str) -> FuelTransaction {
    FuelTransaction {
        transaction_id: TransactionId(format!("{source_id}:vehicle-1")),
        row_number: RowNumber(1),
        source_id: SourceRowId(source_id.to_string()),
        vehicle_id: VehicleId("vehicle-1".to_string()),
        occurred_on: FuelDate("2026-05-21".to_string()),
        quantity_liters: 40.0,
        total_cost: 60.0,
        unit_cost: 1.5,
        odometer: OdometerReading::Missing,
        merchant: Merchant::Known("Depot".to_string()),
    }
}
