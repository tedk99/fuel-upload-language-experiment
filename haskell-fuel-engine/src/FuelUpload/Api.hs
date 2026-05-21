module FuelUpload.Api
  ( FuelUploadRequestDto (..)
  , FuelUploadRowDto (..)
  , RawFuelUploadRow (..)
  , ImportBatchRequest (..)
  , FuelUploadResponseDto (..)
  , FuelUploadDecisionDto (..)
  , FuelUploadMappingErrorCode (..)
  , FuelUploadMappingError (..)
  , FuelImportErrorCode (..)
  , FuelImportError (..)
  , DomainUploadRequest (..)
  , toFuelUploadRequestDto
  , toDomainRequest
  , toResponseDto
  , classifyUploadDto
  , classifyImportBatch
  ) where

import Data.Char (isDigit, toLower)
import Text.Read (readMaybe)
import FuelUpload.DecisionEngine (classifyBatch)
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row
import FuelUpload.Domain.Vehicle

data FuelUploadRequestDto = FuelUploadRequestDto
  { dtoUploadMode :: String
  , dtoMaximumQuantity :: Rational
  , dtoMaximumAmount :: Rational
  , dtoHighQuantityWarning :: Rational
  , dtoHighAmountWarning :: Rational
  , dtoHighOdometerWarning :: Integer
  , dtoSuspiciousQuantity :: Rational
  , dtoSuspiciousAmount :: Rational
  , dtoRows :: [FuelUploadRowDto]
  }
  deriving stock (Eq, Show)

data FuelUploadRowDto = FuelUploadRowDto
  { dtoRowNumber :: Int
  , dtoExternalRowId :: String
  , dtoRegistration :: String
  , dtoQuantity :: Rational
  , dtoAmount :: Rational
  , dtoOdometer :: Integer
  , dtoMerchantName :: String
  , dtoVehicleLookupStatus :: String
  , dtoVehicleId :: String
  , dtoVehicleLookupError :: String
  , dtoDuplicateStatus :: String
  , dtoPreviousTransactionId :: String
  , dtoCanonicalizationState :: String
  , dtoFinalizationState :: String
  , dtoDuplicateError :: String
  }
  deriving stock (Eq, Show)

data ImportBatchRequest = ImportBatchRequest
  { importUploadMode :: String
  , importMaximumQuantity :: String
  , importMaximumAmount :: String
  , importHighQuantityWarning :: String
  , importHighAmountWarning :: String
  , importHighOdometerWarning :: String
  , importSuspiciousQuantity :: String
  , importSuspiciousAmount :: String
  , importRows :: [RawFuelUploadRow]
  }
  deriving stock (Eq, Show)

data RawFuelUploadRow = RawFuelUploadRow
  { importRowNumber :: String
  , importTransactionDate :: String
  , importExternalRowId :: String
  , importRegistration :: String
  , importQuantity :: String
  , importAmount :: String
  , importOdometer :: String
  , importMerchantName :: String
  , importVehicleLookupStatus :: String
  , importVehicleId :: String
  , importVehicleLookupError :: String
  , importDuplicateStatus :: String
  , importPreviousTransactionId :: String
  , importCanonicalizationState :: String
  , importFinalizationState :: String
  , importDuplicateError :: String
  }
  deriving stock (Eq, Show)

data FuelUploadResponseDto = FuelUploadResponseDto
  { responseDecisions :: [FuelUploadDecisionDto]
  , responseAccepted :: Int
  , responseAcceptedWithWarnings :: Int
  , responseQuarantined :: Int
  , responseSkippedDuplicates :: Int
  , responseRejected :: Int
  , responseFatal :: Int
  , responseTotalRows :: Int
  , responseBlocked :: Bool
  }
  deriving stock (Eq, Show)

data FuelUploadDecisionDto = FuelUploadDecisionDto
  { decisionRowNumber :: Int
  , decisionOutcome :: String
  , decisionTransactionId :: Maybe String
  , decisionVehicleId :: Maybe String
  , decisionWarnings :: [String]
  , decisionQuarantineReasons :: [String]
  , decisionRejectionReason :: Maybe String
  , decisionDuplicateSkipReason :: Maybe String
  , decisionFatalError :: Maybe String
  }
  deriving stock (Eq, Show)

data FuelUploadMappingErrorCode
  = MissingRequiredField
  | InvalidRowNumber
  | InvalidUploadMode
  | InvalidVehicleLookupStatus
  | MissingVehicleLookupPayload
  | InvalidDuplicateStatus
  | InvalidCanonicalizationState
  | InvalidFinalizationState
  deriving stock (Eq, Show)

