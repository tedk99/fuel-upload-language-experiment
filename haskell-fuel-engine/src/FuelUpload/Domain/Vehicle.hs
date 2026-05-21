module FuelUpload.Domain.Vehicle
  ( Vehicle (..)
  , VehicleLookupResult (..)
  ) where

import FuelUpload.Domain.Primitive

data Vehicle = Vehicle
  { vehicleId :: VehicleId
  , vehicleRegistration :: Registration
  }
  deriving stock (Eq, Show)

data VehicleLookupResult
  = VehicleFound Vehicle
  | VehicleMissing Registration
  | VehicleLookupFatal FatalError
  deriving stock (Eq, Show)
