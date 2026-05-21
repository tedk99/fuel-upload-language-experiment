module FuelUpload.Properties
  ( propertySpec
  ) where

import Data.List.NonEmpty (NonEmpty (..))
import FuelUpload.DecisionEngine
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row
import FuelUpload.Domain.Vehicle
import FuelUpload.Summary
import Test.Hspec
import Test.Hspec.QuickCheck (prop)
import Test.QuickCheck

propertySpec :: Spec
propertySpec =
  describe "properties" do
    prop "summary total is the number of row decisions" \(Decisions decisions) ->
      summaryTotalRows (summarizeRows decisions) == length decisions

    prop "summary count partitions always add up to total" \(Decisions decisions) ->
      let summary = summarizeRows decisions
       in summaryAccepted summary
            + summarySkippedDuplicates summary
            + summaryRejected summary
            + summaryFatal summary
            == summaryTotalRows summary

    prop "fatal row decisions block the batch" \(NonEmptyFatalContexts contexts) ->
      case batchOutcome (classifyBatch defaultConfig Normal contexts) of
        BatchBlockedByFatal _ -> property True
        BatchUploadable -> counterexample "expected fatal to block batch" False

    prop "accepted row decisions never contain validation errors" do
      forAll acceptedRowGen \row ->
        case classifyRow defaultConfig Normal (uniqueContext row) of
          Accepted _ -> property True
          AcceptedWithWarnings _ _ -> property True
          other -> counterexample ("expected accepted decision, got " <> show other) False

newtype Decisions = Decisions [RowDecision]
  deriving stock (Show)

instance Arbitrary Decisions where
  arbitrary = Decisions <$> listOf arbitraryDecision

arbitraryDecision :: Gen RowDecision
arbitraryDecision =
  oneof
    [ Accepted <$> arbitraryTransaction
    , AcceptedWithWarnings <$> arbitraryTransaction <*> arbitraryWarnings
    , SkippedDuplicate <$> arbitrarySkippedDuplicate
    , Rejected <$> arbitraryRejectedRow
    , Fatal <$> arbitraryFatalError
    ]

newtype NonEmptyFatalContexts = NonEmptyFatalContexts [RowContext]
  deriving stock (Show)

instance Arbitrary NonEmptyFatalContexts where
  arbitrary = do
    prefix <- listOf arbitraryUploadableContext
    suffix <- listOf arbitraryUploadableContext
    fatal <- arbitraryFatalContext
    pure (NonEmptyFatalContexts (prefix <> [fatal] <> suffix))

arbitraryUploadableContext :: Gen RowContext
arbitraryUploadableContext =
  uniqueContext <$> arbitraryValidRow

acceptedRowGen :: Gen ParsedFuelRow
acceptedRowGen = do
  quantity <- FuelQuantity . fromInteger <$> chooseInteger (1, 100)
  amount <- MoneyAmount . fromInteger <$> chooseInteger (1, 200)
  odometer <- OdometerReading <$> chooseInteger (0, 300000)
  pure
    validRow
      { parsedQuantity = quantity
      , parsedAmount = amount
      , parsedOdometer = odometer
      }

arbitraryFatalContext :: Gen RowContext
arbitraryFatalContext =
  oneof
    [ do
        row <- arbitraryValidRow
        let fatalError = VehicleLookupUnavailable (parsedRowNumber row)
        pure
          RowContext
            { contextRow = row
            , contextVehicleLookup = VehicleLookupFatal fatalError
            , contextDuplicateCheck = DuplicateCheckSucceeded UniqueRow
            }
    , do
        row <- arbitraryValidRow
        let fatalError = DuplicateCheckUnavailable (parsedRowNumber row)
        pure
          RowContext
            { contextRow = row
            , contextVehicleLookup = VehicleFound validVehicle
            , contextDuplicateCheck = DuplicateCheckFatal fatalError
            }
    ]

