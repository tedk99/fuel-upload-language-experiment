use std::convert::TryFrom;

use crate::domain::*;
use crate::engine::classify_batch;

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadRequestDto {
    pub upload_mode: String,
    pub validation: FuelUploadValidationDto,
    pub rows: Vec<FuelUploadRowDto>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ImportBatchRequest {
    pub upload_mode: Option<String>,
    pub validation: ImportedFuelValidation,
    pub rows: Vec<RawFuelUploadRow>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ImportedFuelValidation {
    pub cost_rule: Option<String>,
    pub odometer_rule: Option<String>,
    pub large_quantity_warning: Option<String>,
    pub high_unit_cost_warning: Option<String>,
    pub suspicious_quantity: Option<String>,
    pub suspicious_total_cost: Option<String>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct RawFuelUploadRow {
    pub row_number: Option<String>,
    pub source_id: Option<String>,
    pub vehicle_ref: Option<String>,
    pub occurred_on: Option<String>,
    pub quantity_liters: Option<String>,
    pub total_cost: Option<String>,
    pub odometer: Option<String>,
    pub merchant: Option<String>,
    pub vehicle_lookup_status: Option<String>,
    pub vehicle_id: Option<String>,
    pub ambiguous_vehicle_ids: Vec<String>,
    pub vehicle_lookup_error: Option<String>,
    pub duplicate_status: Option<String>,
    pub duplicate_state: Option<String>,
    pub transaction_id: Option<String>,
    pub attempt_id: Option<String>,
    pub duplicate_error: Option<String>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadValidationDto {
    pub cost_rule: String,
    pub odometer_rule: String,
    pub large_quantity_warning: Option<f64>,
    pub high_unit_cost_warning: Option<f64>,
    pub suspicious_quantity: f64,
    pub suspicious_total_cost: f64,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadRowDto {
    pub row_number: u32,
    pub source_id: String,
    pub vehicle_ref: String,
    pub occurred_on: String,
    pub quantity_liters: f64,
    pub total_cost: f64,
    pub odometer: Option<u32>,
    pub merchant: Option<String>,
    pub vehicle_lookup_status: String,
    pub vehicle_id: Option<String>,
    pub ambiguous_vehicle_ids: Vec<String>,
    pub vehicle_lookup_error: Option<String>,
    pub duplicate_status: String,
    pub duplicate_state: Option<String>,
    pub transaction_id: Option<String>,
    pub attempt_id: Option<String>,
    pub duplicate_error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FuelUploadMappingErrorCode {
    MissingRequiredField,
    InvalidUploadMode,
    InvalidCostRule,
    InvalidOdometerRule,
    InvalidVehicleLookupStatus,
    MissingVehicleLookupPayload,
    InvalidDuplicateStatus,
    MissingDuplicatePayload,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FuelUploadMappingError {
    pub code: FuelUploadMappingErrorCode,
    pub field: String,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FuelImportErrorCode {
    MissingRows,
    MissingRequiredCell,
    InvalidNumber,
    InvalidDate,
    InvalidUploadMode,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FuelImportError {
    pub code: FuelImportErrorCode,
    pub field: String,
    pub detail: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadDomainRequest {
    pub rows: Vec<RowInput>,
    pub mode: UploadMode,
    pub config: ValidationConfig,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadResponseDto {
    pub decisions: Vec<FuelUploadDecisionDto>,
    pub total_rows: usize,
    pub accepted: usize,
    pub accepted_with_warnings: usize,
    pub quarantined: usize,
    pub skipped_duplicates: usize,
    pub rejected: usize,
    pub fatal_errors: usize,
    pub warnings: usize,
    pub uploadable_transactions: usize,
    pub blocked: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelUploadDecisionDto {
    pub source_id: Option<String>,
    pub outcome: String,
    pub transaction_vehicle_id: Option<String>,
    pub warnings: Vec<String>,
    pub quarantine_reasons: Vec<String>,
    pub rejection: Option<String>,
    pub duplicate_skip: Option<String>,
    pub fatal: Option<String>,
}

pub struct FuelUploadApplicationService;

impl FuelUploadApplicationService {
    pub fn classify(
        request: &FuelUploadRequestDto,
    ) -> Result<FuelUploadResponseDto, Vec<FuelUploadMappingError>> {
        let domain = FuelUploadDomainRequest::try_from(request)?;
        let decision = classify_batch(&domain.rows, domain.mode, &domain.config);
        Ok(FuelUploadResponseDto::from(&decision))
    }
}

pub trait VehicleRepository {
    fn lookup(&self, reference: &VehicleRef)
    -> Result<VehicleLookupResult, VehicleRepositoryError>;
}

pub trait DuplicateRepository {
    fn lookup(
        &self,
        lookup: &DuplicateLookup,
    ) -> Result<DuplicateCheckResult, DuplicateRepositoryError>;
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DuplicateLookup {
    pub row_number: RowNumber,
    pub source_id: SourceRowId,
    pub vehicle_ref: VehicleRef,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum VehicleRepositoryError {
    Unavailable { message: String },
    TimedOut { message: String },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DuplicateRepositoryError {
    Unavailable { message: String },
    TimedOut { message: String },
}

pub struct RepositoryFuelUploadApplicationService<'a, V, D>
where
    V: VehicleRepository,
    D: DuplicateRepository,
{
    vehicle_repository: &'a V,
    duplicate_repository: &'a D,
}

impl<'a, V, D> RepositoryFuelUploadApplicationService<'a, V, D>
where
    V: VehicleRepository,
    D: DuplicateRepository,
{
    pub fn new(vehicle_repository: &'a V, duplicate_repository: &'a D) -> Self {
        Self {
            vehicle_repository,
            duplicate_repository,
        }
    }

    pub fn classify(
        &self,
        request: &FuelUploadRequestDto,
    ) -> Result<FuelUploadResponseDto, Vec<FuelUploadMappingError>> {
        let resolved = FuelUploadRequestDto {
            upload_mode: request.upload_mode.clone(),
            validation: request.validation.clone(),
            rows: request
                .rows
                .iter()
                .map(|row| self.resolve_row(row))
                .collect(),
        };

        FuelUploadApplicationService::classify(&resolved)
    }

    fn resolve_row(&self, row: &FuelUploadRowDto) -> FuelUploadRowDto {
        let vehicle_ref = VehicleRef(row.vehicle_ref.trim().to_string());
        let vehicle_lookup = self.vehicle_repository.lookup(&vehicle_ref);
        let duplicate_lookup = match &vehicle_lookup {
            Err(_) => Ok(DuplicateCheckResult::Unique),
            Ok(_) => self.duplicate_repository.lookup(&DuplicateLookup {
                row_number: RowNumber(row.row_number),
                source_id: SourceRowId(row.source_id.trim().to_string()),
                vehicle_ref,
            }),
        };

        let mut resolved = row.clone();
        apply_vehicle_lookup(&mut resolved, vehicle_lookup);
        apply_duplicate_lookup(&mut resolved, duplicate_lookup);
        resolved
    }
}

pub struct FuelUploadImportMapper;

impl FuelUploadImportMapper {
    pub fn to_application_request(
        request: &ImportBatchRequest,
    ) -> Result<FuelUploadRequestDto, Vec<FuelImportError>> {
        FuelUploadRequestDto::try_from(request)
    }

    pub fn classify(
        request: &ImportBatchRequest,
    ) -> Result<FuelUploadResponseDto, Vec<FuelImportError>> {
        let application_request = FuelUploadRequestDto::try_from(request)?;
        FuelUploadApplicationService::classify(&application_request)
            .map_err(|errors| errors.into_iter().map(FuelImportError::from).collect())
    }
}

impl TryFrom<&ImportBatchRequest> for FuelUploadRequestDto {
    type Error = Vec<FuelImportError>;

    fn try_from(value: &ImportBatchRequest) -> Result<Self, Self::Error> {
        let upload_mode = parse_import_upload_mode(value.upload_mode.as_deref(), "upload_mode");
        let suspicious_quantity = parse_required_f64(
            value.validation.suspicious_quantity.as_deref(),
            "validation.suspicious_quantity",
        );
        let suspicious_total_cost = parse_required_f64(
            value.validation.suspicious_total_cost.as_deref(),
            "validation.suspicious_total_cost",
        );

        let mapped_rows: Vec<Result<FuelUploadRowDto, Vec<FuelImportError>>> = value
            .rows
            .iter()
            .enumerate()
            .map(|(index, row)| FuelUploadRowDto::try_from((index, row)))
            .collect();

        let mut errors: Vec<FuelImportError> = upload_mode
            .as_ref()
            .err()
            .into_iter()
            .chain(suspicious_quantity.as_ref().err())
            .chain(suspicious_total_cost.as_ref().err())
            .flat_map(|errors| errors.iter().cloned())
            .chain(mapped_rows.iter().flat_map(|result| match result {
                Ok(_) => Vec::new(),
                Err(errors) => errors.clone(),
            }))
            .collect();

        if value.rows.is_empty() {
            errors.push(import_error(
                FuelImportErrorCode::MissingRows,
                "rows",
                "Rows are required.",
            ));
        }

        if !errors.is_empty() {
            return Err(errors);
        }

        Ok(Self {
            upload_mode: upload_mode.expect("checked errors"),
            validation: FuelUploadValidationDto {
                cost_rule: optional_cell(value.validation.cost_rule.as_deref())
                    .unwrap_or_else(|| "zero_or_positive".to_string()),
                odometer_rule: optional_cell(value.validation.odometer_rule.as_deref())
                    .unwrap_or_else(|| "optional".to_string()),
                large_quantity_warning: parse_optional_f64(
                    value.validation.large_quantity_warning.as_deref(),
                    "validation.large_quantity_warning",
                )?,
                high_unit_cost_warning: parse_optional_f64(
                    value.validation.high_unit_cost_warning.as_deref(),
                    "validation.high_unit_cost_warning",
                )?,
                suspicious_quantity: suspicious_quantity.expect("checked errors"),
                suspicious_total_cost: suspicious_total_cost.expect("checked errors"),
            },
            rows: mapped_rows.into_iter().filter_map(Result::ok).collect(),
        })
    }
}

impl TryFrom<(usize, &RawFuelUploadRow)> for FuelUploadRowDto {
    type Error = Vec<FuelImportError>;

    fn try_from((index, value): (usize, &RawFuelUploadRow)) -> Result<Self, Self::Error> {
        let prefix = format!("rows[{index}]");
        let row_number =
            parse_required_u32(value.row_number.as_deref(), &format!("{prefix}.row_number"));
        let source_id =
            require_import_cell(value.source_id.as_deref(), &format!("{prefix}.source_id"));
        let vehicle_ref = require_import_cell(
            value.vehicle_ref.as_deref(),
            &format!("{prefix}.vehicle_ref"),
        );
        let occurred_on = parse_import_date(
            value.occurred_on.as_deref(),
            &format!("{prefix}.occurred_on"),
        );
        let quantity = parse_required_f64(
            value.quantity_liters.as_deref(),
            &format!("{prefix}.quantity_liters"),
        );
        let total_cost =
            parse_required_f64(value.total_cost.as_deref(), &format!("{prefix}.total_cost"));
        let odometer = parse_optional_u32(value.odometer.as_deref(), &format!("{prefix}.odometer"));
        let merchant = optional_cell(value.merchant.as_deref());
        let vehicle_lookup_status = require_import_cell(
            value.vehicle_lookup_status.as_deref(),
            &format!("{prefix}.vehicle_lookup_status"),
        );
        let duplicate_status = require_import_cell(
            value.duplicate_status.as_deref(),
            &format!("{prefix}.duplicate_status"),
        );

        let errors: Vec<FuelImportError> = row_number
            .as_ref()
            .err()
            .into_iter()
            .chain(source_id.as_ref().err())
            .chain(vehicle_ref.as_ref().err())
            .chain(occurred_on.as_ref().err())
            .chain(quantity.as_ref().err())
            .chain(total_cost.as_ref().err())
            .chain(odometer.as_ref().err())
            .chain(vehicle_lookup_status.as_ref().err())
            .chain(duplicate_status.as_ref().err())
            .flat_map(|errors| errors.iter().cloned())
            .collect();

        if !errors.is_empty() {
            return Err(errors);
        }

        Ok(Self {
            row_number: row_number.expect("checked errors"),
            source_id: source_id.expect("checked errors"),
            vehicle_ref: vehicle_ref.expect("checked errors"),
            occurred_on: occurred_on.expect("checked errors"),
            quantity_liters: quantity.expect("checked errors"),
            total_cost: total_cost.expect("checked errors"),
            odometer: odometer.expect("checked errors"),
            merchant,
            vehicle_lookup_status: vehicle_lookup_status.expect("checked errors"),
            vehicle_id: value.vehicle_id.clone(),
            ambiguous_vehicle_ids: value.ambiguous_vehicle_ids.clone(),
            vehicle_lookup_error: value.vehicle_lookup_error.clone(),
            duplicate_status: duplicate_status.expect("checked errors"),
            duplicate_state: value.duplicate_state.clone(),
            transaction_id: value.transaction_id.clone(),
            attempt_id: value.attempt_id.clone(),
            duplicate_error: value.duplicate_error.clone(),
        })
    }
}

impl TryFrom<&FuelUploadRequestDto> for FuelUploadDomainRequest {
    type Error = Vec<FuelUploadMappingError>;

    fn try_from(value: &FuelUploadRequestDto) -> Result<Self, Self::Error> {
        let mode = parse_upload_mode(&value.upload_mode, "upload_mode");
        let cost_rule = parse_cost_rule(&value.validation.cost_rule, "validation.cost_rule");
        let odometer_rule =
            parse_odometer_rule(&value.validation.odometer_rule, "validation.odometer_rule");

        let mapped_rows: Vec<Result<RowInput, Vec<FuelUploadMappingError>>> = value
            .rows
            .iter()
            .enumerate()
            .map(|(index, row)| RowInput::try_from((index, row)))
            .collect();

        let errors: Vec<FuelUploadMappingError> = mode
            .as_ref()
            .err()
            .into_iter()
            .chain(cost_rule.as_ref().err())
            .chain(odometer_rule.as_ref().err())
            .flat_map(|errors| errors.iter().cloned())
            .chain(mapped_rows.iter().flat_map(|result| match result {
                Ok(_) => Vec::new(),
                Err(errors) => errors.clone(),
            }))
            .collect();

        if !errors.is_empty() {
            return Err(errors);
        }

        Ok(Self {
            rows: mapped_rows
                .into_iter()
                .filter_map(Result::ok)
                .collect::<Vec<_>>(),
            mode: mode.expect("checked errors"),
            config: ValidationConfig {
                cost_rule: cost_rule.expect("checked errors"),
                odometer_rule: odometer_rule.expect("checked errors"),
                large_quantity_warning: warning_limit(value.validation.large_quantity_warning),
                high_unit_cost_warning: warning_limit(value.validation.high_unit_cost_warning),
                suspicious_quantity: value.validation.suspicious_quantity,
                suspicious_total_cost: value.validation.suspicious_total_cost,
            },
        })
    }
}

impl TryFrom<(usize, &FuelUploadRowDto)> for RowInput {
    type Error = Vec<FuelUploadMappingError>;

    fn try_from((index, value): (usize, &FuelUploadRowDto)) -> Result<Self, Self::Error> {
        let prefix = format!("rows[{index}]");
        let vehicle_lookup = parse_vehicle_lookup(value, &prefix);
        let duplicate_check = parse_duplicate_check(value, &prefix);

        let errors: Vec<FuelUploadMappingError> = vehicle_lookup
            .as_ref()
            .err()
            .into_iter()
            .chain(duplicate_check.as_ref().err())
            .flat_map(|errors| errors.iter().cloned())
            .collect();

        if !errors.is_empty() {
            return Err(errors);
        }

        Ok(Self {
            row: ParsedFuelRow {
                row_number: RowNumber(value.row_number),
                source_id: SourceRowId(value.source_id.trim().to_string()),
                vehicle_ref: VehicleRef(value.vehicle_ref.trim().to_string()),
                occurred_on: FuelDate(value.occurred_on.trim().to_string()),
                quantity_liters: value.quantity_liters,
                total_cost: value.total_cost,
                odometer: value
                    .odometer
                    .map(OdometerReading::Known)
                    .unwrap_or(OdometerReading::Missing),
                merchant: value
                    .merchant
                    .as_ref()
                    .filter(|merchant| !merchant.trim().is_empty())
                    .map(|merchant| Merchant::Known(merchant.trim().to_string()))
                    .unwrap_or(Merchant::Missing),
            },
            vehicle_lookup: vehicle_lookup.expect("checked errors"),
            duplicate_check: duplicate_check.expect("checked errors"),
        })
    }
}

impl From<&BatchDecision> for FuelUploadResponseDto {
    fn from(value: &BatchDecision) -> Self {
        Self {
            decisions: value
                .rows()
                .iter()
                .map(FuelUploadDecisionDto::from)
                .collect(),
            total_rows: value.summary().total_rows,
            accepted: value.summary().accepted,
            accepted_with_warnings: value.summary().accepted_with_warnings,
            quarantined: value.summary().quarantined,
            skipped_duplicates: value.summary().skipped_duplicates,
            rejected: value.summary().rejected,
            fatal_errors: value.summary().fatal_errors,
            warnings: value.summary().warnings,
            uploadable_transactions: value.uploadable_transactions().len(),
            blocked: matches!(value, BatchDecision::Blocked { .. }),
        }
    }
}

impl From<&RowDecision> for FuelUploadDecisionDto {
    fn from(value: &RowDecision) -> Self {
        match value {
            RowDecision::Accepted(transaction) => {
                transaction_decision("accepted", transaction, &[], &[])
            }
            RowDecision::Warning {
                transaction,
                warnings,
            } => transaction_decision("accepted_with_warnings", transaction, warnings, &[]),
            RowDecision::Quarantined {
                transaction,
                reasons,
                warnings,
            } => transaction_decision(
                "quarantined",
                transaction,
                warnings,
                &reasons
                    .as_slice()
                    .iter()
                    .map(|reason| format!("{reason:?}"))
                    .collect::<Vec<_>>(),
            ),
            RowDecision::SkippedDuplicate(skipped) => Self {
                source_id: Some(skipped.source_id.0.clone()),
                outcome: "skipped_duplicate".to_string(),
                transaction_vehicle_id: None,
                warnings: Vec::new(),
                quarantine_reasons: Vec::new(),
                rejection: None,
                duplicate_skip: Some(format!("{:?}", skipped.state)),
                fatal: None,
            },
            RowDecision::Rejected(rejected) => Self {
                source_id: Some(rejected.source_id.0.clone()),
                outcome: "rejected".to_string(),
                transaction_vehicle_id: None,
                warnings: Vec::new(),
                quarantine_reasons: Vec::new(),
                rejection: Some(format!("{:?}", rejected.reason)),
                duplicate_skip: None,
                fatal: None,
            },
            RowDecision::Fatal(fatal) => Self {
                source_id: None,
                outcome: "fatal".to_string(),
                transaction_vehicle_id: None,
                warnings: Vec::new(),
                quarantine_reasons: Vec::new(),
                rejection: None,
                duplicate_skip: None,
                fatal: Some(fatal.to_string()),
            },
        }
    }
}

fn transaction_decision(
    outcome: &str,
    transaction: &FuelTransaction,
    warnings: &[Warning],
    quarantine_reasons: &[String],
) -> FuelUploadDecisionDto {
    FuelUploadDecisionDto {
        source_id: Some(transaction.source_id.0.clone()),
        outcome: outcome.to_string(),
        transaction_vehicle_id: Some(transaction.vehicle_id.0.clone()),
        warnings: warnings
            .iter()
            .map(|warning| format!("{warning:?}"))
            .collect(),
        quarantine_reasons: quarantine_reasons.to_vec(),
        rejection: None,
        duplicate_skip: None,
        fatal: None,
    }
}

fn apply_vehicle_lookup(
    row: &mut FuelUploadRowDto,
    result: Result<VehicleLookupResult, VehicleRepositoryError>,
) {
    match result {
        Ok(VehicleLookupResult::Found(vehicle)) => {
            row.vehicle_lookup_status = "found".to_string();
            row.vehicle_id = Some(vehicle.id.0);
            row.ambiguous_vehicle_ids = Vec::new();
            row.vehicle_lookup_error = None;
        }
        Ok(VehicleLookupResult::NotFound { .. }) => {
            row.vehicle_lookup_status = "not_found".to_string();
            row.vehicle_id = None;
            row.ambiguous_vehicle_ids = Vec::new();
            row.vehicle_lookup_error = None;
        }
        Ok(VehicleLookupResult::Ambiguous { matches, .. }) => {
            row.vehicle_lookup_status = "ambiguous".to_string();
            row.vehicle_id = None;
            row.ambiguous_vehicle_ids = matches.into_iter().map(|id| id.0).collect();
            row.vehicle_lookup_error = None;
        }
        Ok(VehicleLookupResult::Fatal(fatal)) => {
            row.vehicle_lookup_status = "fatal".to_string();
            row.vehicle_id = None;
            row.ambiguous_vehicle_ids = Vec::new();
            row.vehicle_lookup_error = Some(fatal.to_string());
        }
        Err(error) => {
            row.vehicle_lookup_status = "fatal".to_string();
            row.vehicle_id = None;
            row.ambiguous_vehicle_ids = Vec::new();
            row.vehicle_lookup_error = Some(vehicle_repository_error_message(&error).to_string());
        }
    }
}

fn apply_duplicate_lookup(
    row: &mut FuelUploadRowDto,
    result: Result<DuplicateCheckResult, DuplicateRepositoryError>,
) {
    match result {
        Ok(DuplicateCheckResult::Unique) => {
            row.duplicate_status = "unique".to_string();
            row.duplicate_state = None;
            row.transaction_id = None;
            row.attempt_id = None;
            row.duplicate_error = None;
        }
        Ok(DuplicateCheckResult::Duplicate(state)) => {
            row.duplicate_status = "duplicate".to_string();
            apply_duplicate_state(row, state);
            row.duplicate_error = None;
        }
        Ok(DuplicateCheckResult::Fatal(fatal)) => {
            row.duplicate_status = "fatal".to_string();
            row.duplicate_state = None;
            row.transaction_id = None;
            row.attempt_id = None;
            row.duplicate_error = Some(fatal.to_string());
        }
        Err(error) => {
            row.duplicate_status = "fatal".to_string();
            row.duplicate_state = None;
            row.transaction_id = None;
            row.attempt_id = None;
            row.duplicate_error = Some(duplicate_repository_error_message(&error).to_string());
        }
    }
}

fn apply_duplicate_state(row: &mut FuelUploadRowDto, state: DuplicateState) {
    match state {
        DuplicateState::CanonicalFinalized { transaction_id } => {
            row.duplicate_state = Some("canonical_finalized".to_string());
            row.transaction_id = Some(transaction_id.0);
            row.attempt_id = None;
        }
        DuplicateState::PreviousAttempt {
            attempt_id,
            retry,
            finalization,
            canonical_transaction,
        } => {
            row.attempt_id = Some(attempt_id.0);
            row.transaction_id = match &canonical_transaction {
                CanonicalTransactionKey::Present(transaction_id) => Some(transaction_id.0.clone()),
                CanonicalTransactionKey::Missing => Some(format!("missing-key-{}", row.row_number)),
            };
            row.duplicate_state = Some(match (retry, finalization, canonical_transaction) {
                (_, FinalizationState::FailedBeforeCanonicalFinalization, _) => {
                    "failed_before_canonical_finalization".to_string()
                }
                (
                    _,
                    FinalizationState::FailedAfterCanonicalFinalization,
                    CanonicalTransactionKey::Present(_),
                ) => "failed_after_canonical_finalization_with_key".to_string(),
                (
                    _,
                    FinalizationState::FailedAfterCanonicalFinalization,
                    CanonicalTransactionKey::Missing,
                ) => "failed_after_canonical_finalization_without_key".to_string(),
                (RetryEligibility::ExplicitlyRetryable, FinalizationState::Unknown, _) => {
                    "retryable_failure".to_string()
                }
                (RetryEligibility::NotRetryable, FinalizationState::Unknown, _) => {
                    "not_retryable".to_string()
                }
            });
        }
    }
}

fn vehicle_repository_error_message(error: &VehicleRepositoryError) -> &str {
    match error {
        VehicleRepositoryError::Unavailable { message }
        | VehicleRepositoryError::TimedOut { message } => message,
    }
}

fn duplicate_repository_error_message(error: &DuplicateRepositoryError) -> &str {
    match error {
        DuplicateRepositoryError::Unavailable { message }
        | DuplicateRepositoryError::TimedOut { message } => message,
    }
}

fn parse_vehicle_lookup(
    row: &FuelUploadRowDto,
    prefix: &str,
) -> Result<VehicleLookupResult, Vec<FuelUploadMappingError>> {
    match normalize(&row.vehicle_lookup_status).as_str() {
        "found" => {
            let vehicle_id = require(
                row.vehicle_id.as_deref(),
                &format!("{prefix}.vehicle_id"),
                FuelUploadMappingErrorCode::MissingVehicleLookupPayload,
            )?;
            Ok(VehicleLookupResult::Found(Vehicle {
                id: VehicleId(vehicle_id),
                reference: VehicleRef(row.vehicle_ref.trim().to_string()),
            }))
        }
        "notfound" => Ok(VehicleLookupResult::NotFound {
            requested: VehicleRef(row.vehicle_ref.trim().to_string()),
        }),
        "ambiguous" => {
            if row.ambiguous_vehicle_ids.is_empty() {
                Err(vec![mapping_error(
                    FuelUploadMappingErrorCode::MissingVehicleLookupPayload,
                    &format!("{prefix}.ambiguous_vehicle_ids"),
                    "Ambiguous vehicle lookup requires at least one candidate id.",
                )])
            } else {
                Ok(VehicleLookupResult::Ambiguous {
                    requested: VehicleRef(row.vehicle_ref.trim().to_string()),
                    matches: row
                        .ambiguous_vehicle_ids
                        .iter()
                        .map(|id| VehicleId(id.trim().to_string()))
                        .collect(),
                })
            }
        }
        "fatal" => {
            let message = require(
                row.vehicle_lookup_error.as_deref(),
                &format!("{prefix}.vehicle_lookup_error"),
                FuelUploadMappingErrorCode::MissingVehicleLookupPayload,
            )?;
            Ok(VehicleLookupResult::Fatal(
                FatalError::VehicleLookupUnavailable {
                    row_number: RowNumber(row.row_number),
                    message,
                },
            ))
        }
        _ => Err(vec![mapping_error(
            FuelUploadMappingErrorCode::InvalidVehicleLookupStatus,
            &format!("{prefix}.vehicle_lookup_status"),
            &format!(
                "Unsupported vehicle lookup status '{}'.",
                row.vehicle_lookup_status
            ),
        )]),
    }
}

fn parse_duplicate_check(
    row: &FuelUploadRowDto,
    prefix: &str,
) -> Result<DuplicateCheckResult, Vec<FuelUploadMappingError>> {
    match normalize(&row.duplicate_status).as_str() {
        "unique" => Ok(DuplicateCheckResult::Unique),
        "duplicate" => {
            let transaction_id = require(
                row.transaction_id.as_deref(),
                &format!("{prefix}.transaction_id"),
                FuelUploadMappingErrorCode::MissingDuplicatePayload,
            )?;
            let state = match normalize(row.duplicate_state.as_deref().unwrap_or_default()).as_str()
            {
                "canonicalfinalized" => DuplicateState::CanonicalFinalized {
                    transaction_id: TransactionId(transaction_id),
                },
                "retryablefailure" => previous_attempt(
                    row,
                    prefix,
                    RetryEligibility::ExplicitlyRetryable,
                    FinalizationState::Unknown,
                    CanonicalTransactionKey::Present(TransactionId(transaction_id)),
                )?,
                "notretryable" => previous_attempt(
                    row,
                    prefix,
                    RetryEligibility::NotRetryable,
                    FinalizationState::Unknown,
                    CanonicalTransactionKey::Present(TransactionId(transaction_id)),
                )?,
                "failedbeforecanonicalfinalization" => previous_attempt(
                    row,
                    prefix,
                    RetryEligibility::ExplicitlyRetryable,
                    FinalizationState::FailedBeforeCanonicalFinalization,
                    CanonicalTransactionKey::Missing,
                )?,
                "failedaftercanonicalfinalizationwithkey" => previous_attempt(
                    row,
                    prefix,
                    RetryEligibility::ExplicitlyRetryable,
                    FinalizationState::FailedAfterCanonicalFinalization,
                    CanonicalTransactionKey::Present(TransactionId(transaction_id)),
                )?,
                "failedaftercanonicalfinalizationwithoutkey" => previous_attempt(
                    row,
                    prefix,
                    RetryEligibility::ExplicitlyRetryable,
                    FinalizationState::FailedAfterCanonicalFinalization,
                    CanonicalTransactionKey::Missing,
                )?,
                _ => {
                    return Err(vec![mapping_error(
                        FuelUploadMappingErrorCode::MissingDuplicatePayload,
                        &format!("{prefix}.duplicate_state"),
                        "Duplicate rows require a supported duplicate state.",
                    )]);
                }
            };
            Ok(DuplicateCheckResult::Duplicate(state))
        }
        "fatal" => {
            let message = require(
                row.duplicate_error.as_deref(),
                &format!("{prefix}.duplicate_error"),
                FuelUploadMappingErrorCode::MissingDuplicatePayload,
            )?;
            Ok(DuplicateCheckResult::Fatal(
                FatalError::DuplicateCheckUnavailable {
                    row_number: RowNumber(row.row_number),
                    message,
                },
            ))
        }
        _ => Err(vec![mapping_error(
            FuelUploadMappingErrorCode::InvalidDuplicateStatus,
            &format!("{prefix}.duplicate_status"),
            &format!("Unsupported duplicate status '{}'.", row.duplicate_status),
        )]),
    }
}

fn previous_attempt(
    row: &FuelUploadRowDto,
    prefix: &str,
    retry: RetryEligibility,
    finalization: FinalizationState,
    canonical_transaction: CanonicalTransactionKey,
) -> Result<DuplicateState, Vec<FuelUploadMappingError>> {
    let attempt_id = require(
        row.attempt_id.as_deref(),
        &format!("{prefix}.attempt_id"),
        FuelUploadMappingErrorCode::MissingDuplicatePayload,
    )?;

    Ok(DuplicateState::PreviousAttempt {
        attempt_id: AttemptId(attempt_id),
        retry,
        finalization,
        canonical_transaction,
    })
}

fn parse_upload_mode(value: &str, field: &str) -> Result<UploadMode, Vec<FuelUploadMappingError>> {
    match normalize(value).as_str() {
        "normal" => Ok(UploadMode::Normal),
        "retry" => Ok(UploadMode::Retry),
        "conservativerecovery" => Ok(UploadMode::ConservativeRecovery),
        "aggressiverecovery" => Ok(UploadMode::AggressiveRecovery),
        _ => Err(vec![mapping_error(
            FuelUploadMappingErrorCode::InvalidUploadMode,
            field,
            &format!("Unsupported upload mode '{value}'."),
        )]),
    }
}

fn parse_import_upload_mode(
    value: Option<&str>,
    field: &str,
) -> Result<String, Vec<FuelImportError>> {
    let required = require_import_cell(value, field)?;
    match normalize(&required).as_str() {
        "normal" | "retry" | "conservativerecovery" | "aggressiverecovery" => Ok(required),
        _ => Err(vec![import_error(
            FuelImportErrorCode::InvalidUploadMode,
            field,
            &format!("Unsupported upload mode '{required}'."),
        )]),
    }
}

fn parse_import_date(value: Option<&str>, field: &str) -> Result<String, Vec<FuelImportError>> {
    let required = require_import_cell(value, field)?;
    if is_iso_date(&required) {
        Ok(required)
    } else {
        Err(vec![import_error(
            FuelImportErrorCode::InvalidDate,
            field,
            "Date must use yyyy-MM-dd format.",
        )])
    }
}

fn parse_required_f64(value: Option<&str>, field: &str) -> Result<f64, Vec<FuelImportError>> {
    let required = require_import_cell(value, field)?;
    match required.parse::<f64>() {
        Ok(parsed) if parsed.is_finite() => Ok(parsed),
        _ => Err(vec![import_error(
            FuelImportErrorCode::InvalidNumber,
            field,
            "Cell must be a decimal number.",
        )]),
    }
}

fn parse_optional_f64(
    value: Option<&str>,
    field: &str,
) -> Result<Option<f64>, Vec<FuelImportError>> {
    match optional_cell(value) {
        Some(value) => match value.parse::<f64>() {
            Ok(parsed) if parsed.is_finite() => Ok(Some(parsed)),
            _ => Err(vec![import_error(
                FuelImportErrorCode::InvalidNumber,
                field,
                "Cell must be a decimal number.",
            )]),
        },
        None => Ok(None),
    }
}

fn parse_required_u32(value: Option<&str>, field: &str) -> Result<u32, Vec<FuelImportError>> {
    let required = require_import_cell(value, field)?;
    required.parse::<u32>().map_err(|_| {
        vec![import_error(
            FuelImportErrorCode::InvalidNumber,
            field,
            "Cell must be an integer.",
        )]
    })
}

fn parse_optional_u32(
    value: Option<&str>,
    field: &str,
) -> Result<Option<u32>, Vec<FuelImportError>> {
    match optional_cell(value) {
        Some(value) => value.parse::<u32>().map(Some).map_err(|_| {
            vec![import_error(
                FuelImportErrorCode::InvalidNumber,
                field,
                "Cell must be an integer.",
            )]
        }),
        None => Ok(None),
    }
}

fn parse_cost_rule(
    value: &str,
    field: &str,
) -> Result<PositiveNumberRule, Vec<FuelUploadMappingError>> {
    match normalize(value).as_str() {
        "strictlypositive" => Ok(PositiveNumberRule::StrictlyPositive),
        "zeroorpositive" => Ok(PositiveNumberRule::ZeroOrPositive),
        _ => Err(vec![mapping_error(
            FuelUploadMappingErrorCode::InvalidCostRule,
            field,
            &format!("Unsupported cost rule '{value}'."),
        )]),
    }
}

fn parse_odometer_rule(
    value: &str,
    field: &str,
) -> Result<OdometerRule, Vec<FuelUploadMappingError>> {
    match normalize(value).as_str() {
        "required" => Ok(OdometerRule::Required),
        "optional" => Ok(OdometerRule::Optional),
        "optionalwarnwhenmissing" => Ok(OdometerRule::OptionalWarnWhenMissing),
        _ => Err(vec![mapping_error(
            FuelUploadMappingErrorCode::InvalidOdometerRule,
            field,
            &format!("Unsupported odometer rule '{value}'."),
        )]),
    }
}

fn warning_limit(value: Option<f64>) -> WarningLimit<f64> {
    value
        .map(WarningLimit::Above)
        .unwrap_or(WarningLimit::Disabled)
}

fn require(
    value: Option<&str>,
    field: &str,
    code: FuelUploadMappingErrorCode,
) -> Result<String, Vec<FuelUploadMappingError>> {
    match value.map(str::trim) {
        Some(value) if !value.is_empty() => Ok(value.to_string()),
        _ => Err(vec![mapping_error(
            code,
            field,
            "A non-empty value is required.",
        )]),
    }
}

fn require_import_cell(value: Option<&str>, field: &str) -> Result<String, Vec<FuelImportError>> {
    match value.map(str::trim) {
        Some(value) if !value.is_empty() => Ok(value.to_string()),
        _ => Err(vec![import_error(
            FuelImportErrorCode::MissingRequiredCell,
            field,
            "A non-empty cell is required.",
        )]),
    }
}

fn optional_cell(value: Option<&str>) -> Option<String> {
    value
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_string)
}

fn mapping_error(
    code: FuelUploadMappingErrorCode,
    field: &str,
    detail: &str,
) -> FuelUploadMappingError {
    FuelUploadMappingError {
        code,
        field: field.to_string(),
        detail: detail.to_string(),
    }
}

fn import_error(code: FuelImportErrorCode, field: &str, detail: &str) -> FuelImportError {
    FuelImportError {
        code,
        field: field.to_string(),
        detail: detail.to_string(),
    }
}

impl From<FuelUploadMappingError> for FuelImportError {
    fn from(value: FuelUploadMappingError) -> Self {
        let code = match value.code {
            FuelUploadMappingErrorCode::InvalidUploadMode => FuelImportErrorCode::InvalidUploadMode,
            _ => FuelImportErrorCode::MissingRequiredCell,
        };
        import_error(code, &value.field, &value.detail)
    }
}

fn normalize(value: &str) -> String {
    value.replace('_', "").trim().to_ascii_lowercase()
}

fn is_iso_date(value: &str) -> bool {
    let bytes = value.as_bytes();
    bytes.len() == 10
        && bytes[4] == b'-'
        && bytes[7] == b'-'
        && bytes
            .iter()
            .enumerate()
            .all(|(index, byte)| index == 4 || index == 7 || byte.is_ascii_digit())
        && value[5..7]
            .parse::<u32>()
            .is_ok_and(|month| (1..=12).contains(&month))
        && value[8..10]
            .parse::<u32>()
            .is_ok_and(|day| (1..=31).contains(&day))
}
