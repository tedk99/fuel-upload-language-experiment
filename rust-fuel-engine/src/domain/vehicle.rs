use super::primitives::*;
use super::validation::FatalError;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Vehicle {
    pub id: VehicleId,
    pub reference: VehicleRef,
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
