module FuelUpload.DecisionEngine
  ( classifyRow
  , classifyBatch
  ) where

import Data.List.NonEmpty (NonEmpty (..))
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Duplicate
import FuelUpload.Domain.Primitive
import FuelUpload.Domain.Row
import FuelUpload.Domain.Vehicle
import FuelUpload.DuplicatePolicy
import FuelUpload.Summary
import FuelUpload.Validation

classifyRow :: ValidationConfig -> UploadMode -> RowContext -> RowDecision
classifyRow config mode context =
  case (contextVehicleLookup context, contextDuplicateCheck context) of
    (VehicleLookupFatal fatalError, _) ->
      Fatal fatalError
    (_, DuplicateCheckFatal fatalError) ->
      Fatal fatalError
    _ ->
      classifyValidated
  where
    row = contextRow context

    reject reason =
      Rejected RejectedRow
        { rejectedRow = row
        , rejectionReason = reason
        }

    classifyValidated =
      case validationErrors config row of
        firstError : remainingErrors ->
          reject (RowFailedValidation (firstError :| remainingErrors))
        [] ->
          case contextVehicleLookup context of
            VehicleMissing registration ->
              reject (VehicleWasNotFound registration)
            VehicleFound vehicle ->
              case contextDuplicateCheck context of
                DuplicateCheckSucceeded duplicateState ->
                  case duplicateDecision mode duplicateState of
                    UploadDuplicate ->
                      acceptedOrQuarantined vehicle
                    SkipDuplicate reason ->
                      SkippedDuplicate SkippedDuplicateInfo
                        { skippedRow = row
                        , duplicateSkipReason = reason
                        }
                    RejectDuplicate reason ->
                      reject (DuplicateCannotBeUploaded mode reason)
                DuplicateCheckFatal fatalError ->
                  Fatal fatalError
            VehicleLookupFatal fatalError ->
              Fatal fatalError

    acceptedOrQuarantined vehicle =
      let transaction = toTransaction row vehicle
       in case quarantineReasons config row of
            firstReason : remainingReasons ->
              Quarantined transaction (firstReason :| remainingReasons)
            [] ->
              case validationWarnings config row of
                firstWarning : remainingWarnings ->
                  AcceptedWithWarnings transaction (firstWarning :| remainingWarnings)
                [] ->
                  Accepted transaction

classifyBatch :: ValidationConfig -> UploadMode -> [RowContext] -> BatchDecision
classifyBatch config mode contexts =
  let decisions = fmap (classifyRow config mode) contexts
      summary = summarizeRows decisions
   in BatchDecision
        { batchRows = decisions
        , batchSummary = summary
        , batchOutcome = outcomeFromRows decisions
        }

toTransaction :: ParsedFuelRow -> Vehicle -> FuelTransaction
toTransaction row vehicle =
  FuelTransaction
    { transactionId = deriveTransactionId row (vehicleId vehicle)
    , transactionRowNumber = parsedRowNumber row
    , transactionVehicleId = vehicleId vehicle
    , transactionExternalRowId = parsedExternalRowId row
    , transactionQuantity = parsedQuantity row
    , transactionAmount = parsedAmount row
    , transactionOdometer = parsedOdometer row
    }

deriveTransactionId :: ParsedFuelRow -> VehicleId -> TransactionId
deriveTransactionId row (VehicleId vehicleValue) =
  TransactionId
    ( externalRowValue (parsedExternalRowId row)
        <> ":"
        <> vehicleValue
    )
  where
    externalRowValue (ExternalRowId value) = value
