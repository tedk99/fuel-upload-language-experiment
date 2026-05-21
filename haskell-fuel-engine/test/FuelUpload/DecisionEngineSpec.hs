module Main (main) where

import Data.List.NonEmpty (NonEmpty (..))
import FuelUpload.DecisionEngine
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row
import FuelUpload.Domain.Vehicle
import FuelUpload.Properties
import Test.Hspec

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

      it "quarantines a suspicious row with a typed reason" do
        let row = validRow {parsedMerchantName = "Manual fuel entry"}
            transaction = validTransactionFor row
        classifyRow defaultConfig Normal (uniqueContext row)
          `shouldBe` Quarantined transaction (SuspiciousMerchantName :| [])

      it "rejects validation errors instead of quarantining" do
        let row = validRow {parsedQuantity = FuelQuantity 0, parsedMerchantName = "manual review"}
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

      it "does not turn warnings into quarantine" do
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

      it "skips Normal duplicates instead of quarantining" do
        let row = validRow {parsedMerchantName = "test merchant"}
            rowContext =
              RowContext
                { contextRow = row
                , contextVehicleLookup = VehicleFound validVehicle
                , contextDuplicateCheck = DuplicateCheckSucceeded (DuplicateOf finalizedAttempt)
                }
        classifyRow defaultConfig Normal rowContext
          `shouldBe` SkippedDuplicate
            SkippedDuplicateInfo
              { skippedRow = row
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

      it "accepts ConservativeRecovery duplicates only before canonical finalization" do
        classifyRow defaultConfig ConservativeRecovery (duplicateContext preCanonicalAttempt)
          `shouldBe` Accepted validTransaction

      it "skips ConservativeRecovery duplicates after canonicalization" do
        classifyRow defaultConfig ConservativeRecovery (duplicateContext retryableAttempt)
          `shouldBe` SkippedDuplicate
            SkippedDuplicateInfo
              { skippedRow = validRow
              , duplicateSkipReason = PreviousAttemptCanonicalized (TransactionId "previous")
              }

      it "accepts AggressiveRecovery failed-after-canonicalization duplicates only without a canonical transaction key" do
        classifyRow defaultConfig AggressiveRecovery (duplicateContext retryableAttemptWithoutCanonicalKey)
          `shouldBe` Accepted validTransaction

        classifyRow defaultConfig AggressiveRecovery (duplicateContext retryableAttempt)
          `shouldBe` SkippedDuplicate
            SkippedDuplicateInfo
              { skippedRow = validRow
              , duplicateSkipReason = PreviousAttemptCanonicalized (TransactionId "previous")
              }

      it "quarantines suspicious duplicates accepted by recovery modes" do
        let row = validRow {parsedMerchantName = "manual review"}
            transaction = validTransactionFor row
            conservativeContext =
              (duplicateContext preCanonicalAttempt) {contextRow = row}
            aggressiveContext =
              (duplicateContext retryableAttemptWithoutCanonicalKey) {contextRow = row}
        classifyRow defaultConfig ConservativeRecovery conservativeContext
          `shouldBe` Quarantined transaction (SuspiciousMerchantName :| [])
        classifyRow defaultConfig AggressiveRecovery aggressiveContext
          `shouldBe` Quarantined transaction (SuspiciousMerchantName :| [])

    describe "classifyBatch" do
      it "derives summary counts from per-row decisions" do
        let warningRow = validRow {parsedRowNumber = RowNumber 2, parsedExternalRowId = ExternalRowId "row-2", parsedAmount = MoneyAmount 150}
            rejectedRow = validRow {parsedRowNumber = RowNumber 3, parsedExternalRowId = ExternalRowId "row-3", parsedQuantity = FuelQuantity 0}
            fatalError = DuplicateCheckUnavailable (RowNumber 4)
            quarantinedRow = validRow {parsedRowNumber = RowNumber 5, parsedExternalRowId = ExternalRowId "row-5", parsedQuantity = FuelQuantity 33}
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
                , uniqueContext quarantinedRow
                , fatalContext
                ]
        batchSummary batch
          `shouldBe` BatchSummary
            { summaryAccepted = 2
            , summaryAcceptedWithWarnings = 1
            , summaryQuarantined = 1
            , summarySkippedDuplicates = 1
            , summaryRejected = 1
            , summaryFatal = 1
            , summaryTotalRows = 6
            }
        batchOutcome batch `shouldBe` BatchBlockedByFatal (fatalError :| [])

      it "does not block a batch when a row is quarantined" do
        let quarantinedRow = validRow {parsedRowNumber = RowNumber 2, parsedExternalRowId = ExternalRowId "row-2", parsedAmount = MoneyAmount 99}
            batch = classifyBatch defaultConfig Normal [validUniqueContext, uniqueContext quarantinedRow]
        batchOutcome batch `shouldBe` BatchUploadable
        summaryAccepted (batchSummary batch) `shouldBe` 1
        summaryQuarantined (batchSummary batch) `shouldBe` 1
        batchRows batch
          `shouldSatisfy` any
            ( \decision ->
                case decision of
                  Quarantined _ reasons -> SuspiciousCostPattern `elem` reasons
                  _ -> False
            )

      it "does not block a batch when accepted rows only have warnings" do
        let warningRow = validRow {parsedAmount = MoneyAmount 150}
            batch = classifyBatch defaultConfig Normal [uniqueContext warningRow]
        batchOutcome batch `shouldBe` BatchUploadable
        summaryAcceptedWithWarnings (batchSummary batch) `shouldBe` 1

    propertySpec

defaultConfig :: ValidationConfig
defaultConfig =
  ValidationConfig
    { maximumQuantity = FuelQuantity 100
    , maximumAmount = MoneyAmount 200
    , highQuantityWarning = FuelQuantity 60
    , highAmountWarning = MoneyAmount 100
    , highOdometerWarning = OdometerReading 150000
    , suspiciousQuantity = FuelQuantity 33
    , suspiciousAmount = MoneyAmount 99
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
    , parsedMerchantName = "Depot Fuel"
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
    , previousCanonicalizationState = CanonicalizedWithTransactionKey (TransactionId "previous")
    , previousFinalizationState = Finalized
    }

retryableAttempt :: PreviousAttempt
retryableAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = CanonicalizedWithTransactionKey (TransactionId "previous")
    , previousFinalizationState = FailedRetryable
    }

retryableAttemptWithoutCanonicalKey :: PreviousAttempt
retryableAttemptWithoutCanonicalKey =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = CanonicalizedWithoutTransactionKey
    , previousFinalizationState = FailedRetryable
    }

notRetryableAttempt :: PreviousAttempt
notRetryableAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = CanonicalizedWithTransactionKey (TransactionId "previous")
    , previousFinalizationState = FailedNotRetryable
    }

preCanonicalAttempt :: PreviousAttempt
preCanonicalAttempt =
  PreviousAttempt
    { previousTransactionId = TransactionId "previous"
    , previousCanonicalizationState = FailedBeforeCanonicalization
    , previousFinalizationState = FailedRetryable
    }