data FuelUploadMappingError = FuelUploadMappingError
  { mappingErrorCode :: FuelUploadMappingErrorCode
  , mappingErrorField :: String
  , mappingErrorDetail :: String
  }
  deriving stock (Eq, Show)

data FuelImportErrorCode
  = ImportMissingRows
  | ImportMissingRequiredCell
  | ImportInvalidNumber
  | ImportInvalidDate
  | ImportInvalidUploadMode
  deriving stock (Eq, Show)

data FuelImportError = FuelImportError
  { importErrorCode :: FuelImportErrorCode
  , importErrorField :: String
  , importErrorDetail :: String
  }
  deriving stock (Eq, Show)

data DomainUploadRequest = DomainUploadRequest
  { domainValidationConfig :: ValidationConfig
  , domainUploadMode :: UploadMode
  , domainRows :: [RowContext]
  }
  deriving stock (Eq, Show)

toDomainRequest :: FuelUploadRequestDto -> Either [FuelUploadMappingError] DomainUploadRequest
toDomainRequest dto =
  case parseMode "uploadMode" (dtoUploadMode dto) of
    Left modeErrors ->
      Left (modeErrors <> rowErrors)
    Right mode
      | null rowErrors ->
          Right
            DomainUploadRequest
              { domainValidationConfig =
                  ValidationConfig
                    { maximumQuantity = FuelQuantity (dtoMaximumQuantity dto)
                    , maximumAmount = MoneyAmount (dtoMaximumAmount dto)
                    , highQuantityWarning = FuelQuantity (dtoHighQuantityWarning dto)
                    , highAmountWarning = MoneyAmount (dtoHighAmountWarning dto)
                    , highOdometerWarning = OdometerReading (dtoHighOdometerWarning dto)
                    , suspiciousQuantity = FuelQuantity (dtoSuspiciousQuantity dto)
                    , suspiciousAmount = MoneyAmount (dtoSuspiciousAmount dto)
                    }
              , domainUploadMode = mode
              , domainRows = rows
              }
      | otherwise ->
          Left rowErrors
  where
    mappedRows = fmap (uncurry mapRow) (zip [0 :: Int ..] (dtoRows dto))
    rowErrors = concatMap errorsOf mappedRows
    rows = [row | Right row <- mappedRows]

classifyUploadDto :: FuelUploadRequestDto -> Either [FuelUploadMappingError] FuelUploadResponseDto
classifyUploadDto dto = do
  request <- toDomainRequest dto
  pure
    ( toResponseDto
        ( classifyBatch
            (domainValidationConfig request)
            (domainUploadMode request)
            (domainRows request)
        )
    )

classifyImportBatch :: ImportBatchRequest -> Either [FuelImportError] FuelUploadResponseDto
classifyImportBatch request = do
  dto <- toFuelUploadRequestDto request
  case classifyUploadDto dto of
    Right response -> Right response
    Left errors -> Left (fmap importErrorFromMappingError errors)

toFuelUploadRequestDto :: ImportBatchRequest -> Either [FuelImportError] FuelUploadRequestDto
toFuelUploadRequestDto request =
  case
    ( uploadMode
    , maximumQuantity
    , maximumAmount
    , highQuantity
    , highAmount
    , highOdometer
    , suspiciousQuantity
    , suspiciousAmount
    , rowErrors
    )
  of
    (Right mode, Right maxQuantity, Right maxAmount, Right highQuantityValue, Right highAmountValue, Right highOdometerValue, Right suspiciousQuantityValue, Right suspiciousAmountValue, []) ->
      Right
        FuelUploadRequestDto
          { dtoUploadMode = mode
          , dtoMaximumQuantity = maxQuantity
          , dtoMaximumAmount = maxAmount
          , dtoHighQuantityWarning = highQuantityValue
          , dtoHighAmountWarning = highAmountValue
          , dtoHighOdometerWarning = highOdometerValue
          , dtoSuspiciousQuantity = suspiciousQuantityValue
          , dtoSuspiciousAmount = suspiciousAmountValue
          , dtoRows = rows
          }
    _ ->
      Left
        ( errorsOfImport uploadMode
            <> errorsOfImport maximumQuantity
            <> errorsOfImport maximumAmount
            <> errorsOfImport highQuantity
            <> errorsOfImport highAmount
            <> errorsOfImport highOdometer
            <> errorsOfImport suspiciousQuantity
            <> errorsOfImport suspiciousAmount
            <> rowErrors
        )
  where
    uploadMode = parseImportMode "uploadMode" (importUploadMode request)
    maximumQuantity = parseImportRational "maximumQuantity" (importMaximumQuantity request)
    maximumAmount = parseImportRational "maximumAmount" (importMaximumAmount request)
    highQuantity = parseImportRational "highQuantityWarning" (importHighQuantityWarning request)
    highAmount = parseImportRational "highAmountWarning" (importHighAmountWarning request)
    highOdometer = parseImportInteger "highOdometerWarning" (importHighOdometerWarning request)
    suspiciousQuantity = parseImportRational "suspiciousQuantity" (importSuspiciousQuantity request)
    suspiciousAmount = parseImportRational "suspiciousAmount" (importSuspiciousAmount request)
    mappedRows = fmap (uncurry mapImportRow) (zip [0 :: Int ..] (importRows request))
    missingRows =
      [ importError ImportMissingRows "rows" "Rows are required."
      | null (importRows request)
      ]
    rowErrors = missingRows <> concatMap errorsOfImport mappedRows
    rows = [row | Right row <- mappedRows]

