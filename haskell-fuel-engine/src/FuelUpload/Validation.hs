module FuelUpload.Validation
  ( validationErrors
  , validationWarnings
  , quarantineReasons
  ) where

import Data.Char (toLower)
import Data.List (isInfixOf)
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row

validationErrors :: ValidationConfig -> ParsedFuelRow -> [ValidationError]
validationErrors config row =
  quantityErrors <> amountErrors <> odometerErrors
  where
    quantity = parsedQuantity row
    amount = parsedAmount row
    odometer = parsedOdometer row

    quantityErrors =
      positiveQuantityError quantity
        <> maximumQuantityError (maximumQuantity config) quantity

    amountErrors =
      positiveAmountError amount
        <> maximumAmountError (maximumAmount config) amount

    odometerErrors =
      nonNegativeOdometerError odometer

validationWarnings :: ValidationConfig -> ParsedFuelRow -> [ValidationWarning]
validationWarnings config row =
  highQuantityWarningFor config (parsedQuantity row)
    <> highAmountWarningFor config (parsedAmount row)
    <> highOdometerWarningFor config (parsedOdometer row)

quarantineReasons :: ValidationConfig -> ParsedFuelRow -> [QuarantineReason]
quarantineReasons config row =
  suspiciousMerchantName (parsedMerchantName row)
    <> suspiciousQuantityPattern config (parsedQuantity row)
    <> suspiciousAmountPattern config (parsedAmount row)

positiveQuantityError :: FuelQuantity -> [ValidationError]
positiveQuantityError quantity@(FuelQuantity value)
  | value <= 0 = [QuantityMustBePositive quantity]
  | otherwise = []

maximumQuantityError :: FuelQuantity -> FuelQuantity -> [ValidationError]
maximumQuantityError maximumAllowed quantity
  | quantity > maximumAllowed = [QuantityExceedsMaximum quantity maximumAllowed]
  | otherwise = []

positiveAmountError :: MoneyAmount -> [ValidationError]
positiveAmountError amount@(MoneyAmount value)
  | value <= 0 = [AmountMustBePositive amount]
  | otherwise = []

maximumAmountError :: MoneyAmount -> MoneyAmount -> [ValidationError]
maximumAmountError maximumAllowed amount
  | amount > maximumAllowed = [AmountExceedsMaximum amount maximumAllowed]
  | otherwise = []

nonNegativeOdometerError :: OdometerReading -> [ValidationError]
nonNegativeOdometerError odometer@(OdometerReading value)
  | value < 0 = [OdometerMustNotBeNegative odometer]
  | otherwise = []

highQuantityWarningFor :: ValidationConfig -> FuelQuantity -> [ValidationWarning]
highQuantityWarningFor config quantity
  | quantity > highQuantityWarning config =
      [QuantityAboveWarningThreshold quantity (highQuantityWarning config)]
  | otherwise =
      []

highAmountWarningFor :: ValidationConfig -> MoneyAmount -> [ValidationWarning]
highAmountWarningFor config amount
  | amount > highAmountWarning config =
      [AmountAboveWarningThreshold amount (highAmountWarning config)]
  | otherwise =
      []

highOdometerWarningFor :: ValidationConfig -> OdometerReading -> [ValidationWarning]
highOdometerWarningFor config odometer
  | odometer > highOdometerWarning config =
      [OdometerAboveWarningThreshold odometer (highOdometerWarning config)]
  | otherwise =
      []

suspiciousMerchantName :: String -> [QuarantineReason]
suspiciousMerchantName merchantName
  | any (`isInfixOf` lowered) ["test", "unknown", "manual"] =
      [SuspiciousMerchantName]
  | otherwise =
      []
  where
    lowered = fmap toLower merchantName

suspiciousQuantityPattern :: ValidationConfig -> FuelQuantity -> [QuarantineReason]
suspiciousQuantityPattern config quantity
  | quantity == suspiciousQuantity config = [SuspiciousQuantityPattern]
  | otherwise = []

suspiciousAmountPattern :: ValidationConfig -> MoneyAmount -> [QuarantineReason]
suspiciousAmountPattern config amount
  | amount == suspiciousAmount config = [SuspiciousCostPattern]
  | otherwise = []
