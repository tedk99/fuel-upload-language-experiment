use std::fmt;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct RowNumber(pub u32);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct SourceRowId(pub String);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct VehicleRef(pub String);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct VehicleId(pub String);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct TransactionId(pub String);

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct AttemptId(pub String);

#[derive(Debug, Clone, PartialEq)]
pub struct ParsedFuelRow {
    pub row_number: RowNumber,
    pub source_id: SourceRowId,
    pub vehicle_ref: VehicleRef,
    pub occurred_on: FuelDate,
    pub quantity_liters: f64,
    pub total_cost: f64,
    pub odometer: OdometerReading,
    pub merchant: Merchant,
}

#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct FuelDate(pub String);

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum OdometerReading {
    Known(u32),
    Missing,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Merchant {
    Known(String),
    Missing,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Vehicle {
    pub id: VehicleId,
    pub reference: VehicleRef,
}

#[derive(Debug, Clone, PartialEq)]
pub struct FuelTransaction {
    pub source_id: SourceRowId,
    pub vehicle_id: VehicleId,
    pub occurred_on: FuelDate,
    pub quantity_liters: f64,
    pub total_cost: f64,
    pub unit_cost: f64,
    pub odometer: OdometerReading,
    pub merchant: Merchant,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum VehicleLookupResult {
    Found(Vehicle),
    NotFound {
        requested: VehicleRef,
    },
    Ambiguous {
        requested: VehicleRef,
        matches: Vec<VehicleId>,
    },
    Fatal(FatalError),
}

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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UploadMode {
    Normal,
    Retry,
    Recovery,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ValidationConfig {
    pub cost_rule: PositiveNumberRule,
    pub odometer_rule: OdometerRule,
    pub large_quantity_warning: WarningLimit<f64>,
    pub high_unit_cost_warning: WarningLimit<f64>,
}

impl Default for ValidationConfig {
    fn default() -> Self {
        Self {
            cost_rule: PositiveNumberRule::ZeroOrPositive,
            odometer_rule: OdometerRule::OptionalWarnWhenMissing,
            large_quantity_warning: WarningLimit::Above(300.0),
            high_unit_cost_warning: WarningLimit::Above(10.0),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PositiveNumberRule {
    StrictlyPositive,
    ZeroOrPositive,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OdometerRule {
    Required,
    Optional,
    OptionalWarnWhenMissing,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum WarningLimit<T> {
    Disabled,
    Above(T),
}

#[derive(Debug, Clone, PartialEq)]
pub enum ValidationError {
    QuantityNotFinite,
    QuantityNotPositive { value: f64 },
    CostNotFinite,
    CostNegative { value: f64 },
    CostNotPositive { value: f64 },
    OdometerRequired,
    EmptySourceId,
    EmptyVehicleRef,
    EmptyFuelDate,
    EmptyMerchant,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Warning {
    MissingOdometer,
    LargeQuantity {
        quantity_liters: f64,
        configured_limit: f64,
    },
    HighUnitCost {
        unit_cost: f64,
        configured_limit: f64,
    },
}

#[derive(Debug, Clone, PartialEq)]
pub enum RejectionReason {
    ValidationFailed(Vec<ValidationError>),
    VehicleNotFound {
        requested: VehicleRef,
    },
    AmbiguousVehicle {
        requested: VehicleRef,
        matches: Vec<VehicleId>,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FatalError {
    VehicleLookupUnavailable {
        row_number: RowNumber,
        message: String,
    },
    DuplicateCheckUnavailable {
        row_number: RowNumber,
        message: String,
    },
    EmptyBatch,
}

#[derive(Debug, Clone, PartialEq)]
pub struct RejectedRow {
    pub row_number: RowNumber,
    pub source_id: SourceRowId,
    pub reason: RejectionReason,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SkippedDuplicate {
    pub row_number: RowNumber,
    pub source_id: SourceRowId,
    pub state: DuplicateState,
    pub mode: UploadMode,
}

#[derive(Debug, Clone, PartialEq)]
pub enum RowDecision {
    Accepted(FuelTransaction),
    Warning {
        transaction: FuelTransaction,
        warnings: Vec<Warning>,
    },
    SkippedDuplicate(SkippedDuplicate),
    Rejected(RejectedRow),
    Fatal(FatalError),
}

#[derive(Debug, Clone, PartialEq)]
pub struct RowInput {
    pub row: ParsedFuelRow,
    pub vehicle_lookup: VehicleLookupResult,
    pub duplicate_check: DuplicateCheckResult,
}

#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct Summary {
    pub total_rows: usize,
    pub accepted: usize,
    pub accepted_with_warnings: usize,
    pub skipped_duplicates: usize,
    pub rejected: usize,
    pub fatal_errors: usize,
    pub warnings: usize,
}

#[derive(Debug, Clone, PartialEq)]
pub enum BatchDecision {
    Ready {
        rows: Vec<RowDecision>,
        summary: Summary,
    },
    Blocked {
        rows: Vec<RowDecision>,
        summary: Summary,
        fatal_errors: Vec<FatalError>,
    },
}

impl BatchDecision {
    pub fn summary(&self) -> &Summary {
        match self {
            BatchDecision::Ready { summary, .. } | BatchDecision::Blocked { summary, .. } => {
                summary
            }
        }
    }

    pub fn rows(&self) -> &[RowDecision] {
        match self {
            BatchDecision::Ready { rows, .. } | BatchDecision::Blocked { rows, .. } => rows,
        }
    }

    pub fn uploadable_transactions(&self) -> Vec<&FuelTransaction> {
        match self {
            BatchDecision::Ready { rows, .. } => rows
                .iter()
                .filter_map(|decision| match decision {
                    RowDecision::Accepted(transaction)
                    | RowDecision::Warning { transaction, .. } => Some(transaction),
                    RowDecision::SkippedDuplicate(_)
                    | RowDecision::Rejected(_)
                    | RowDecision::Fatal(_) => None,
                })
                .collect(),
            BatchDecision::Blocked { .. } => Vec::new(),
        }
    }
}

pub fn classify_batch(
    inputs: &[RowInput],
    mode: UploadMode,
    config: &ValidationConfig,
) -> BatchDecision {
    if inputs.is_empty() {
        let fatal = FatalError::EmptyBatch;
        let rows = vec![RowDecision::Fatal(fatal.clone())];
        let summary = Summary::from_rows(&rows);

        return BatchDecision::Blocked {
            rows,
            summary,
            fatal_errors: vec![fatal],
        };
    }

    let rows: Vec<RowDecision> = inputs
        .iter()
        .map(|input| classify_row(input, mode, config))
        .collect();
    let summary = Summary::from_rows(&rows);
    let fatal_errors: Vec<FatalError> = rows
        .iter()
        .filter_map(|decision| match decision {
            RowDecision::Fatal(error) => Some(error.clone()),
            RowDecision::Accepted(_)
            | RowDecision::Warning { .. }
            | RowDecision::SkippedDuplicate(_)
            | RowDecision::Rejected(_) => None,
        })
        .collect();

    if fatal_errors.is_empty() {
        BatchDecision::Ready { rows, summary }
    } else {
        BatchDecision::Blocked {
            rows,
            summary,
            fatal_errors,
        }
    }
}

pub fn classify_row(input: &RowInput, mode: UploadMode, config: &ValidationConfig) -> RowDecision {
    let validation_errors = validate_row(&input.row, config);
    if !validation_errors.is_empty() {
        return RowDecision::Rejected(RejectedRow {
            row_number: input.row.row_number,
            source_id: input.row.source_id.clone(),
            reason: RejectionReason::ValidationFailed(validation_errors),
        });
    }

    let vehicle = match &input.vehicle_lookup {
        VehicleLookupResult::Found(vehicle) => vehicle,
        VehicleLookupResult::NotFound { requested } => {
            return RowDecision::Rejected(RejectedRow {
                row_number: input.row.row_number,
                source_id: input.row.source_id.clone(),
                reason: RejectionReason::VehicleNotFound {
                    requested: requested.clone(),
                },
            });
        }
        VehicleLookupResult::Ambiguous { requested, matches } => {
            return RowDecision::Rejected(RejectedRow {
                row_number: input.row.row_number,
                source_id: input.row.source_id.clone(),
                reason: RejectionReason::AmbiguousVehicle {
                    requested: requested.clone(),
                    matches: matches.clone(),
                },
            });
        }
        VehicleLookupResult::Fatal(error) => return RowDecision::Fatal(error.clone()),
    };

    match duplicate_gate(&input.row, &input.duplicate_check, mode) {
        DuplicateGate::Continue => {}
        DuplicateGate::Skip(skipped) => return RowDecision::SkippedDuplicate(skipped),
        DuplicateGate::Fatal(error) => return RowDecision::Fatal(error),
    }

    let transaction = FuelTransaction {
        source_id: input.row.source_id.clone(),
        vehicle_id: vehicle.id.clone(),
        occurred_on: input.row.occurred_on.clone(),
        quantity_liters: input.row.quantity_liters,
        total_cost: input.row.total_cost,
        unit_cost: input.row.total_cost / input.row.quantity_liters,
        odometer: input.row.odometer.clone(),
        merchant: input.row.merchant.clone(),
    };
    let warnings = warnings_for(&input.row, transaction.unit_cost, config);

    if warnings.is_empty() {
        RowDecision::Accepted(transaction)
    } else {
        RowDecision::Warning {
            transaction,
            warnings,
        }
    }
}

impl Summary {
    pub fn from_rows(rows: &[RowDecision]) -> Self {
        rows.iter().fold(
            Self {
                total_rows: rows.len(),
                ..Self::default()
            },
            |mut summary, decision| {
                match decision {
                    RowDecision::Accepted(_) => {
                        summary.accepted += 1;
                    }
                    RowDecision::Warning { warnings, .. } => {
                        summary.accepted += 1;
                        summary.accepted_with_warnings += 1;
                        summary.warnings += warnings.len();
                    }
                    RowDecision::SkippedDuplicate(_) => {
                        summary.skipped_duplicates += 1;
                    }
                    RowDecision::Rejected(_) => {
                        summary.rejected += 1;
                    }
                    RowDecision::Fatal(_) => {
                        summary.fatal_errors += 1;
                    }
                }
                summary
            },
        )
    }
}

enum DuplicateGate {
    Continue,
    Skip(SkippedDuplicate),
    Fatal(FatalError),
}

fn duplicate_gate(
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
            UploadMode::Recovery,
            DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
                finalization: FinalizationState::FailedBeforeCanonicalFinalization,
                ..
            }),
        ) => DuplicateGate::Continue,
        (UploadMode::Recovery, DuplicateCheckResult::Duplicate(state)) => {
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

fn validate_row(row: &ParsedFuelRow, config: &ValidationConfig) -> Vec<ValidationError> {
    let mut errors = Vec::new();

    if row.source_id.0.trim().is_empty() {
        errors.push(ValidationError::EmptySourceId);
    }
    if row.vehicle_ref.0.trim().is_empty() {
        errors.push(ValidationError::EmptyVehicleRef);
    }
    if row.occurred_on.0.trim().is_empty() {
        errors.push(ValidationError::EmptyFuelDate);
    }
    if matches!(&row.merchant, Merchant::Known(name) if name.trim().is_empty()) {
        errors.push(ValidationError::EmptyMerchant);
    }
    if !row.quantity_liters.is_finite() {
        errors.push(ValidationError::QuantityNotFinite);
    } else if row.quantity_liters <= 0.0 {
        errors.push(ValidationError::QuantityNotPositive {
            value: row.quantity_liters,
        });
    }
    if !row.total_cost.is_finite() {
        errors.push(ValidationError::CostNotFinite);
    } else {
        match config.cost_rule {
            PositiveNumberRule::StrictlyPositive if row.total_cost <= 0.0 => {
                errors.push(ValidationError::CostNotPositive {
                    value: row.total_cost,
                });
            }
            PositiveNumberRule::ZeroOrPositive if row.total_cost < 0.0 => {
                errors.push(ValidationError::CostNegative {
                    value: row.total_cost,
                });
            }
            PositiveNumberRule::StrictlyPositive | PositiveNumberRule::ZeroOrPositive => {}
        }
    }
    if matches!(
        (&config.odometer_rule, &row.odometer),
        (OdometerRule::Required, OdometerReading::Missing)
    ) {
        errors.push(ValidationError::OdometerRequired);
    }

    errors
}

fn warnings_for(row: &ParsedFuelRow, unit_cost: f64, config: &ValidationConfig) -> Vec<Warning> {
    let mut warnings = Vec::new();

    match (&config.odometer_rule, &row.odometer) {
        (OdometerRule::OptionalWarnWhenMissing, OdometerReading::Missing) => {
            warnings.push(Warning::MissingOdometer);
        }
        (OdometerRule::Required, _)
        | (OdometerRule::Optional, _)
        | (OdometerRule::OptionalWarnWhenMissing, OdometerReading::Known(_)) => {}
    }

    match config.large_quantity_warning {
        WarningLimit::Above(limit) if row.quantity_liters > limit => {
            warnings.push(Warning::LargeQuantity {
                quantity_liters: row.quantity_liters,
                configured_limit: limit,
            });
        }
        WarningLimit::Above(_) | WarningLimit::Disabled => {}
    }

    match config.high_unit_cost_warning {
        WarningLimit::Above(limit) if unit_cost > limit => {
            warnings.push(Warning::HighUnitCost {
                unit_cost,
                configured_limit: limit,
            });
        }
        WarningLimit::Above(_) | WarningLimit::Disabled => {}
    }

    warnings
}

impl fmt::Display for FatalError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            FatalError::VehicleLookupUnavailable {
                row_number,
                message,
            } => write!(
                formatter,
                "vehicle lookup unavailable for row {}: {}",
                row_number.0, message
            ),
            FatalError::DuplicateCheckUnavailable {
                row_number,
                message,
            } => write!(
                formatter,
                "duplicate check unavailable for row {}: {}",
                row_number.0, message
            ),
            FatalError::EmptyBatch => write!(formatter, "batch contains no rows"),
        }
    }
}

impl std::error::Error for FatalError {}

#[cfg(test)]
mod tests {
    use super::*;

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
    fn duplicate_recovery_is_accepted_only_before_canonical_finalization() {
        let accepted = classify_row(
            &row_input(
                valid_row(),
                found_vehicle(),
                previous_attempt(
                    RetryEligibility::NotRetryable,
                    FinalizationState::FailedBeforeCanonicalFinalization,
                ),
            ),
            UploadMode::Recovery,
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
            UploadMode::Recovery,
            &quiet_config(),
        );
        let finalized = classify_row(
            &row_input(valid_row(), found_vehicle(), finalized_duplicate()),
            UploadMode::Recovery,
            &quiet_config(),
        );

        assert!(matches!(accepted, RowDecision::Accepted(_)));
        assert!(matches!(skipped, RowDecision::SkippedDuplicate(_)));
        assert!(matches!(finalized, RowDecision::SkippedDuplicate(_)));
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

    fn previous_attempt(
        retry: RetryEligibility,
        finalization: FinalizationState,
    ) -> DuplicateCheckResult {
        DuplicateCheckResult::Duplicate(DuplicateState::PreviousAttempt {
            attempt_id: AttemptId("attempt-1".to_string()),
            retry,
            finalization,
        })
    }
}
