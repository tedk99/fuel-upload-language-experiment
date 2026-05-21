namespace FuelUpload.Domain

[<RequireQualifiedAccess>]
type OperationalBatchStatus =
    | Ready
    | Fatal

[<CLIMutable>]
type OperationalQuarantinedRow =
    { RowNumber: int
      Reasons: QuarantineReason list }

[<CLIMutable>]
type OperationalBatchReport =
    { Status: OperationalBatchStatus
      Counts: BatchSummary
      UploadedTransactionIds: string list
      RejectedRowNumbers: int list
      QuarantinedRows: OperationalQuarantinedRow list
      SkippedDuplicateRowNumbers: int list
      FatalErrors: FatalProcessingError list }

module OperationalBatchReport =
    let private rowsOf decision =
        match decision with
        | BatchDecision.Ready(rows, _) -> rows
        | BatchDecision.Blocked(rows, _, _) -> rows

    let private summaryOf decision =
        match decision with
        | BatchDecision.Ready(_, summary) -> summary
        | BatchDecision.Blocked(_, summary, _) -> summary

    let private fatalErrorsOf decision =
        match decision with
        | BatchDecision.Ready _ -> []
        | BatchDecision.Blocked(_, _, fatalErrors) -> fatalErrors

    let private statusOf decision =
        match decision with
        | BatchDecision.Ready _ -> OperationalBatchStatus.Ready
        | BatchDecision.Blocked _ -> OperationalBatchStatus.Fatal

    let project decision =
        let rows = rowsOf decision
        let status = statusOf decision

        let uploadedTransactionIds =
            if status = OperationalBatchStatus.Fatal then
                []
            else
                rows
                |> List.choose (fun classified ->
                    match classified.Decision with
                    | RowDecision.Accepted transaction
                    | RowDecision.AcceptedWithWarnings(transaction, _) -> Some transaction.TransactionId
                    | RowDecision.Quarantined _
                    | RowDecision.SkippedDuplicate _
                    | RowDecision.Rejected _
                    | RowDecision.Fatal _ -> None)

        { Status = status
          Counts = summaryOf decision
          UploadedTransactionIds = uploadedTransactionIds
          RejectedRowNumbers =
            rows
            |> List.choose (fun classified ->
                match classified.Decision with
                | RowDecision.Rejected rejected -> Some rejected.Row.RowNumber
                | _ -> None)
          QuarantinedRows =
            rows
            |> List.choose (fun classified ->
                match classified.Decision with
                | RowDecision.Quarantined quarantined ->
                    Some
                        { RowNumber = quarantined.Transaction.SourceRowNumber
                          Reasons = QuarantineReasons.toList quarantined.Reasons }
                | _ -> None)
          SkippedDuplicateRowNumbers =
            rows
            |> List.choose (fun classified ->
                match classified.Decision with
                | RowDecision.SkippedDuplicate skipped -> Some skipped.Row.RowNumber
                | _ -> None)
          FatalErrors = fatalErrorsOf decision }
