open OUnit2
open Fuel_upload_engine

let config =
  { maximum_quantity = FuelQuantity 100.0
  ; maximum_amount = MoneyAmount 500.0
  ; high_quantity_warning = FuelQuantity 80.0
  ; high_amount_warning = MoneyAmount 400.0
  ; high_odometer_warning = OdometerReading 200_000
  }

let quiet_config =
  { config with
    high_quantity_warning = FuelQuantity 1_000.0
  ; high_amount_warning = MoneyAmount 10_000.0
  ; high_odometer_warning = OdometerReading 1_000_000
  }

let row ?(row_number = RowNumber 1) ?(external_row_id = ExternalRowId "row-1")
  ?(registration = Registration "AB12CDE") ?(quantity = FuelQuantity 42.0)
  ?(amount = MoneyAmount 126.0) ?(odometer = OdometerReading 12_345) () =
  { row_number; external_row_id; registration; quantity; amount; odometer }

let vehicle =
  { vehicle_id = VehicleId "vehicle-1"; vehicle_registration = Registration "AB12CDE" }

let context ?(row = row ()) ?(vehicle_lookup = Vehicle_found vehicle)
  ?(duplicate_check = Duplicate_check_succeeded Unique_row) () =
  { row; vehicle_lookup; duplicate_check }

let previous ?(transaction_id = TransactionId "tx-previous")
  ?(canonicalization_state = Canonicalized) ?(finalization_state = Finalized) () =
  { previous_transaction_id = transaction_id; canonicalization_state; finalization_state }

let assert_accepted = function
  | Accepted _ | Accepted_with_warnings _ -> ()
  | Skipped_duplicate _ -> assert_failure "expected accepted, got skipped duplicate"
  | Rejected _ -> assert_failure "expected accepted, got rejected"
  | Fatal _ -> assert_failure "expected accepted, got fatal"

let assert_skipped = function
  | Skipped_duplicate _ -> ()
  | Accepted _ | Accepted_with_warnings _ -> assert_failure "expected skipped duplicate, got accepted"
  | Rejected _ -> assert_failure "expected skipped duplicate, got rejected"
  | Fatal _ -> assert_failure "expected skipped duplicate, got fatal"

let test_accepts_unique_valid_row _ =
  match classify_row quiet_config Normal (context ()) with
  | Accepted transaction ->
    assert_equal (TransactionId "row-1:vehicle-1") transaction.transaction_id;
    assert_equal (VehicleId "vehicle-1") transaction.transaction_vehicle_id;
    assert_equal (FuelQuantity 42.0) transaction.transaction_quantity
  | Accepted_with_warnings _ -> assert_failure "quiet config should not warn"
  | Skipped_duplicate _ -> assert_failure "unique row was skipped"
  | Rejected _ -> assert_failure "valid row was rejected"
  | Fatal _ -> assert_failure "valid row was fatal"

let test_validation_errors_reject_row _ =
  let invalid_row =
    row
      ~quantity:(FuelQuantity 0.0)
      ~amount:(MoneyAmount 750.0)
      ~odometer:(OdometerReading (-1))
      ()
  in
  match classify_row config Normal (context ~row:invalid_row ()) with
  | Rejected { rejection_reason = Row_failed_validation errors; _ } ->
    assert_bool "expected quantity error" (List.mem (Quantity_must_be_positive (FuelQuantity 0.0)) errors);
    assert_bool
      "expected amount maximum error"
      (List.mem (Amount_exceeds_maximum (MoneyAmount 750.0, MoneyAmount 500.0)) errors);
    assert_bool
      "expected odometer error"
      (List.mem (Odometer_must_not_be_negative (OdometerReading (-1))) errors)
  | Rejected _ -> assert_failure "expected validation rejection"
  | Accepted _ | Accepted_with_warnings _ -> assert_failure "invalid row was accepted"
  | Skipped_duplicate _ -> assert_failure "unique invalid row was skipped"
  | Fatal _ -> assert_failure "invalid row should not be fatal"

let test_nan_quantity_rejects_row _ =
  let invalid_row = row ~quantity:(FuelQuantity Float.nan) () in
  match classify_row config Normal (context ~row:invalid_row ()) with
  | Rejected { rejection_reason = Row_failed_validation [ Quantity_must_be_finite (FuelQuantity q) ]; _ } ->
    assert_bool "stored NaN in typed validation error" (Float.is_nan q)
  | Rejected { rejection_reason = Row_failed_validation errors; _ } ->
    assert_failure ("expected one finite quantity validation error, got " ^ string_of_int (List.length errors))
  | Rejected _ -> assert_failure "expected validation rejection"
  | Accepted _ | Accepted_with_warnings _ -> assert_failure "NaN row was accepted"
  | Skipped_duplicate _ -> assert_failure "unique NaN row was skipped"
  | Fatal _ -> assert_failure "NaN row should not be fatal"

