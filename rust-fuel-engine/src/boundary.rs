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

fn normalize(value: &str) -> String {
    value.replace('_', "").trim().to_ascii_lowercase()
}
