module FuelUpload.Domain.Row
  ( ParsedFuelRow (..)
  ) where

import FuelUpload.Domain.Primitive

data ParsedFuelRow = ParsedFuelRow
  { parsedRowNumber :: RowNumber
  , parsedExternalRowId :: ExternalRowId
  , parsedRegistration :: Registration
  , parsedQuantity :: FuelQuantity
  , parsedAmount :: MoneyAmount
  , parsedOdometer :: OdometerReading
  , parsedMerchantName :: String
  }
  deriving stock (Eq, Show)