let test_vehicle_missing_rejects_row _ =
  match
    classify_row
      config
      Normal
      (context ~vehicle_lookup:(Vehicle_missing (Registration "MISSING")) ())
  with
  | Rejected { rejection_reason = Vehicle_was_not_found (Registration "MISSING"); _ } -> ()
  | Rejected _ -> assert_failure "expected unknown vehicle rejection"
  | Accepted _ | Accepted_with_warnings _ -> assert_failure "missing vehicle was accepted"
  | Skipped_duplicate _ -> assert_failure "missing vehicle was skipped"
  | Fatal _ -> assert_failure "missing vehicle should not be fatal"

let test_duplicate_behavior_by_mode _ =
  let retryable =
    Duplicate_check_succeeded
      (Duplicate_of (previous ~finalization_state:Failed_retryable ()))
  in
  let finalized =
    Duplicate_check_succeeded (Duplicate_of (previous ~finalization_state:Finalized ()))
  in
  let not_retryable =
    Duplicate_check_succeeded
      (Duplicate_of (previous ~finalization_state:Failed_not_retryable ()))
  in
  let pre_canonical =
    Duplicate_check_succeeded
      (Duplicate_of
         (previous
            ~canonicalization_state:Failed_before_canonicalization
            ~finalization_state:Failed_retryable
            ()))
  in
  let canonicalized_retryable =
    Duplicate_check_succeeded
      (Duplicate_of
         (previous
            ~canonicalization_state:Canonicalized
            ~finalization_state:Failed_retryable
            ()))
  in
  assert_skipped (classify_row config Normal (context ~duplicate_check:retryable ()));
  assert_accepted (classify_row config Retry (context ~duplicate_check:retryable ()));
  assert_skipped (classify_row config Retry (context ~duplicate_check:finalized ()));
  assert_skipped (classify_row config Retry (context ~duplicate_check:not_retryable ()));
  assert_accepted (classify_row config Recovery (context ~duplicate_check:pre_canonical ()));
  match classify_row config Recovery (context ~duplicate_check:canonicalized_retryable ()) with
  | Rejected { rejection_reason = Duplicate_cannot_be_uploaded (Recovery, Previous_attempt_canonicalized _); _ } -> ()
  | Rejected _ -> assert_failure "expected typed recovery duplicate rejection"
  | Accepted _ | Accepted_with_warnings _ -> assert_failure "canonicalized recovery duplicate was accepted"
  | Skipped_duplicate _ -> assert_failure "unsafe recovery duplicate should be rejected"
  | Fatal _ -> assert_failure "unsafe recovery duplicate should not be fatal"

let test_fatal_blocks_batch _ =
  let fatal = Vehicle_lookup_unavailable (RowNumber 2) in
  let fatal_context =
    context
      ~row:(row ~row_number:(RowNumber 2) ~external_row_id:(ExternalRowId "row-2") ())
      ~vehicle_lookup:(Vehicle_lookup_fatal fatal)
      ()
  in
  let decision = classify_batch config Normal [ context (); fatal_context ] in
  assert_equal 2 decision.summary.total_rows;
  assert_equal 1 decision.summary.accepted_rows;
  assert_equal 1 decision.summary.fatal_error_rows;
  match decision.outcome with
  | Blocked_by_fatal [ actual ] -> assert_equal fatal actual
  | Blocked_by_fatal _ -> assert_failure "expected exactly one fatal error"
  | Uploadable -> assert_failure "fatal batch was uploadable"

let test_warnings_do_not_block_transaction _ =
  let warning_row =
    row
      ~quantity:(FuelQuantity 90.0)
      ~amount:(MoneyAmount 450.0)
      ~odometer:(OdometerReading 250_000)
      ()
  in
  let batch = classify_batch config Normal [ context ~row:warning_row () ] in
  (match batch.rows with
   | [ Accepted_with_warnings (transaction, warnings) ] ->
     assert_equal (TransactionId "row-1:vehicle-1") transaction.transaction_id;
     assert_equal 3 (List.length warnings)
   | [ Accepted _ ] -> assert_failure "expected warning decision"
   | _ -> assert_failure "expected one accepted row with warnings");
  assert_equal 1 batch.summary.accepted_rows;
  assert_equal 1 batch.summary.accepted_with_warning_rows;
  assert_equal 3 batch.summary.warning_count;
  match batch.outcome with
  | Uploadable -> ()
  | Blocked_by_fatal _ -> assert_failure "warnings blocked upload"