toResponseDto :: BatchDecision -> FuelUploadResponseDto
toResponseDto decision =
  FuelUploadResponseDto
    { responseDecisions = fmap toDecisionDto (batchRows decision)
    , responseAccepted = summaryAccepted summary
    , responseAcceptedWithWarnings = summaryAcceptedWithWarnings summary
    , responseQuarantined = summaryQuarantined summary
    , responseSkippedDuplicates = summarySkippedDuplicates summary
    , responseRejected = summaryRejected summary
    , responseFatal = summaryFatal summary
    , responseTotalRows = summaryTotalRows summary
    , responseBlocked =
        case batchOutcome decision of
          BatchUploadable -> False
          BatchBlockedByFatal _ -> True
    }
  where
    summary = batchSummary decision

mapImportRow :: Int -> RawFuelUploadRow -> Either [FuelImportError] FuelUploadRowDto
mapImportRow index row =
  case (rowNumber, transactionDate, externalRowId, registration, quantity, amount, odometer, merchant, lookupStatus, duplicateStatus) of
    (Right importedRowNumber, Right _, Right externalRowIdValue, Right registrationValue, Right quantityValue, Right amountValue, Right odometerValue, Right merchantValue, Right lookupStatusValue, Right duplicateStatusValue) ->
      Right
        FuelUploadRowDto
          { dtoRowNumber = importedRowNumber
          , dtoExternalRowId = externalRowIdValue
          , dtoRegistration = registrationValue
          , dtoQuantity = quantityValue
          , dtoAmount = amountValue
          , dtoOdometer = odometerValue
          , dtoMerchantName = merchantValue
          , dtoVehicleLookupStatus = lookupStatusValue
          , dtoVehicleId = importVehicleId row
          , dtoVehicleLookupError = importVehicleLookupError row
          , dtoDuplicateStatus = duplicateStatusValue
          , dtoPreviousTransactionId = importPreviousTransactionId row
          , dtoCanonicalizationState = importCanonicalizationState row
          , dtoFinalizationState = importFinalizationState row
          , dtoDuplicateError = importDuplicateError row
          }
    _ ->
      Left
        ( errorsOfImport rowNumber
            <> errorsOfImport transactionDate
            <> errorsOfImport externalRowId
            <> errorsOfImport registration
            <> errorsOfImport quantity
            <> errorsOfImport amount
            <> errorsOfImport odometer
            <> errorsOfImport merchant
            <> errorsOfImport lookupStatus
            <> errorsOfImport duplicateStatus
        )
  where
    prefix = "rows[" <> show index <> "]"
    rowNumber = parseImportInt (prefix <> ".rowNumber") (importRowNumber row)
    transactionDate = parseImportDate (prefix <> ".transactionDate") (importTransactionDate row)
    externalRowId = requireImportCell (prefix <> ".externalRowId") (importExternalRowId row)
    registration = requireImportCell (prefix <> ".registration") (importRegistration row)
    quantity = parseImportRational (prefix <> ".quantity") (importQuantity row)
    amount = parseImportRational (prefix <> ".amount") (importAmount row)
    odometer = parseImportInteger (prefix <> ".odometer") (importOdometer row)
    merchant = requireImportCell (prefix <> ".merchantName") (importMerchantName row)
    lookupStatus = requireImportCell (prefix <> ".vehicleLookupStatus") (importVehicleLookupStatus row)
    duplicateStatus = requireImportCell (prefix <> ".duplicateStatus") (importDuplicateStatus row)

