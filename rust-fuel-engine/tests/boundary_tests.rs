use std::convert::TryFrom;

use rust_fuel_engine::*;

#[test]
fn valid_dto_maps_and_classifies() {
    let response = FuelUploadApplicationService::classify(&valid_request()).unwrap();

    assert_eq!(response.total_rows, 1);
    assert_eq!(response.accepted, 1);
    assert_eq!(response.decisions[0].outcome, "accepted");
    assert_eq!(
        response.decisions[0].transaction_vehicle_id,
        Some("vehicle-1".to_string())
    );
}

#[test]
fn invalid_dto_returns_typed_mapping_error() {
    let mut request = valid_request();
    request.upload_mode = "eventual".to_string();

    let errors = FuelUploadDomainRequest::try_from(&request).unwrap_err();

    assert!(errors.iter().any(|error| {
        error.code == FuelUploadMappingErrorCode::InvalidUploadMode && error.field == "upload_mode"
    }));
}

#[test]
fn response_dto_represents_all_decision_shapes() {
    let mut request = valid_request();
    request.validation.large_quantity_warning = Some(300.0);
    request.validation.suspicious_quantity = 33.0;
    request.rows = vec![
        valid_row(1),
        FuelUploadRowDto {
            row_number: 2,
            source_id: "row-2".to_string(),
            quantity_liters: 400.0,
            ..valid_row(2)
        },
        FuelUploadRowDto {
            row_number: 3,
            source_id: "row-3".to_string(),
            quantity_liters: 33.0,
            ..valid_row(3)
        },
        FuelUploadRowDto {
            row_number: 4,
            source_id: "row-4".to_string(),
            duplicate_status: "duplicate".to_string(),
            duplicate_state: Some("canonical_finalized".to_string()),
            transaction_id: Some("txn-existing".to_string()),
            ..valid_row(4)
        },
        FuelUploadRowDto {
            row_number: 5,
            source_id: String::new(),
            ..valid_row(5)
        },
        FuelUploadRowDto {
            row_number: 6,
            source_id: "row-6".to_string(),
            duplicate_status: "fatal".to_string(),
            duplicate_error: Some("duplicate store timed out".to_string()),
            ..valid_row(6)
        },
    ];

    let response = FuelUploadApplicationService::classify(&request).unwrap();
    let outcomes: Vec<&str> = response
        .decisions
        .iter()
        .map(|decision| decision.outcome.as_str())
        .collect();

    assert!(outcomes.contains(&"accepted"));
    assert!(outcomes.contains(&"accepted_with_warnings"));
    assert!(outcomes.contains(&"quarantined"));
    assert!(outcomes.contains(&"skipped_duplicate"));
    assert!(outcomes.contains(&"rejected"));
    assert!(outcomes.contains(&"fatal"));
}

#[test]
fn response_summary_uses_domain_summary_without_recomputing() {
    let decision = BatchDecision::Ready {
        rows: vec![RowDecision::Accepted(FuelTransaction {
            source_id: SourceRowId("row-1".to_string()),
            vehicle_id: VehicleId("vehicle-1".to_string()),
            occurred_on: FuelDate("2026-05-21".to_string()),
            quantity_liters: 10.0,
            total_cost: 20.0,
            unit_cost: 2.0,
            odometer: OdometerReading::Missing,
            merchant: Merchant::Known("Depot".to_string()),
        })],
        summary: Summary {
            total_rows: 99,
            accepted: 42,
            accepted_with_warnings: 5,
            quarantined: 4,
            skipped_duplicates: 3,
            rejected: 2,
            fatal_errors: 1,
            warnings: 7,
        },
    };

    let response = FuelUploadResponseDto::from(&decision);

    assert_eq!(response.total_rows, 99);
    assert_eq!(response.accepted, 42);
    assert_eq!(response.quarantined, 4);
    assert_eq!(response.warnings, 7);
}

fn valid_request() -> FuelUploadRequestDto {
    FuelUploadRequestDto {
        upload_mode: "normal".to_string(),
        validation: FuelUploadValidationDto {
            cost_rule: "zero_or_positive".to_string(),
            odometer_rule: "optional".to_string(),
            large_quantity_warning: None,
            high_unit_cost_warning: None,
            suspicious_quantity: 333_333.0,
            suspicious_total_cost: 333_333.0,
        },
        rows: vec![valid_row(1)],
    }
}

fn valid_row(row_number: u32) -> FuelUploadRowDto {
    FuelUploadRowDto {
        row_number,
        source_id: format!("row-{row_number}"),
        vehicle_ref: "truck-1".to_string(),
        occurred_on: "2026-05-21".to_string(),
        quantity_liters: 40.0,
        total_cost: 60.0,
        odometer: None,
        merchant: Some("Depot".to_string()),
        vehicle_lookup_status: "found".to_string(),
        vehicle_id: Some("vehicle-1".to_string()),
        ambiguous_vehicle_ids: Vec::new(),
        vehicle_lookup_error: None,
        duplicate_status: "unique".to_string(),
        duplicate_state: None,
        transaction_id: None,
        attempt_id: None,
        duplicate_error: None,
    }
}
