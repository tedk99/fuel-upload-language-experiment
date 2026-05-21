use super::primitives::*;

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
