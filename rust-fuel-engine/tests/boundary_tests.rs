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

#[test]
fn valid_imported_row_maps_and_classifies() {
    let response = FuelUploadImportMapper::classify(&valid_import_request()).unwrap();

    assert_eq!(response.total_rows, 1);
    assert_eq!(response.accepted, 1);
    assert_eq!(response.decisions[0].outcome, "accepted");
}

#[test]
fn missing_required_imported_cell_returns_typed_import_error() {
    let mut request = valid_import_request();
    request.rows[0].vehicle_ref = Some(" ".to_string());

    let errors = FuelUploadImportMapper::to_application_request(&request).unwrap_err();

    assert!(errors.iter().any(|error| {
        error.code == FuelImportErrorCode::MissingRequiredCell
            && error.field == "rows[0].vehicle_ref"
    }));
}

#[test]
fn bad_numeric_imported_value_returns_typed_import_error() {
    let mut request = valid_import_request();
    request.rows[0].quantity_liters = Some("many".to_string());

    let errors = FuelUploadImportMapper::to_application_request(&request).unwrap_err();

    assert!(errors.iter().any(|error| {
        error.code == FuelImportErrorCode::InvalidNumber && error.field == "rows[0].quantity_liters"
    }));
}

#[test]
fn unknown_imported_upload_mode_returns_typed_import_error() {
    let mut request = valid_import_request();
    request.upload_mode = Some("eventual".to_string());

    let errors = FuelUploadImportMapper::to_application_request(&request).unwrap_err();

    assert!(errors.iter().any(|error| {
        error.code == FuelImportErrorCode::InvalidUploadMode && error.field == "upload_mode"
    }));
}

#[test]
fn quarantine_still_works_through_imported_input() {
    let mut request = valid_import_request();
    request.rows[0].merchant = Some("Manual fuel entry".to_string());

    let response = FuelUploadImportMapper::classify(&request).unwrap();

    assert_eq!(response.quarantined, 1);
    assert_eq!(response.decisions[0].outcome, "quarantined");
}

#[test]
fn import_mapper_does_not_recompute_summary_independently() {
    let response = FuelUploadImportMapper::classify(&valid_import_request()).unwrap();

    assert_eq!(response.total_rows, 1);
    assert_eq!(response.accepted, 1);
    assert_eq!(response.uploadable_transactions, 1);
}

#[test]
fn repository_vehicle_match_leads_to_normal_classification() {
    let response = repository_service(
        Ok(VehicleLookupResult::Found(Vehicle {
            id: VehicleId("vehicle-1".to_string()),
            reference: VehicleRef("truck-1".to_string()),
        })),
        Ok(DuplicateCheckResult::Unique),
    )
    .classify(&repository_request())
    .unwrap();

    assert_eq!(response.decisions[0].outcome, "accepted");
    assert_eq!(
        response.decisions[0].transaction_vehicle_id,
        Some("vehicle-1".to_string())
    );
}

#[test]
fn repository_missing_vehicle_uses_existing_not_found_behavior() {
    let response = repository_service(
        Ok(VehicleLookupResult::NotFound {
            requested: VehicleRef("truck-1".to_string()),
        }),
        Ok(DuplicateCheckResult::Unique),
    )
    .classify(&repository_request())
    .unwrap();

    assert_eq!(response.decisions[0].outcome, "rejected");
    assert!(
        response.decisions[0]
            .rejection
            .as_ref()
            .is_some_and(|reason| reason.contains("VehicleNotFound"))
    );
}

#[test]
fn repository_duplicate_state_leads_to_skipped_duplicate() {
    let response = repository_service(
        Ok(VehicleLookupResult::Found(Vehicle {
            id: VehicleId("vehicle-1".to_string()),
            reference: VehicleRef("truck-1".to_string()),
        })),
        Ok(DuplicateCheckResult::Duplicate(
            DuplicateState::CanonicalFinalized {
                transaction_id: TransactionId("existing".to_string()),
            },
        )),
    )
    .classify(&repository_request())
    .unwrap();

    assert_eq!(response.decisions[0].outcome, "skipped_duplicate");
    assert_eq!(response.skipped_duplicates, 1);
}

#[test]
fn repository_failure_is_typed_and_not_a_validation_error() {
    let repository_error = VehicleRepositoryError::TimedOut {
        message: "vehicle store timed out".to_string(),
    };
    let response = repository_service(
        Err(repository_error.clone()),
        Ok(DuplicateCheckResult::Unique),
    )
    .classify(&repository_request())
    .unwrap();

    assert_eq!(
        repository_error,
        VehicleRepositoryError::TimedOut {
            message: "vehicle store timed out".to_string()
        }
    );
    assert_eq!(response.decisions[0].outcome, "fatal");
    assert!(response.decisions[0].fatal.is_some());
    assert!(response.decisions[0].rejection.is_none());
}