let unit_tests =
  [ "accepts unique valid row" >:: test_accepts_unique_valid_row
  ; "validation errors reject row" >:: test_validation_errors_reject_row
  ; "nan quantity rejects row" >:: test_nan_quantity_rejects_row
  ; "vehicle missing rejects row" >:: test_vehicle_missing_rejects_row
  ; "duplicate behavior by mode" >:: test_duplicate_behavior_by_mode
  ; "fatal blocks batch" >:: test_fatal_blocks_batch
  ; "warnings do not block transaction" >:: test_warnings_do_not_block_transaction
  ]

let sample_transaction =
  { transaction_id = TransactionId "tx"
  ; transaction_row_number = RowNumber 1
  ; transaction_vehicle_id = VehicleId "vehicle"
  ; transaction_external_row_id = ExternalRowId "row"
  ; transaction_quantity = FuelQuantity 1.0
  ; transaction_amount = MoneyAmount 2.0
  ; transaction_odometer = OdometerReading 3
  }

let sample_warning = Quantity_above_warning_threshold (FuelQuantity 2.0, FuelQuantity 1.0)
let sample_rejected = { rejected_row = row (); rejection_reason = Vehicle_was_not_found (Registration "X") }
let sample_skipped = { skipped_row = row (); duplicate_skip_reason = Already_finalized (TransactionId "tx") }
let sample_fatal = Duplicate_check_unavailable (RowNumber 1)

let row_decision_gen =
  let open QCheck2.Gen in
  oneof
    [ return (Accepted sample_transaction)
    ; map
        (fun count -> Accepted_with_warnings (sample_transaction, List.init count (fun _ -> sample_warning)))
        (int_range 1 5)
    ; return (Skipped_duplicate sample_skipped)
    ; return (Rejected sample_rejected)
    ; return (Fatal sample_fatal)
    ]

let count_by predicate rows = rows |> List.filter predicate |> List.length

let qcheck_tests =
  let open QCheck2 in
  [ Test.make
      ~name:"summary counts are derived from row decisions"
      ~count:500
      Gen.(list_size (int_range 0 50) row_decision_gen)
      (fun rows ->
        let summary = summarize_rows rows in
        let warning_count =
          rows
          |> List.fold_left
               (fun total -> function
                 | Accepted_with_warnings (_, warnings) -> total + List.length warnings
                 | Accepted _ | Skipped_duplicate _ | Rejected _ | Fatal _ -> total)
               0
        in
        summary.total_rows = List.length rows
        && summary.accepted_rows
           = count_by
               (function
                 | Accepted _ | Accepted_with_warnings _ -> true
                 | Skipped_duplicate _ | Rejected _ | Fatal _ -> false)
               rows
        && summary.accepted_with_warning_rows
           = count_by
               (function
                 | Accepted_with_warnings _ -> true
                 | Accepted _ | Skipped_duplicate _ | Rejected _ | Fatal _ -> false)
               rows
        && summary.warning_count = warning_count
        && summary.skipped_duplicate_rows
           = count_by
               (function
                 | Skipped_duplicate _ -> true
                 | Accepted _ | Accepted_with_warnings _ | Rejected _ | Fatal _ -> false)
               rows
        && summary.rejected_rows
           = count_by
               (function
                 | Rejected _ -> true
                 | Accepted _ | Accepted_with_warnings _ | Skipped_duplicate _ | Fatal _ -> false)
               rows
        && summary.fatal_error_rows
           = count_by
               (function
                 | Fatal _ -> true
                 | Accepted _ | Accepted_with_warnings _ | Skipped_duplicate _ | Rejected _ -> false)
               rows)
  ; Test.make
      ~name:"unique rows with validation errors are never accepted"
      ~count:300
      Gen.(
        oneof
          [ map (fun value -> row ~quantity:(FuelQuantity value) ()) (oneof_list [ -10.0; -1.0; 0.0; Float.nan ])
          ; map (fun value -> row ~amount:(MoneyAmount value) ()) (oneof_list [ -10.0; -1.0; 0.0; Float.nan ])
          ; map (fun value -> row ~odometer:(OdometerReading value) ()) (int_range (-10_000) (-1))
          ])
      (fun invalid_row ->
        match classify_row config Normal (context ~row:invalid_row ()) with
        | Rejected { rejection_reason = Row_failed_validation (_ :: _); _ } -> true
        | Accepted _ | Accepted_with_warnings _ | Skipped_duplicate _ | Fatal _ | Rejected _ -> false)
  ]
  |> QCheck_ounit.to_ounit2_test_list

let () = run_test_tt_main ("fuel upload engine" >::: (unit_tests @ qcheck_tests))