mapRow :: Int -> FuelUploadRowDto -> Either [FuelUploadMappingError] RowContext
mapRow index dto =
  case (rowNumber, externalRowId, registration, vehicleLookup, duplicateCheck) of
    (Right number, Right externalId, Right registrationValue, Right vehicleLookupValue, Right duplicate) ->
      Right
        RowContext
          { contextRow =
              ParsedFuelRow
                { parsedRowNumber = number
                , parsedExternalRowId = externalId
                , parsedRegistration = registrationValue
                , parsedQuantity = FuelQuantity (dtoQuantity dto)
                , parsedAmount = MoneyAmount (dtoAmount dto)
                , parsedOdometer = OdometerReading (dtoOdometer dto)
                , parsedMerchantName = dtoMerchantName dto
                }
          , contextVehicleLookup = vehicleLookupValue
          , contextDuplicateCheck = duplicate
          }
    _ ->
      Left
        ( errorsOf rowNumber
            <> errorsOf externalRowId
            <> errorsOf registration
            <> errorsOf vehicleLookup
            <> errorsOf duplicateCheck
        )
  where
    prefix = "rows[" <> show index <> "]"
    rowNumber = parseRowNumber (prefix <> ".rowNumber") (dtoRowNumber dto)
    externalRowId = ExternalRowId <$> require (prefix <> ".externalRowId") (dtoExternalRowId dto)
    registration = Registration <$> require (prefix <> ".registration") (dtoRegistration dto)
    vehicleLookup = parseVehicleLookup prefix dto
    duplicateCheck = parseDuplicateCheck prefix dto

parseVehicleLookup :: String -> FuelUploadRowDto -> Either [FuelUploadMappingError] VehicleLookupResult
parseVehicleLookup prefix dto =
  case normalize (dtoVehicleLookupStatus dto) of
    "found" ->
      VehicleFound
        <$> ( Vehicle
                <$> (VehicleId <$> require (prefix <> ".vehicleId") (dtoVehicleId dto))
                <*> (Registration <$> require (prefix <> ".registration") (dtoRegistration dto))
            )
    "missing" ->
      VehicleMissing <$> (Registration <$> require (prefix <> ".registration") (dtoRegistration dto))
    "fatal" ->
      VehicleLookupFatal
        <$> ( VehicleLookupUnavailable
                <$> parseRowNumber (prefix <> ".rowNumber") (dtoRowNumber dto)
            )
    _ ->
      Left
        [ FuelUploadMappingError
            InvalidVehicleLookupStatus
            (prefix <> ".vehicleLookupStatus")
            ("Unsupported vehicle lookup status '" <> dtoVehicleLookupStatus dto <> "'.")
        ]

parseDuplicateCheck :: String -> FuelUploadRowDto -> Either [FuelUploadMappingError] DuplicateCheckResult
parseDuplicateCheck prefix dto =
  case normalize (dtoDuplicateStatus dto) of
    "unique" -> Right (DuplicateCheckSucceeded UniqueRow)
    "duplicate" ->
      DuplicateCheckSucceeded . DuplicateOf
        <$> ( PreviousAttempt
                <$> (TransactionId <$> require (prefix <> ".previousTransactionId") (dtoPreviousTransactionId dto))
                <*> parseCanonicalizationState prefix dto
                <*> parseFinalizationState prefix dto
            )
    "fatal" ->
      DuplicateCheckFatal
        <$> ( DuplicateCheckUnavailable
                <$> parseRowNumber (prefix <> ".rowNumber") (dtoRowNumber dto)
            )
    _ ->
      Left
        [ FuelUploadMappingError
            InvalidDuplicateStatus
            (prefix <> ".duplicateStatus")
            ("Unsupported duplicate status '" <> dtoDuplicateStatus dto <> "'.")
        ]

parseCanonicalizationState :: String -> FuelUploadRowDto -> Either [FuelUploadMappingError] CanonicalizationState
parseCanonicalizationState prefix dto =
  case normalize (dtoCanonicalizationState dto) of
    "withtransactionkey" ->
      CanonicalizedWithTransactionKey
        <$> (TransactionId <$> require (prefix <> ".previousTransactionId") (dtoPreviousTransactionId dto))
    "withouttransactionkey" -> Right CanonicalizedWithoutTransactionKey
    "failedbeforecanonicalization" -> Right FailedBeforeCanonicalization
    _ ->
      Left
        [ FuelUploadMappingError
            InvalidCanonicalizationState
            (prefix <> ".canonicalizationState")
            ("Unsupported canonicalization state '" <> dtoCanonicalizationState dto <> "'.")
        ]

