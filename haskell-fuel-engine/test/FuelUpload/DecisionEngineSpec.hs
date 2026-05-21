module Main (main) where

import Data.List.NonEmpty (NonEmpty (..))
import qualified FuelUpload.Api as Api
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

    describe "DTO API boundary" do
      it "maps and classifies a valid DTO request" do
        Api.classifyUploadDto validDtoRequest
          `shouldBe` Right
            Api.FuelUploadResponseDto
              { Api.responseDecisions =
                  [ Api.FuelUploadDecisionDto
                      { Api.decisionRowNumber = 1
                      , Api.decisionOutcome = "accepted"
                      , Api.decisionTransactionId = Just "row-1:vehicle-1"
                      , Api.decisionVehicleId = Just "vehicle-1"
                      , Api.decisionWarnings = []
                      , Api.decisionQuarantineReasons = []
                      , Api.decisionRejectionReason = Nothing
                      , Api.decisionDuplicateSkipReason = Nothing
                      , Api.decisionFatalError = Nothing
                      }
                  ]
              , Api.responseAccepted = 1
              , Api.responseAcceptedWithWarnings = 0
              , Api.responseQuarantined = 0
              , Api.responseSkippedDuplicates = 0
              , Api.responseRejected = 0
              , Api.responseFatal = 0
              , Api.responseTotalRows = 1
              , Api.responseBlocked = False
              }

      it "returns typed mapping errors for invalid DTOs" do
        let request = validDtoRequest {Api.dtoUploadMode = "eventual"}
        Api.toDomainRequest request
          `shouldBe` Left
            [ Api.FuelUploadMappingError
                { Api.mappingErrorCode = Api.InvalidUploadMode
                , Api.mappingErrorField = "uploadMode"
                , Api.mappingErrorDetail = "Unsupported upload mode 'eventual'."
                }
            ]

      it "represents accepted rejected skipped quarantined and fatal decisions" do
        let rows =
              [ validDtoRow
              , validDtoRow {Api.dtoRowNumber = 2, Api.dtoExternalRowId = "row-2", Api.dtoAmount = 150}
              , validDtoRow {Api.dtoRowNumber = 3, Api.dtoExternalRowId = "row-3", Api.dtoQuantity = 33}
              , validDtoRow
                  { Api.dtoRowNumber = 4
                  , Api.dtoExternalRowId = "row-4"
                  , Api.dtoDuplicateStatus = "duplicate"
                  , Api.dtoPreviousTransactionId = "previous"
                  , Api.dtoCanonicalizationState = "with_transaction_key"
                  , Api.dtoFinalizationState = "finalized"
                  }
              , validDtoRow {Api.dtoRowNumber = 5, Api.dtoExternalRowId = "row-5", Api.dtoQuantity = 0}
              , validDtoRow
                  { Api.dtoRowNumber = 6
                  , Api.dtoExternalRowId = "row-6"
                  , Api.dtoDuplicateStatus = "fatal"
                  }
              ]
            request = validDtoRequest {Api.dtoRows = rows}
        case Api.classifyUploadDto request of
          Right response -> do
            let outcomes = fmap Api.decisionOutcome (Api.responseDecisions response)
            outcomes `shouldContain` ["accepted"]
            outcomes `shouldContain` ["accepted_with_warnings"]
            outcomes `shouldContain` ["quarantined"]
            outcomes `shouldContain` ["skipped_duplicate"]
            outcomes `shouldContain` ["rejected"]
            outcomes `shouldContain` ["fatal"]
          Left errors -> expectationFailure ("Expected DTO to classify, got " <> show errors)

      it "uses the domain batch summary without recomputing it" do
        let decision =
              BatchDecision
                { batchRows = [Accepted validTransaction]
                , batchSummary =
                    BatchSummary
                      { summaryAccepted = 42
                      , summaryAcceptedWithWarnings = 5
                      , summaryQuarantined = 4
                      , summarySkippedDuplicates = 3
                      , summaryRejected = 2
                      , summaryFatal = 1
                      , summaryTotalRows = 99
                      }
                , batchOutcome = BatchUploadable
                }
            response = Api.toResponseDto decision
        Api.responseTotalRows response `shouldBe` 99
        Api.responseAccepted response `shouldBe` 42
        Api.responseQuarantined response `shouldBe` 4

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

validDtoRequest :: Api.FuelUploadRequestDto
validDtoRequest =
  Api.FuelUploadRequestDto
    { Api.dtoUploadMode = "normal"
    , Api.dtoMaximumQuantity = 100
    , Api.dtoMaximumAmount = 200
    , Api.dtoHighQuantityWarning = 60
    , Api.dtoHighAmountWarning = 100
    , Api.dtoHighOdometerWarning = 150000
    , Api.dtoSuspiciousQuantity = 33
    , Api.dtoSuspiciousAmount = 99
    , Api.dtoRows = [validDtoRow]
    }

validDtoRow :: Api.FuelUploadRowDto
validDtoRow =
  Api.FuelUploadRowDto
    { Api.dtoRowNumber = 1
    , Api.dtoExternalRowId = "row-1"
    , Api.dtoRegistration = "AB12 CDE"
    , Api.dtoQuantity = 40
    , Api.dtoAmount = 80
    , Api.dtoOdometer = 42000
    , Api.dtoMerchantName = "Depot Fuel"
    , Api.dtoVehicleLookupStatus = "found"
    , Api.dtoVehicleId = "vehicle-1"
    , Api.dtoVehicleLookupError = ""
    , Api.dtoDuplicateStatus = "unique"
    , Api.dtoPreviousTransactionId = ""
    , Api.dtoCanonicalizationState = ""
    , Api.dtoFinalizationState = ""
    , Api.dtoDuplicateError = ""
    }
