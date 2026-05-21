module Test.Main where

import Prelude (class Eq, Unit, discard, negate, pure, unit, (<>), (==))

import Effect (Effect)
import Effect.Console (log)
import Effect.Exception (throw)
import Fuel.Engine
  ( BatchDecision(..)
  , BatchOutcome(..)
  , BatchSummary(..)
  , CanonicalTimestamp(..)
  , CostPolicy(..)
  , CostWarningPolicy(..)
  , DuplicateCheckResult(..)
  , DuplicateSkipReason(..)
  , ExternalReference(..)
  , FatalError(..)
  , ParsedFuelRow(..)
  , PreviousAttempt(..)
  , QuantityPolicy(..)
  , RejectedRow(..)
  , RejectionReason(..)
  , RetryableFailure(..)
  , RowDecision(..)
  , RowId(..)
  , SkippedDuplicate(..)
  , Transaction(..)
  , TransactionId(..)
  , UploadMode(..)
  , ValidationConfig(..)
  , ValidationError(..)
  , ValidationErrors(..)
  , Vehicle(..)
  , VehicleId(..)
  , VehicleKey(..)
  , VehicleLookupResult(..)
  , VehicleStatus(..)
  , VehicleStatusPolicy(..)
  , VolumeWarningPolicy(..)
  , Warning(..)
  , Warnings(..)
  , classifyBatch
  , classifyRow
  , defaultValidationConfig
  )

main :: Effect Unit
main = do
  testAcceptsValidUniqueRow
  testRejectsValidationErrors
  testRejectsMissingVehicle
  testDuplicateNormalSkipped
  testDuplicateRetryBehavior
  testDuplicateRecoveryBehavior
  testRetryableDuplicateStillRejectsValidationErrors
  testWarningsDoNotBlockAcceptedTransaction
  testFatalBlocksBatch
  testDuplicateCheckFatalBlocksBatch
  testDerivedSummaryCounts
  log "All tests passed."

testAcceptsValidUniqueRow :: Effect Unit
testAcceptsValidUniqueRow =
  assertEqual
    "valid unique normal row is accepted"
    (AcceptedTransaction (expectedTransaction Normal baseRow activeVehicle))
    (classifyRow defaultValidationConfig Normal baseRow (VehicleFound activeVehicle) UniqueTransaction)

testRejectsValidationErrors :: Effect Unit
testRejectsValidationErrors =
  let
    row =
      fuelRow 1 "fuel-1" 0.0 (-1.0)

    expectedErrors =
      ValidationErrors (InvalidFuelQuantity 0.0) [ InvalidTotalCost (-1.0) ]
  in
    assertEqual
      "invalid row is rejected with typed validation errors"
      (RejectedRowDecision (RejectedRow { row, reason: RejectedByValidation expectedErrors }))
      (classifyRow defaultValidationConfig Normal row (VehicleFound activeVehicle) UniqueTransaction)

testRejectsMissingVehicle :: Effect Unit
testRejectsMissingVehicle =
  let
    missingKey =
      VehicleKey "missing"
  in
    assertEqual
      "missing vehicle is a typed row rejection"
      (RejectedRowDecision (RejectedRow { row: baseRow, reason: RejectedVehicleMissing missingKey }))
      (classifyRow defaultValidationConfig Normal baseRow (VehicleNotFound missingKey) UniqueTransaction)

testDuplicateNormalSkipped :: Effect Unit
testDuplicateNormalSkipped =
  let
    previous =
      PreviousCanonicalTransaction (TransactionId "tx-previous")
  in
    assertEqual
      "normal mode skips any duplicate"
      (SkippedDuplicateRow (SkippedDuplicate { row: baseRow, reason: DuplicateInNormalMode previous }))
      (classifyRow defaultValidationConfig Normal baseRow (VehicleFound activeVehicle) (DuplicateTransaction previous))