parseFinalizationState :: String -> FuelUploadRowDto -> Either [FuelUploadMappingError] FinalizationState
parseFinalizationState prefix dto =
  case normalize (dtoFinalizationState dto) of
    "finalized" -> Right Finalized
    "failedretryable" -> Right FailedRetryable
    "failednotretryable" -> Right FailedNotRetryable
    _ ->
      Left
        [ FuelUploadMappingError
            InvalidFinalizationState
            (prefix <> ".finalizationState")
            ("Unsupported finalization state '" <> dtoFinalizationState dto <> "'.")
        ]

parseMode :: String -> String -> Either [FuelUploadMappingError] UploadMode
parseMode field value =
  case normalize value of
    "normal" -> Right Normal
    "retry" -> Right Retry
    "conservativerecovery" -> Right ConservativeRecovery
    "aggressiverecovery" -> Right AggressiveRecovery
    _ ->
      Left
        [ FuelUploadMappingError
            InvalidUploadMode
            field
            ("Unsupported upload mode '" <> value <> "'.")
        ]

parseImportMode :: String -> String -> Either [FuelImportError] String
parseImportMode field value = do
  required <- requireImportCell field value
  case normalize required of
    "normal" -> Right required
    "retry" -> Right required
    "conservativerecovery" -> Right required
    "aggressiverecovery" -> Right required
    _ ->
      Left
        [ importError
            ImportInvalidUploadMode
            field
            ("Unsupported upload mode '" <> required <> "'.")
        ]

parseImportDate :: String -> String -> Either [FuelImportError] String
parseImportDate field value = do
  required <- requireImportCell field value
  if isIsoDate required
    then Right required
    else
      Left
        [ importError
            ImportInvalidDate
            field
            "Date must use yyyy-MM-dd format."
        ]

parseImportRational :: String -> String -> Either [FuelImportError] Rational
parseImportRational field value = do
  required <- requireImportCell field value
  case readMaybe required :: Maybe Double of
    Just parsed
      | not (isNaN parsed) && not (isInfinite parsed) -> Right (toRational parsed)
    _ ->
      Left
        [ importError
            ImportInvalidNumber
            field
            "Cell must be a decimal number."
        ]

parseImportInt :: String -> String -> Either [FuelImportError] Int
parseImportInt field value = do
  required <- requireImportCell field value
  case readMaybe required of
    Just parsed -> Right parsed
    Nothing ->
      Left
        [ importError
            ImportInvalidNumber
            field
            "Cell must be an integer."
        ]

parseImportInteger :: String -> String -> Either [FuelImportError] Integer
parseImportInteger field value = do
  required <- requireImportCell field value
  case readMaybe required of
    Just parsed -> Right parsed
    Nothing ->
      Left
        [ importError
            ImportInvalidNumber
            field
            "Cell must be an integer."
        ]

parseRowNumber :: String -> Int -> Either [FuelUploadMappingError] RowNumber
parseRowNumber field value
  | value > 0 = Right (RowNumber value)
  | otherwise =
      Left
        [ FuelUploadMappingError
            InvalidRowNumber
            field
            "Row number must be positive."
        ]

require :: String -> String -> Either [FuelUploadMappingError] String
require field value
  | null (trim value) =
      Left
        [ FuelUploadMappingError
            MissingRequiredField
            field
            "A non-empty value is required."
        ]
  | otherwise = Right (trim value)

requireImportCell :: String -> String -> Either [FuelImportError] String
requireImportCell field value
  | null (trim value) =
      Left
        [ importError
            ImportMissingRequiredCell
            field
            "A non-empty cell is required."
        ]
  | otherwise = Right (trim value)

importError :: FuelImportErrorCode -> String -> String -> FuelImportError
importError code field detail =
  FuelImportError
    { importErrorCode = code
    , importErrorField = field
    , importErrorDetail = detail
    }

importErrorFromMappingError :: FuelUploadMappingError -> FuelImportError
importErrorFromMappingError mappingErrorValue =
  importError code (mappingErrorField mappingErrorValue) (mappingErrorDetail mappingErrorValue)
  where
    code =
      case mappingErrorCode mappingErrorValue of
        InvalidUploadMode -> ImportInvalidUploadMode
        MissingRequiredField -> ImportMissingRequiredCell
        _ -> ImportMissingRequiredCell

