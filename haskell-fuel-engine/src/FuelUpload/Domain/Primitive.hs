module FuelUpload.Domain.Primitive
  ( RowNumber (..)
  , ExternalRowId (..)
  , VehicleId (..)
  , Registration (..)
  , FuelQuantity (..)
  , MoneyAmount (..)
  , OdometerReading (..)
  , TransactionId (..)
  , UploadMode (..)
  , ValidationConfig (..)
  , FatalError (..)
  ) where

newtype RowNumber = RowNumber Int
  deriving stock (Eq, Ord, Show)

newtype ExternalRowId = ExternalRowId String
  deriving stock (Eq, Ord, Show)

newtype VehicleId = VehicleId String
  deriving stock (Eq, Ord, Show)

newtype Registration = Registration String
  deriving stock (Eq, Ord, Show)

newtype FuelQuantity = FuelQuantity Rational
  deriving stock (Eq, Ord, Show)

newtype MoneyAmount = MoneyAmount Rational
  deriving stock (Eq, Ord, Show)

newtype OdometerReading = OdometerReading Integer
  deriving stock (Eq, Ord, Show)

newtype TransactionId = TransactionId String
  deriving stock (Eq, Ord, Show)

data UploadMode
  = Normal
  | Retry
  | ConservativeRecovery
  | AggressiveRecovery
  deriving stock (Eq, Show)

data ValidationConfig = ValidationConfig
  { maximumQuantity :: FuelQuantity
  , maximumAmount :: MoneyAmount
  , highQuantityWarning :: FuelQuantity
  , highAmountWarning :: MoneyAmount
  , highOdometerWarning :: OdometerReading
  , suspiciousQuantity :: FuelQuantity
  , suspiciousAmount :: MoneyAmount
  }
  deriving stock (Eq, Show)

data FatalError
  = VehicleLookupUnavailable RowNumber
  | DuplicateCheckUnavailable RowNumber
  | CorruptParsedRow RowNumber
  deriving stock (Eq, Show)
