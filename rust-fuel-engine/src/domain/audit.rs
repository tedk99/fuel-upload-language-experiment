use super::decision::{BatchDecision, FuelTransaction, RejectedRow, RowDecision, SkippedDuplicate};
use super::primitives::*;
use super::validation::{FatalError, QuarantineReason, RejectionReason, Warning};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AuditEventKind {
    Accepted,
    AcceptedWithWarnings,
    Rejected,
    SkippedDuplicate,
    Quarantined,
    FatalBatch,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AuditRecord {
    pub kind: AuditEventKind,
    pub row_number: Option<RowNumber>,
    pub source_id: Option<SourceRowId>,
    pub vehicle_id: Option<VehicleId>,
    pub warnings: Vec<Warning>,
    pub quarantine_reasons: Vec<QuarantineReason>,
    pub rejection: Option<RejectionReason>,
    pub duplicate_skip: Option<SkippedDuplicate>,
    pub fatal: Option<FatalError>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AuditRecordDto {
    pub status: String,
    pub row_number: Option<u32>,
    pub source_id: Option<String>,
    pub vehicle_id: Option<String>,
    pub warnings: Vec<String>,
    pub quarantine_reasons: Vec<String>,
    pub rejection: Option<String>,
    pub duplicate_skip: Option<String>,
    pub fatal: Option<String>,
}

impl AuditRecord {
    pub fn to_dto(&self) -> AuditRecordDto {
        AuditRecordDto {
            status: audit_status(self.kind).to_string(),
            row_number: self.row_number.map(|row| row.0),
            source_id: self.source_id.as_ref().map(|source| source.0.clone()),
            vehicle_id: self.vehicle_id.as_ref().map(|vehicle| vehicle.0.clone()),
            warnings: self
                .warnings
                .iter()
                .map(|warning| format!("{warning:?}"))
                .collect(),
            quarantine_reasons: self
                .quarantine_reasons
                .iter()
                .map(|reason| format!("{reason:?}"))
                .collect(),
            rejection: self.rejection.as_ref().map(|reason| format!("{reason:?}")),
            duplicate_skip: self
                .duplicate_skip
                .as_ref()
                .map(|skipped| format!("{:?}", skipped.state)),
            fatal: self.fatal.as_ref().map(ToString::to_string),
        }
    }
}

pub fn project_audit(decision: &BatchDecision) -> Vec<AuditRecord> {
    decision.rows().iter().map(project_row).collect()
}

pub fn audit_status(kind: AuditEventKind) -> &'static str {
    match kind {
        AuditEventKind::Accepted => "accepted",
        AuditEventKind::AcceptedWithWarnings => "accepted_with_warnings",
        AuditEventKind::Rejected => "rejected",
        AuditEventKind::SkippedDuplicate => "skipped_duplicate",
        AuditEventKind::Quarantined => "quarantined",
        AuditEventKind::FatalBatch => "fatal_batch",
    }
}

fn project_row(decision: &RowDecision) -> AuditRecord {
    match decision {
        RowDecision::Accepted(transaction) => transaction_record(
            AuditEventKind::Accepted,
            transaction,
            Vec::new(),
            Vec::new(),
        ),
        RowDecision::Warning {
            transaction,
            warnings,
        } => transaction_record(
            AuditEventKind::AcceptedWithWarnings,
            transaction,
            warnings.clone(),
            Vec::new(),
        ),
        RowDecision::Quarantined {
            transaction,
            reasons,
            warnings,
        } => transaction_record(
            AuditEventKind::Quarantined,
            transaction,
            warnings.clone(),
            reasons.as_slice().to_vec(),
        ),
        RowDecision::SkippedDuplicate(skipped) => empty_record(AuditEventKind::SkippedDuplicate)
            .with_row(skipped.row_number, skipped.source_id.clone())
            .with_duplicate_skip(skipped.clone()),
        RowDecision::Rejected(rejected) => rejected_record(rejected),
        RowDecision::Fatal(fatal) => {
            let mut record = empty_record(AuditEventKind::FatalBatch);
            record.row_number = fatal_row_number(fatal);
            record.fatal = Some(fatal.clone());
            record
        }
    }
}

fn transaction_record(
    kind: AuditEventKind,
    transaction: &FuelTransaction,
    warnings: Vec<Warning>,
    quarantine_reasons: Vec<QuarantineReason>,
) -> AuditRecord {
    let mut record = empty_record(kind);
    record.source_id = Some(transaction.source_id.clone());
    record.vehicle_id = Some(transaction.vehicle_id.clone());
    record.warnings = warnings;
    record.quarantine_reasons = quarantine_reasons;
    record
}

fn rejected_record(rejected: &RejectedRow) -> AuditRecord {
    let mut record = empty_record(AuditEventKind::Rejected);
    record.row_number = Some(rejected.row_number);
    record.source_id = Some(rejected.source_id.clone());
    record.rejection = Some(rejected.reason.clone());
    record
}

fn empty_record(kind: AuditEventKind) -> AuditRecord {
    AuditRecord {
        kind,
        row_number: None,
        source_id: None,
        vehicle_id: None,
        warnings: Vec::new(),
        quarantine_reasons: Vec::new(),
        rejection: None,
        duplicate_skip: None,
        fatal: None,
    }
}

fn fatal_row_number(fatal: &FatalError) -> Option<RowNumber> {
    match fatal {
        FatalError::VehicleLookupUnavailable { row_number, .. }
        | FatalError::DuplicateCheckUnavailable { row_number, .. } => Some(*row_number),
        FatalError::EmptyBatch => None,
    }
}

trait AuditRecordBuilder {
    fn with_row(self, row_number: RowNumber, source_id: SourceRowId) -> Self;
    fn with_duplicate_skip(self, skipped: SkippedDuplicate) -> Self;
}

impl AuditRecordBuilder for AuditRecord {
    fn with_row(mut self, row_number: RowNumber, source_id: SourceRowId) -> Self {
        self.row_number = Some(row_number);
        self.source_id = Some(source_id);
        self
    }

    fn with_duplicate_skip(mut self, skipped: SkippedDuplicate) -> Self {
        self.duplicate_skip = Some(skipped);
        self
    }
}
