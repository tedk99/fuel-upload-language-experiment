use super::duplicate::DuplicateCheckResult;
use super::duplicate::DuplicateState;
use super::primitives::*;
use super::row::ParsedFuelRow;
use super::validation::{FatalError, QuarantineReasons, RejectionReason, Warning};
use super::vehicle::VehicleLookupResult;

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
    Quarantined {
        transaction: FuelTransaction,
        reasons: QuarantineReasons,
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
    pub quarantined: usize,
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
                    RowDecision::Quarantined { .. }
                    | RowDecision::SkippedDuplicate(_)
                    | RowDecision::Rejected(_)
                    | RowDecision::Fatal(_) => None,
                })
                .collect(),
            BatchDecision::Blocked { .. } => Vec::new(),
        }
    }
}
