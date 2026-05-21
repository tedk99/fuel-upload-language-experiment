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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UploadMode {
    Normal,
    Retry,
    Recovery,
}