#[test]
fn quarantine_still_works_with_repository_backed_service() {
    let mut request = repository_request();
    request.rows[0].merchant = Some("Manual fuel entry".to_string());

    let response = repository_service(
        Ok(VehicleLookupResult::Found(Vehicle {
            id: VehicleId("vehicle-1".to_string()),
            reference: VehicleRef("truck-1".to_string()),
        })),
        Ok(DuplicateCheckResult::Unique),
    )
    .classify(&request)
    .unwrap();

    assert_eq!(response.decisions[0].outcome, "quarantined");
    assert_eq!(response.quarantined, 1);
}

#[test]
fn repository_backed_service_summary_matches_application_summary() {
    let repository_response = repository_service(
        Ok(VehicleLookupResult::Found(Vehicle {
            id: VehicleId("vehicle-1".to_string()),
            reference: VehicleRef("truck-1".to_string()),
        })),
        Ok(DuplicateCheckResult::Unique),
    )
    .classify(&repository_request())
    .unwrap();
    let application_response = FuelUploadApplicationService::classify(&valid_request()).unwrap();

    assert_eq!(
        repository_response.total_rows,
        application_response.total_rows
    );
    assert_eq!(repository_response.accepted, application_response.accepted);
    assert_eq!(
        repository_response.uploadable_transactions,
        application_response.uploadable_transactions
    );
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

fn repository_request() -> FuelUploadRequestDto {
    let mut request = valid_request();
    request.rows[0].vehicle_lookup_status = String::new();
    request.rows[0].vehicle_id = None;
    request.rows[0].duplicate_status = String::new();
    request.rows[0].transaction_id = None;
    request
}

fn repository_service(
    vehicle_result: Result<VehicleLookupResult, VehicleRepositoryError>,
    duplicate_result: Result<DuplicateCheckResult, DuplicateRepositoryError>,
) -> RepositoryFuelUploadApplicationService<'static, FakeVehicleRepository, FakeDuplicateRepository>
{
    let vehicle_repository = Box::leak(Box::new(FakeVehicleRepository { vehicle_result }));
    let duplicate_repository = Box::leak(Box::new(FakeDuplicateRepository { duplicate_result }));
    RepositoryFuelUploadApplicationService::new(vehicle_repository, duplicate_repository)
}

fn valid_import_request() -> ImportBatchRequest {
    ImportBatchRequest {
        upload_mode: Some("normal".to_string()),
        validation: ImportedFuelValidation {
            cost_rule: Some("zero_or_positive".to_string()),
            odometer_rule: Some("optional".to_string()),
            large_quantity_warning: None,
            high_unit_cost_warning: None,
            suspicious_quantity: Some("333333".to_string()),
            suspicious_total_cost: Some("333333".to_string()),
        },
        rows: vec![valid_raw_row(1)],
    }
}

fn valid_raw_row(row_number: u32) -> RawFuelUploadRow {
    RawFuelUploadRow {
        row_number: Some(row_number.to_string()),
        source_id: Some(format!("row-{row_number}")),
        vehicle_ref: Some("truck-1".to_string()),
        occurred_on: Some("2026-05-21".to_string()),
        quantity_liters: Some("40".to_string()),
        total_cost: Some("60".to_string()),
        odometer: None,
        merchant: Some("Depot".to_string()),
        vehicle_lookup_status: Some("found".to_string()),
        vehicle_id: Some("vehicle-1".to_string()),
        ambiguous_vehicle_ids: Vec::new(),
        vehicle_lookup_error: None,
        duplicate_status: Some("unique".to_string()),
        duplicate_state: None,
        transaction_id: None,
        attempt_id: None,
        duplicate_error: None,
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

struct FakeVehicleRepository {
    vehicle_result: Result<VehicleLookupResult, VehicleRepositoryError>,
}

impl VehicleRepository for FakeVehicleRepository {
    fn lookup(
        &self,
        _reference: &VehicleRef,
    ) -> Result<VehicleLookupResult, VehicleRepositoryError> {
        self.vehicle_result.clone()
    }
}

struct FakeDuplicateRepository {
    duplicate_result: Result<DuplicateCheckResult, DuplicateRepositoryError>,
}

impl DuplicateRepository for FakeDuplicateRepository {
    fn lookup(
        &self,
        _lookup: &DuplicateLookup,
    ) -> Result<DuplicateCheckResult, DuplicateRepositoryError> {
        self.duplicate_result.clone()
    }
}
