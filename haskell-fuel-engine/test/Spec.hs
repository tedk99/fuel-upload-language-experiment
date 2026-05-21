module Main (main) where

import Data.List.NonEmpty (NonEmpty (..))
import FuelUpload.Engine
import Test.Hspec
import Test.Hspec.QuickCheck (prop)
import Test.QuickCheck

main :: IO ()
main =
  hspec do
    describe "classifyRow" do
      it "accepts a unique valid row" do
        classifyRow defaultConfig Normal validUniqueContext
          `shouldBe` Accepted validTransaction

      it "rejects validation errors with a typed reason and no accepted transaction" do
        let row = validRow {parsedQuantity = FuelQuantity 0}
            decision = classifyRow defaultConfig Normal (uniqueContext row)
        decision
          `shouldBe` Rejected
            RejectedRow
              { rejectedRow = row
              , rejectionReason = RowFailedValidation (QuantityMustBePositive (FuelQuantity 0) :| [])
              }

      it "accepts rows with warnings because warnings do not block upload" do
        let row =
              validRow
                { parsedQuantity = FuelQuantity 75
                , parsedAmount = MoneyAmount 150
                , parsedOdometer = OdometerReading 200000
                }
            transaction = validTransactionFor row
        classifyRow defaultConfig Normal (uniqueContext row)
          `shouldBe` AcceptedWithWarnings
            transaction
            ( QuantityAboveWarningThreshold (FuelQuantity 75) (FuelQuantity 60)
                :| [ AmountAboveWarningThreshold (MoneyAmount 150) (MoneyAmount 100)
                   , OdometerAboveWarningThreshold (OdometerReading 200000) (OdometerReading 150000)
                   ]
            )

      it "returns fatal when vehicle lookup fails fatally" do
        let fatalError = VehicleLookupUnavailable (RowNumber 7)
            rowContext =
              RowContext
                { contextRow = validRow
                , contextVehicleLookup = VehicleLookupFatal fatalError
                , contextDuplicateCheck = DuplicateCheckSucceeded UniqueRow
                }
        classifyRow defaultConfig Normal rowContext `shouldBe` Fatal fatalError

      it "returns fatal when duplicate check fails fatally" do
        let fatalError = DuplicateCheckUnavailable (RowNumber 8)
            rowContext =
              RowContext
                { contextRow = validRow
                , contextVehicleLookup = VehicleFound validVehicle
                , contextDuplicateCheck = DuplicateCheckFatal fatalError
                }
        classifyRow defaultConfig Normal rowContext `shouldBe` Fatal fatalError

      it "rejects missing vehicles with a typed reason" do
        let registration = Registration "MISSING"
            row = validRow {parsedRegistration = registration}
            rowContext =
              RowContext
                { contextRow = row
                , contextVehicleLookup = VehicleMissing registration
                , contextDuplicateCheck = DuplicateCheckSucceeded UniqueRow
                }
        classifyRow defaultConfig Normal rowContext
          `shouldBe` Rejected
            RejectedRow
              { rejectedRow = row
              , rejectionReason = VehicleWasNotFound registration
              }

    describe "duplicate policy" do
      it "skips duplicates in Normal mode" do
        classifyRow defaultConfig Normal (duplicateContext finalizedAttempt)
          `shouldBe` SkippedDuplicate
            SkippedDuplicateInfo
              { skippedRow = validRow
              , duplicateSkipReason = AlreadyFinalized (TransactionId "previous")
              }

      it "skips Retry duplicates unless the previous attempt is explicitly retryable" do
        classifyRow defaultConfig Retry (duplicateContext notRetryableAttempt)
          `shouldBe` SkippedDuplicate
            SkippedDuplicateInfo
              { skippedRow = validRow
              , duplicateSkipReason = PreviousAttemptNotRetryable (TransactionId "previous")
              }

      it "accepts Retry duplicates when the previous attempt is retryable" do
        classifyRow defaultConfig Retry (duplicateContext retryableAttempt)
          `shouldBe` Accepted validTransaction

      it "accepts Recovery duplicates only before canonical finalization" do
        classifyRow defaultConfig Recovery (duplicateContext preCanonicalAttempt)
          `shouldBe` Accepted validTransaction

      it "rejects Recovery duplicates after canonicalization with a typed reason" do
        classifyRow defaultConfig Recovery (duplicateContext retryableAttempt)
          `shouldBe` Rejected
            RejectedRow
              { rejectedRow = validRow
              , rejectionReason =
                  DuplicateCannotBeUploaded
                    Recovery
                    (PreviousAttemptCanonicalized (TransactionId "previous"))
              }

    describe "classifyBatch" do
      it "derives summary counts from per-row decisions" do
        let warningRow = validRow {parsedRowNumber = RowNumber 2, parsedExternalRowId = ExternalRowId "row-2", parsedAmount = MoneyAmount 150}
            rejectedRow = validRow {parsedRowNumber = RowNumber 3, parsedExternalRowId = ExternalRowId "row-3", parsedQuantity = FuelQuantity 0}
            fatalError = DuplicateCheckUnavailable (RowNumber 4)
            fatalContext =
              RowContext
                { contextRow = validRow {parsedRowNumber = RowNumber 4, parsedExternalRowId = ExternalRowId "row-4"}
                , contextVehicleLookup = VehicleFound validVehicle
                , contextDuplicateCheck = DuplicateCheckFatal fatalError
                }
            batch =
              classifyBatch
                defaultConfig
                Normal
                [ validUniqueContext
                , uniqueContext warningRow
                , duplicateContext finalizedAttempt
                , uniqueContext rejectedRow
                , fatalContext
                ]
        batchSummary batch
          `shouldBe` BatchSummary
            { summaryAccepted = 2
            , summaryAcceptedWithWarnings = 1
            , summarySkippedDuplicates = 1
            , summaryRejected = 1
            , summaryFatal = 1
            , summaryTotalRows = 5
            }
        batchOutcome batch `shouldBe` BatchBlockedByFatal (fatalError :| [])

      it "does not block a batch when accepted rows only have warnings" do
        let warningRow = validRow {parsedAmount = MoneyAmount 150}
            batch = classifyBatch defaultConfig Normal [uniqueContext warningRow]
        batchOutcome batch `shouldBe` BatchUploadable
        summaryAcceptedWithWarnings (batchSummary batch) `shouldBe` 1

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

