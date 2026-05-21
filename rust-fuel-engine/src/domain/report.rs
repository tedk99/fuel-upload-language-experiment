use super::Summary;
use super::decision::{BatchDecision, RowDecision};
use super::primitives::{RowNumber, TransactionId};
use super::validation::{FatalError, QuarantineReason};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OperationalBatchStatus {
    Ready,
    Fatal,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OperationalQuarantinedRow {
    pub row_number: RowNumber,
    pub reasons: Vec<QuarantineReason>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct OperationalBatchReport {
    pub status: OperationalBatchStatus,
    pub counts: Summary,
    pub uploaded_transaction_ids: Vec<TransactionId>,
    pub rejected_row_numbers: Vec<RowNumber>,
    pub quarantined_rows: Vec<OperationalQuarantinedRow>,
    pub skipped_duplicate_row_numbers: Vec<RowNumber>,
    pub fatal_errors: Vec<FatalError>,
}

pub fn project_operational_report(decision: &BatchDecision) -> OperationalBatchReport {
    let status = match decision {
        BatchDecision::Ready { .. } => OperationalBatchStatus::Ready,
        BatchDecision::Blocked { .. } => OperationalBatchStatus::Fatal,
    };

    let uploaded_transaction_ids = if status == OperationalBatchStatus::Fatal {
        Vec::new()
    } else {
        decision
            .rows()
            .iter()
            .filter_map(|row| match row {
                RowDecision::Accepted(transaction) | RowDecision::Warning { transaction, .. } => {
                    Some(transaction.transaction_id.clone())
                }
                RowDecision::Quarantined { .. }
                | RowDecision::SkippedDuplicate(_)
                | RowDecision::Rejected(_)
                | RowDecision::Fatal(_) => None,
            })
            .collect()
    };

    OperationalBatchReport {
        status,
        counts: decision.summary().clone(),
        uploaded_transaction_ids,
        rejected_row_numbers: decision
            .rows()
            .iter()
            .filter_map(|row| match row {
                RowDecision::Rejected(rejected) => Some(rejected.row_number),
                _ => None,
            })
            .collect(),
        quarantined_rows: decision
            .rows()
            .iter()
            .filter_map(|row| match row {
                RowDecision::Quarantined {
                    transaction,
                    reasons,
                    ..
                } => Some(OperationalQuarantinedRow {
                    row_number: transaction.row_number,
                    reasons: reasons.as_slice().to_vec(),
                }),
                _ => None,
            })
            .collect(),
        skipped_duplicate_row_numbers: decision
            .rows()
            .iter()
            .filter_map(|row| match row {
                RowDecision::SkippedDuplicate(skipped) => Some(skipped.row_number),
                _ => None,
            })
            .collect(),
        fatal_errors: match decision {
            BatchDecision::Ready { .. } => Vec::new(),
            BatchDecision::Blocked { fatal_errors, .. } => fatal_errors.clone(),
        },
    }
}
