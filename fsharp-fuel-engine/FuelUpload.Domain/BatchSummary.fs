namespace FuelUpload.Domain

[<CLIMutable>]
type BatchSummary =
    { TotalRows: int
      AcceptedRows: int
      AcceptedWithWarningRows: int
      WarningCount: int
      QuarantinedRows: int
      SkippedDuplicateRows: int
      RejectedRows: int
      FatalErrorRows: int }

[<RequireQualifiedAccess>]
type BatchDecision =
    | Ready of rows: ClassifiedRow list * summary: BatchSummary
    | Blocked of rows: ClassifiedRow list * summary: BatchSummary * fatalErrors: FatalProcessingError list

module BatchSummary =
    let summarize (rows: ClassifiedRow list) =
        let folder summary classified =
            match classified.Decision with
            | RowDecision.Accepted _ ->
                { summary with
                    AcceptedRows = summary.AcceptedRows + 1 }
            | RowDecision.AcceptedWithWarnings(_, warnings) ->
                { summary with
                    AcceptedRows = summary.AcceptedRows + 1
                    AcceptedWithWarningRows = summary.AcceptedWithWarningRows + 1
                    WarningCount = summary.WarningCount + warnings.Length }
            | RowDecision.Quarantined quarantined ->
                { summary with
                    QuarantinedRows = summary.QuarantinedRows + 1
                    WarningCount = summary.WarningCount + quarantined.Warnings.Length }
            | RowDecision.SkippedDuplicate _ ->
                { summary with
                    SkippedDuplicateRows = summary.SkippedDuplicateRows + 1 }
            | RowDecision.Rejected _ ->
                { summary with
                    RejectedRows = summary.RejectedRows + 1 }
            | RowDecision.Fatal _ ->
                { summary with
                    FatalErrorRows = summary.FatalErrorRows + 1 }

        rows
        |> List.fold
            folder
            { TotalRows = rows.Length
              AcceptedRows = 0
              AcceptedWithWarningRows = 0
              WarningCount = 0
              QuarantinedRows = 0
              SkippedDuplicateRows = 0
              RejectedRows = 0
              FatalErrorRows = 0 }
