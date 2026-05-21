use std::fmt;

use super::primitives::*;

#[derive(Debug, Clone, PartialEq)]
pub struct ValidationConfig {
    pub cost_rule: PositiveNumberRule,
    pub odometer_rule: OdometerRule,
    pub large_quantity_warning: WarningLimit<f64>,
    pub high_unit_cost_warning: WarningLimit<f64>,
    pub suspicious_quantity: f64,
    pub suspicious_total_cost: f64,
}

impl Default for ValidationConfig {
    fn default() -> Self {
        Self {
            cost_rule: PositiveNumberRule::ZeroOrPositive,
            odometer_rule: OdometerRule::OptionalWarnWhenMissing,
            large_quantity_warning: WarningLimit::Above(300.0),
            high_unit_cost_warning: WarningLimit::Above(10.0),
            suspicious_quantity: 333_333.0,
            suspicious_total_cost: 333_333.0,
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

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum QuarantineReason {
    SuspiciousMerchantName,
    SuspiciousQuantityPattern,
    SuspiciousCostPattern,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct QuarantineReasons(Vec<QuarantineReason>);

impl QuarantineReasons {
    pub fn new(reasons: Vec<QuarantineReason>) -> Option<Self> {
        if reasons.is_empty() {
            None
        } else {
            Some(Self(reasons))
        }
    }

    pub fn as_slice(&self) -> &[QuarantineReason] {
        &self.0
    }
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
