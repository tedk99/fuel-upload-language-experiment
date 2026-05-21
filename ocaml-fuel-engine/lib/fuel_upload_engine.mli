type row_number = RowNumber of int
type external_row_id = ExternalRowId of string
type vehicle_id = VehicleId of string
type registration = Registration of string
type fuel_quantity = FuelQuantity of float
type money_amount = MoneyAmount of float
type odometer_reading = OdometerReading of int
type transaction_id = TransactionId of string

type parsed_fuel_row =
  { row_number : row_number
  ; external_row_id : external_row_id
  ; registration : registration
  ; quantity : fuel_quantity
  ; amount : money_amount
  ; odometer : odometer_reading
  }

type upload_mode =
  | Normal
  | Retry
  | Recovery

type validation_config =
  { maximum_quantity : fuel_quantity
  ; maximum_amount : money_amount
  ; high_quantity_warning : fuel_quantity
  ; high_amount_warning : money_amount
  ; high_odometer_warning : odometer_reading
  }

type vehicle =
  { vehicle_id : vehicle_id
  ; vehicle_registration : registration
  }

type fatal_error =
  | Vehicle_lookup_unavailable of row_number
  | Duplicate_check_unavailable of row_number
  | Corrupt_parsed_row of row_number

type vehicle_lookup_result =
  | Vehicle_found of vehicle
  | Vehicle_missing of registration
  | Vehicle_lookup_fatal of fatal_error

type canonicalization_state =
  | Canonicalized
  | Failed_before_canonicalization

type finalization_state =
  | Finalized
  | Failed_retryable
  | Failed_not_retryable

type previous_attempt =
  { previous_transaction_id : transaction_id
  ; canonicalization_state : canonicalization_state
  ; finalization_state : finalization_state
  }

type duplicate_state =
  | Unique_row
  | Duplicate_of of previous_attempt

type duplicate_check_result =
  | Duplicate_check_succeeded of duplicate_state
  | Duplicate_check_fatal of fatal_error

type fuel_transaction =
  { transaction_id : transaction_id
  ; transaction_row_number : row_number
  ; transaction_vehicle_id : vehicle_id
  ; transaction_external_row_id : external_row_id
  ; transaction_quantity : fuel_quantity
  ; transaction_amount : money_amount
  ; transaction_odometer : odometer_reading
  }

type validation_error =
  | Quantity_must_be_finite of fuel_quantity
  | Quantity_must_be_positive of fuel_quantity
  | Amount_must_be_finite of money_amount
  | Amount_must_be_positive of money_amount
  | Odometer_must_not_be_negative of odometer_reading
  | Quantity_exceeds_maximum of fuel_quantity * fuel_quantity
  | Amount_exceeds_maximum of money_amount * money_amount

type warning =
  | Quantity_above_warning_threshold of fuel_quantity * fuel_quantity
  | Amount_above_warning_threshold of money_amount * money_amount
  | Odometer_above_warning_threshold of odometer_reading * odometer_reading

type duplicate_skip_reason =
  | Already_finalized of transaction_id
  | Previous_attempt_not_retryable of transaction_id
  | Previous_attempt_canonicalized of transaction_id

type rejection_reason =
  | Vehicle_was_not_found of registration
  | Row_failed_validation of validation_error list
  | Duplicate_cannot_be_uploaded of upload_mode * duplicate_skip_reason

type rejected_row =
  { rejected_row : parsed_fuel_row
  ; rejection_reason : rejection_reason
  }

type skipped_duplicate =
  { skipped_row : parsed_fuel_row
  ; duplicate_skip_reason : duplicate_skip_reason
  }

type row_decision =
  | Accepted of fuel_transaction
  | Accepted_with_warnings of fuel_transaction * warning list
  | Skipped_duplicate of skipped_duplicate
  | Rejected of rejected_row
  | Fatal of fatal_error

type row_context =
  { row : parsed_fuel_row
  ; vehicle_lookup : vehicle_lookup_result
  ; duplicate_check : duplicate_check_result
  }

type batch_summary =
  { total_rows : int
  ; accepted_rows : int
  ; accepted_with_warning_rows : int
  ; warning_count : int
  ; skipped_duplicate_rows : int
  ; rejected_rows : int
  ; fatal_error_rows : int
  }

type batch_outcome =
  | Uploadable
  | Blocked_by_fatal of fatal_error list

type batch_decision =
  { rows : row_decision list
  ; summary : batch_summary
  ; outcome : batch_outcome
  }

val classify_row : validation_config -> upload_mode -> row_context -> row_decision
val classify_batch : validation_config -> upload_mode -> row_context list -> batch_decision
val summarize_rows : row_decision list -> batch_summary