testDuplicateRetryBehavior :: Effect Unit
testDuplicateRetryBehavior = do
  let
    retryable =
      PreviousRetryableFailure RetryableWriteTimeout

    finalized =
      PreviousCanonicalTransaction (TransactionId "tx-final")

  assertEqual
    "retry mode accepts duplicate only when previous attempt is explicitly retryable"
    (AcceptedTransaction (expectedTransaction Retry baseRow activeVehicle))
    (classifyRow defaultValidationConfig Retry baseRow (VehicleFound activeVehicle) (DuplicateTransaction retryable))

  assertEqual
    "retry mode skips finalized duplicate"
    (SkippedDuplicateRow (SkippedDuplicate { row: baseRow, reason: DuplicateInRetryModeNotRetryable finalized }))
    (classifyRow defaultValidationConfig Retry baseRow (VehicleFound activeVehicle) (DuplicateTransaction finalized))

testDuplicateRecoveryBehavior :: Effect Unit
testDuplicateRecoveryBehavior = do
  let
    beforeCanonical =
      PreviousFailedBeforeCanonicalFinalization (TransactionConstructionFailed "write timed out")

    afterCanonical =
      PreviousFailedAfterCanonicalFinalization (DuplicateCheckUnavailable "audit unavailable")

  assertEqual
    "recovery mode accepts duplicate only after pre-canonical failure"
    (AcceptedTransaction (expectedTransaction Recovery baseRow activeVehicle))
    (classifyRow defaultValidationConfig Recovery baseRow (VehicleFound activeVehicle) (DuplicateTransaction beforeCanonical))

  assertEqual
    "recovery mode skips duplicate after canonical finalization"
    (SkippedDuplicateRow (SkippedDuplicate { row: baseRow, reason: DuplicateInRecoveryModeNotPreCanonicalFailure afterCanonical }))
    (classifyRow defaultValidationConfig Recovery baseRow (VehicleFound activeVehicle) (DuplicateTransaction afterCanonical))

testRetryableDuplicateStillRejectsValidationErrors :: Effect Unit
testRetryableDuplicateStillRejectsValidationErrors =
  let
    row =
      fuelRow 1 "fuel-1" 0.0 84.0

    previous =
      PreviousRetryableFailure RetryableWriteTimeout
  in
    assertEqual
      "retryable duplicate with validation errors is rejected, not accepted"
      (RejectedRowDecision (RejectedRow { row, reason: RejectedByValidation (ValidationErrors (InvalidFuelQuantity 0.0) []) }))
      (classifyRow defaultValidationConfig Retry row (VehicleFound activeVehicle) (DuplicateTransaction previous))

testWarningsDoNotBlockAcceptedTransaction :: Effect Unit
testWarningsDoNotBlockAcceptedTransaction =
  let
    config =
      ValidationConfig
        { quantityPolicy: PositiveLitersRequired
        , costPolicy: NonNegativeTotalCostRequired
        , vehicleStatusPolicy: WarnInactiveVehicles
        , volumeWarningPolicy: WarnWhenLitersExceed 60.0
        , costWarningPolicy: WarnWhenTotalCostExceeds 100.0
        }

    row =
      fuelRow 1 "fuel-1" 70.0 120.0

    expectedWarnings =
      Warnings
        (FuelQuantityAboveReviewLimit 70.0)
        [ TotalCostAboveReviewLimit 120.0
        , InactiveVehicleAllowed (VehicleId "vehicle-1")
        ]
  in
    assertEqual
      "warnings travel with an accepted transaction"
      (WarningWithTransaction (expectedTransaction Normal row inactiveVehicle) expectedWarnings)
      (classifyRow config Normal row (VehicleFound inactiveVehicle) UniqueTransaction)

testFatalBlocksBatch :: Effect Unit
testFatalBlocksBatch =
  let
    fatal =
      VehicleLookupUnavailable "vehicle service unavailable"

    batch =
      classifyBatch defaultValidationConfig Normal
        [ { row: baseRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: UniqueTransaction
          }
        , { row: fuelRow 2 "fuel-2" 42.0 84.0
          , vehicleLookup: VehicleLookupFatal fatal
          , duplicateCheck: UniqueTransaction
          }
        ]

    expected =
      BatchDecision
        { summary:
            BatchSummary
              { totalRows: 2
              , acceptedTransactions: 1
              , warningTransactions: 0
              , skippedDuplicates: 0
              , rejectedRows: 0
              , fatalErrors: 1
              }
        , outcome: BatchBlockedByFatals [ fatal ]
        , rowDecisions:
            [ AcceptedTransaction (expectedTransaction Normal baseRow activeVehicle)
            , FatalProcessingError fatal
            ]
        }
  in
    assertEqual "fatal errors block the entire batch" expected batch

