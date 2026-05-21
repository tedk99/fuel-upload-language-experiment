use crate::domain::*;

pub(crate) fn build_transaction(row: &ParsedFuelRow, vehicle: &Vehicle) -> FuelTransaction {
    FuelTransaction {
        transaction_id: TransactionId(format!("{}:{}", row.source_id.0, vehicle.id.0)),
        row_number: row.row_number,
        source_id: row.source_id.clone(),
        vehicle_id: vehicle.id.clone(),
        occurred_on: row.occurred_on.clone(),
        quantity_liters: row.quantity_liters,
        total_cost: row.total_cost,
        unit_cost: row.total_cost / row.quantity_liters,
        odometer: row.odometer.clone(),
        merchant: row.merchant.clone(),
    }
}