toDecisionDto :: RowDecision -> FuelUploadDecisionDto
toDecisionDto decision =
  case decision of
    Accepted transaction ->
      transactionDto "accepted" transaction [] []
    AcceptedWithWarnings transaction warnings ->
      transactionDto "accepted_with_warnings" transaction (show <$> nonEmptyToList warnings) []
    Quarantined transaction reasons ->
      transactionDto "quarantined" transaction [] (show <$> nonEmptyToList reasons)
    SkippedDuplicate skipped ->
      FuelUploadDecisionDto
        { decisionRowNumber = rowNumberValue (parsedRowNumber (skippedRow skipped))
        , decisionOutcome = "skipped_duplicate"
        , decisionTransactionId = Nothing
        , decisionVehicleId = Nothing
        , decisionWarnings = []
        , decisionQuarantineReasons = []
        , decisionRejectionReason = Nothing
        , decisionDuplicateSkipReason = Just (show (duplicateSkipReason skipped))
        , decisionFatalError = Nothing
        }
    Rejected rejected ->
      FuelUploadDecisionDto
        { decisionRowNumber = rowNumberValue (parsedRowNumber (rejectedRow rejected))
        , decisionOutcome = "rejected"
        , decisionTransactionId = Nothing
        , decisionVehicleId = Nothing
        , decisionWarnings = []
        , decisionQuarantineReasons = []
        , decisionRejectionReason = Just (show (rejectionReason rejected))
        , decisionDuplicateSkipReason = Nothing
        , decisionFatalError = Nothing
        }
    Fatal fatalError ->
      FuelUploadDecisionDto
        { decisionRowNumber = fatalRowNumber fatalError
        , decisionOutcome = "fatal"
        , decisionTransactionId = Nothing
        , decisionVehicleId = Nothing
        , decisionWarnings = []
        , decisionQuarantineReasons = []
        , decisionRejectionReason = Nothing
        , decisionDuplicateSkipReason = Nothing
        , decisionFatalError = Just (show fatalError)
        }

transactionDto :: String -> FuelTransaction -> [String] -> [String] -> FuelUploadDecisionDto
transactionDto outcome transaction warnings quarantines =
  FuelUploadDecisionDto
    { decisionRowNumber = rowNumberValue (transactionRowNumber transaction)
    , decisionOutcome = outcome
    , decisionTransactionId = Just (transactionIdValue (transactionId transaction))
    , decisionVehicleId = Just (vehicleIdValue (transactionVehicleId transaction))
    , decisionWarnings = warnings
    , decisionQuarantineReasons = quarantines
    , decisionRejectionReason = Nothing
    , decisionDuplicateSkipReason = Nothing
    , decisionFatalError = Nothing
    }

errorsOf :: Either [FuelUploadMappingError] value -> [FuelUploadMappingError]
errorsOf result =
  case result of
    Left errors -> errors
    Right _ -> []

errorsOfImport :: Either [FuelImportError] value -> [FuelImportError]
errorsOfImport result =
  case result of
    Left errors -> errors
    Right _ -> []

normalize :: String -> String
normalize =
  fmap toLower . filter (/= '_') . trim

trim :: String -> String
trim =
  reverse . dropWhile (== ' ') . reverse . dropWhile (== ' ')

isIsoDate :: String -> Bool
isIsoDate value =
  case value of
    [y1, y2, y3, y4, '-', m1, m2, '-', d1, d2] ->
      all isDigit [y1, y2, y3, y4, m1, m2, d1, d2]
        && inRange 1 12 [m1, m2]
        && inRange 1 31 [d1, d2]
    _ -> False
  where
    inRange low high digits =
      let parsed = read digits :: Int
       in parsed >= low && parsed <= high

nonEmptyToList :: Foldable f => f a -> [a]
nonEmptyToList = foldr (:) []

rowNumberValue :: RowNumber -> Int
rowNumberValue (RowNumber value) = value

transactionIdValue :: TransactionId -> String
transactionIdValue (TransactionId value) = value

vehicleIdValue :: VehicleId -> String
vehicleIdValue (VehicleId value) = value

fatalRowNumber :: FatalError -> Int
fatalRowNumber fatalError =
  case fatalError of
    VehicleLookupUnavailable rowNumber -> rowNumberValue rowNumber
    DuplicateCheckUnavailable rowNumber -> rowNumberValue rowNumber
    CorruptParsedRow rowNumber -> rowNumberValue rowNumber
