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

type duplicate_policy_decision =
  | Upload_duplicate
  | Skip_duplicate of duplicate_skip_reason
  | Reject_duplicate of duplicate_skip_reason

let quantity_value (FuelQuantity value) = value
let amount_value (MoneyAmount value) = value
let odometer_value (OdometerReading value) = value
let external_row_id_value (ExternalRowId value) = value
let vehicle_id_value (VehicleId value) = value

let validation_errors config row =
  let quantity = quantity_value row.quantity in
  let amount = amount_value row.amount in
  let odometer = odometer_value row.odometer in
  let maximum_quantity = quantity_value config.maximum_quantity in
  let maximum_amount = amount_value config.maximum_amount in
  [ (if not (Float.is_finite quantity) then Some (Quantity_must_be_finite row.quantity) else None)
  ; (if Float.is_finite quantity && quantity <= 0.0 then
       Some (Quantity_must_be_positive row.quantity)
     else
       None)
  ; (if Float.is_finite quantity && quantity > maximum_quantity then
       Some (Quantity_exceeds_maximum (row.quantity, config.maximum_quantity))
     else
       None)
  ; (if not (Float.is_finite amount) then Some (Amount_must_be_finite row.amount) else None)
  ; (if Float.is_finite amount && amount <= 0.0 then
       Some (Amount_must_be_positive row.amount)
     else
       None)
  ; (if Float.is_finite amount && amount > maximum_amount then
       Some (Amount_exceeds_maximum (row.amount, config.maximum_amount))
     else
       None)
  ; (if odometer < 0 then Some (Odometer_must_not_be_negative row.odometer) else None)
  ]
  |> List.filter_map Fun.id

let validation_warnings config row =
  let quantity = quantity_value row.quantity in
  let amount = amount_value row.amount in
  let odometer = odometer_value row.odometer in
  [ (if quantity > quantity_value config.high_quantity_warning then
       Some (Quantity_above_warning_threshold (row.quantity, config.high_quantity_warning))
     else
       None)
  ; (if amount > amount_value config.high_amount_warning then
       Some (Amount_above_warning_threshold (row.amount, config.high_amount_warning))
     else
       None)
  ; (if odometer > odometer_value config.high_odometer_warning then
       Some (Odometer_above_warning_threshold (row.odometer, config.high_odometer_warning))
     else
       None)
  ]
  |> List.filter_map Fun.id

let skip_reason_for_previous_attempt previous =
  match previous.finalization_state with
  | Finalized -> Already_finalized previous.previous_transaction_id
  | Failed_retryable -> Previous_attempt_canonicalized previous.previous_transaction_id
  | Failed_not_retryable -> Previous_attempt_not_retryable previous.previous_transaction_id

let duplicate_decision mode = function
  | Unique_row -> Upload_duplicate
  | Duplicate_of previous ->
    (match mode with
     | Normal -> Skip_duplicate (skip_reason_for_previous_attempt previous)
     | Retry ->
       (match previous.finalization_state with
        | Failed_retryable -> Upload_duplicate
        | Finalized | Failed_not_retryable ->
          Skip_duplicate (skip_reason_for_previous_attempt previous))
     | Recovery ->
       (match previous.canonicalization_state with
        | Failed_before_canonicalization -> Upload_duplicate
        | Canonicalized -> Reject_duplicate (skip_reason_for_previous_attempt previous)))

let derive_transaction_id row vehicle_id =
  TransactionId (external_row_id_value row.external_row_id ^ ":" ^ vehicle_id_value vehicle_id)

let to_transaction row vehicle =
  { transaction_id = derive_transaction_id row vehicle.vehicle_id
  ; transaction_row_number = row.row_number
  ; transaction_vehicle_id = vehicle.vehicle_id
  ; transaction_external_row_id = row.external_row_id
  ; transaction_quantity = row.quantity
  ; transaction_amount = row.amount
  ; transaction_odometer = row.odometer
  }

let rejected row rejection_reason = Rejected { rejected_row = row; rejection_reason }

let classify_validated config row vehicle =
  match validation_errors config row with
  | [] ->
    let transaction = to_transaction row vehicle in
    (match validation_warnings config row with
     | [] -> Accepted transaction
     | warnings -> Accepted_with_warnings (transaction, warnings))
  | errors -> rejected row (Row_failed_validation errors)

let classify_row config mode context =
  match context.vehicle_lookup, context.duplicate_check with
  | Vehicle_lookup_fatal fatal, _ -> Fatal fatal
  | _, Duplicate_check_fatal fatal -> Fatal fatal
  | Vehicle_missing registration, _ -> rejected context.row (Vehicle_was_not_found registration)
  | Vehicle_found vehicle, Duplicate_check_succeeded duplicate_state ->
    (match duplicate_decision mode duplicate_state with
     | Upload_duplicate -> classify_validated config context.row vehicle
     | Skip_duplicate duplicate_skip_reason ->
       Skipped_duplicate { skipped_row = context.row; duplicate_skip_reason }
     | Reject_duplicate duplicate_skip_reason ->
       rejected
         context.row
         (Duplicate_cannot_be_uploaded (mode, duplicate_skip_reason)))

let summarize_rows rows =
  let empty =
    { total_rows = 0
    ; accepted_rows = 0
    ; accepted_with_warning_rows = 0
    ; warning_count = 0
    ; skipped_duplicate_rows = 0
    ; rejected_rows = 0
    ; fatal_error_rows = 0
    }
  in
  let add summary = function
    | Accepted _ -> { summary with total_rows = summary.total_rows + 1; accepted_rows = summary.accepted_rows + 1 }
    | Accepted_with_warnings (_, warnings) ->
      { summary with
        total_rows = summary.total_rows + 1
      ; accepted_rows = summary.accepted_rows + 1
      ; accepted_with_warning_rows = summary.accepted_with_warning_rows + 1
      ; warning_count = summary.warning_count + List.length warnings
      }
    | Skipped_duplicate _ ->
      { summary with
        total_rows = summary.total_rows + 1
      ; skipped_duplicate_rows = summary.skipped_duplicate_rows + 1
      }
    | Rejected _ ->
      { summary with total_rows = summary.total_rows + 1; rejected_rows = summary.rejected_rows + 1 }
    | Fatal _ ->
      { summary with total_rows = summary.total_rows + 1; fatal_error_rows = summary.fatal_error_rows + 1 }
  in
  List.fold_left add empty rows

let fatal_errors rows =
  rows
  |> List.filter_map (function
    | Fatal fatal -> Some fatal
    | Accepted _ | Accepted_with_warnings _ | Skipped_duplicate _ | Rejected _ -> None)

let classify_batch config mode contexts =
  let rows = List.map (classify_row config mode) contexts in
  let summary = summarize_rows rows in
  let outcome =
    match fatal_errors rows with
    | [] -> Uploadable
    | fatals -> Blocked_by_fatal fatals
  in
  { rows; summary; outcome }
