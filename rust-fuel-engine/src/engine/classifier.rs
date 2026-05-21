use crate::domain::*;
use crate::engine::duplicate_policy::{DuplicateGate, duplicate_gate};
use crate::engine::transaction_factory::build_transaction;

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
            | RowDecision::Quarantined { .. }
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
    if let VehicleLookupResult::Fatal(error) = &input.vehicle_lookup {
        return RowDecision::Fatal(error.clone());
    }

    if let DuplicateCheckResult::Fatal(error) = &input.duplicate_check {
        return RowDecision::Fatal(error.clone());
    }

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

    let transaction = build_transaction(&input.row, vehicle);
    let warnings = warnings_for(&input.row, transaction.unit_cost, config);
    let quarantine_reasons = quarantine_reasons_for(&input.row, config);

    if let Some(reasons) = QuarantineReasons::new(quarantine_reasons) {
        return RowDecision::Quarantined {
            transaction,
            reasons,
            warnings,
        };
    }

    if warnings.is_empty() {
        RowDecision::Accepted(transaction)
    } else {
        RowDecision::Warning {
            transaction,
            warnings,
        }
    }
}

fn quarantine_reasons_for(row: &ParsedFuelRow, config: &ValidationConfig) -> Vec<QuarantineReason> {
    let mut reasons = Vec::new();

    if let Merchant::Known(name) = &row.merchant {
        let lowered = name.to_ascii_lowercase();
        if lowered.contains("test") || lowered.contains("unknown") || lowered.contains("manual") {
            reasons.push(QuarantineReason::SuspiciousMerchantName);
        }
    }

    if row.quantity_liters == config.suspicious_quantity {
        reasons.push(QuarantineReason::SuspiciousQuantityPattern);
    }

    if row.total_cost == config.suspicious_total_cost {
        reasons.push(QuarantineReason::SuspiciousCostPattern);
    }

    reasons
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