testDuplicateCheckFatalBlocksBatch :: Effect Unit
testDuplicateCheckFatalBlocksBatch =
  let
    fatal =
      DuplicateCheckUnavailable "duplicate service unavailable"

    batch =
      classifyBatch defaultValidationConfig Normal
        [ { row: baseRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: DuplicateCheckFatal fatal
          }
        ]

    expected =
      BatchDecision
        { summary:
            BatchSummary
              { totalRows: 1
              , acceptedTransactions: 0
              , warningTransactions: 0
              , skippedDuplicates: 0
              , rejectedRows: 0
              , fatalErrors: 1
              }
        , outcome: BatchBlockedByFatals [ fatal ]
        , rowDecisions: [ FatalProcessingError fatal ]
        }
  in
    assertEqual "duplicate check fatal blocks the batch" expected batch

testDerivedSummaryCounts :: Effect Unit
testDerivedSummaryCounts =
  let
    warningConfig =
      ValidationConfig
        { quantityPolicy: PositiveLitersRequired
        , costPolicy: NonNegativeTotalCostRequired
        , vehicleStatusPolicy: RejectInactiveVehicles
        , volumeWarningPolicy: WarnWhenLitersExceed 50.0
        , costWarningPolicy: NoCostWarning
        }

    duplicatePrevious =
      PreviousCanonicalTransaction (TransactionId "already-final")

    warningRow =
      fuelRow 2 "fuel-2" 75.0 84.0

    duplicateRow =
      fuelRow 3 "fuel-3" 42.0 84.0

    rejectedRow =
      fuelRow 4 "fuel-4" (-2.0) 84.0

    batch =
      classifyBatch warningConfig Normal
        [ { row: baseRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: UniqueTransaction
          }
        , { row: warningRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: UniqueTransaction
          }
        , { row: duplicateRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: DuplicateTransaction duplicatePrevious
          }
        , { row: rejectedRow
          , vehicleLookup: VehicleFound activeVehicle
          , duplicateCheck: UniqueTransaction
          }
        ]

    expectedSummary =
      BatchSummary
        { totalRows: 4
        , acceptedTransactions: 2
        , warningTransactions: 1
        , skippedDuplicates: 1
        , rejectedRows: 1
        , fatalErrors: 0
        }
  in
    case batch of
      BatchDecision actual ->
        assertEqual "summary is derived from per-row decisions" expectedSummary actual.summary

baseRow :: ParsedFuelRow
baseRow =
  fuelRow 1 "fuel-1" 42.0 84.0

fuelRow :: Int -> String -> Number -> Number -> ParsedFuelRow
fuelRow rowNumber reference liters totalCost =
  ParsedFuelRow
    { rowId: RowId rowNumber
    , vehicleKey: VehicleKey "fleet-1"
    , externalReference: ExternalReference reference
    , occurredAt: CanonicalTimestamp "2026-05-21T09:00:00Z"
    , liters
    , totalCost
    }

activeVehicle :: Vehicle
activeVehicle =
  Vehicle
    { vehicleId: VehicleId "vehicle-1"
    , vehicleKey: VehicleKey "fleet-1"
    , status: ActiveVehicle
    }

inactiveVehicle :: Vehicle
inactiveVehicle =
  Vehicle
    { vehicleId: VehicleId "vehicle-1"
    , vehicleKey: VehicleKey "fleet-1"
    , status: InactiveVehicle
    }

expectedTransaction :: UploadMode -> ParsedFuelRow -> Vehicle -> Transaction
expectedTransaction mode (ParsedFuelRow row) (Vehicle vehicle) =
  Transaction
    { transactionId: TransactionId case row.externalReference of
        ExternalReference reference -> reference
    , sourceRowId: row.rowId
    , vehicleId: vehicle.vehicleId
    , externalReference: row.externalReference
    , occurredAt: row.occurredAt
    , liters: row.liters
    , totalCost: row.totalCost
    , uploadMode: mode
    }

assertEqual :: forall a. Eq a => String -> a -> a -> Effect Unit
assertEqual label expected actual =
  if actual == expected then
    pure unit
  else
    throw ("failed: " <> label)
