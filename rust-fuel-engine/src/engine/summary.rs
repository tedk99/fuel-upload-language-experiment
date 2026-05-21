use crate::domain::*;

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