arbitraryValidRow :: Gen ParsedFuelRow
arbitraryValidRow = do
  rowNumber <- RowNumber <$> chooseInt (1, 100000)
  externalId <- ExternalRowId . ("row-" <>) <$> vectorOf 8 (elements ['a' .. 'z'])
  quantity <- FuelQuantity . fromInteger <$> chooseInteger (1, 50)
  amount <- MoneyAmount . fromInteger <$> chooseInteger (1, 90)
  odometer <- OdometerReading <$> chooseInteger (0, 120000)
  pure
    ParsedFuelRow
      { parsedRowNumber = rowNumber
      , parsedExternalRowId = externalId
      , parsedRegistration = Registration "AB12 CDE"
      , parsedQuantity = quantity
      , parsedAmount = amount
      , parsedOdometer = odometer
      }

arbitraryTransaction :: Gen FuelTransaction
arbitraryTransaction = validTransactionFor <$> arbitraryValidRow

arbitraryWarnings :: Gen (NonEmpty ValidationWarning)
arbitraryWarnings =
  (QuantityAboveWarningThreshold (FuelQuantity 80) (FuelQuantity 60) :|)
    <$> listOf
      ( elements
          [ AmountAboveWarningThreshold (MoneyAmount 120) (MoneyAmount 100)
          , OdometerAboveWarningThreshold (OdometerReading 200000) (OdometerReading 150000)
          ]
      )

arbitrarySkippedDuplicate :: Gen SkippedDuplicate
arbitrarySkippedDuplicate = do
  row <- arbitraryValidRow
  pure
    SkippedDuplicateInfo
      { skippedRow = row
      , duplicateSkipReason = AlreadyFinalized (TransactionId "previous")
      }

arbitraryRejectedRow :: Gen RejectedRow
arbitraryRejectedRow = do
  row <- arbitraryValidRow
  pure
    RejectedRow
      { rejectedRow = row
      , rejectionReason = VehicleWasNotFound (parsedRegistration row)
      }

arbitraryFatalError :: Gen FatalError
arbitraryFatalError =
  oneof
    [ VehicleLookupUnavailable . RowNumber <$> chooseInt (1, 1000)
    , DuplicateCheckUnavailable . RowNumber <$> chooseInt (1, 1000)
    , CorruptParsedRow . RowNumber <$> chooseInt (1, 1000)
    ]

defaultConfig :: ValidationConfig
defaultConfig =
  ValidationConfig
    { maximumQuantity = FuelQuantity 100
    , maximumAmount = MoneyAmount 200
    , highQuantityWarning = FuelQuantity 60
    , highAmountWarning = MoneyAmount 100
    , highOdometerWarning = OdometerReading 150000
    }

validRow :: ParsedFuelRow
validRow =
  ParsedFuelRow
    { parsedRowNumber = RowNumber 1
    , parsedExternalRowId = ExternalRowId "row-1"
    , parsedRegistration = Registration "AB12 CDE"
    , parsedQuantity = FuelQuantity 40
    , parsedAmount = MoneyAmount 80
    , parsedOdometer = OdometerReading 42000
    }

validVehicle :: Vehicle
validVehicle =
  Vehicle
    { vehicleId = VehicleId "vehicle-1"
    , vehicleRegistration = Registration "AB12 CDE"
    }

validTransactionFor :: ParsedFuelRow -> FuelTransaction
validTransactionFor row =
  FuelTransaction
    { transactionId = TransactionId (externalRowIdValue (parsedExternalRowId row) <> ":vehicle-1")
    , transactionRowNumber = parsedRowNumber row
    , transactionVehicleId = VehicleId "vehicle-1"
    , transactionExternalRowId = parsedExternalRowId row
    , transactionQuantity = parsedQuantity row
    , transactionAmount = parsedAmount row
    , transactionOdometer = parsedOdometer row
    }

externalRowIdValue :: ExternalRowId -> String
externalRowIdValue (ExternalRowId value) = value

uniqueContext :: ParsedFuelRow -> RowContext
uniqueContext row =
  RowContext
    { contextRow = row
    , contextVehicleLookup = VehicleFound validVehicle
    , contextDuplicateCheck = DuplicateCheckSucceeded UniqueRow
    }