validTransaction :: FuelTransaction
validTransaction =
  validTransactionFor validRow

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

validUniqueContext :: RowContext
validUniqueContext =
  uniqueContext validRow

uniqueContext :: ParsedFuelRow -> RowContext
uniqueContext row =
  RowContext
    { contextRow = row
    , contextVehicleLookup = VehicleFound validVehicle
    , contextDuplicateCheck = DuplicateCheckSucceeded UniqueRow
    }

duplicateContext :: PreviousAttempt -> RowContext
duplicateContext previousAttempt =
  RowContext
    { contextRow = validRow
    , contextVehicleLookup = VehicleFound validVehicle
    , contextDuplicateCheck = DuplicateCheckSucceeded (DuplicateOf previousAttempt)
    }

finalizedAttempt :: PreviousAttempt
finalizedAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = Canonicalized
    , previousFinalizationState = Finalized
    }

retryableAttempt :: PreviousAttempt
retryableAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = Canonicalized
    , previousFinalizationState = FailedRetryable
    }

notRetryableAttempt :: PreviousAttempt
notRetryableAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = Canonicalized
    , previousFinalizationState = FailedNotRetryable
    }

preCanonicalAttempt :: PreviousAttempt
preCanonicalAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = FailedBeforeCanonicalization
    , previousFinalizationState = FailedRetryable
    }
