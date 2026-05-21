namespace FuelUpload.Domain

[<RequireQualifiedAccess>]
type AuditEventKind =
    | Accepted
    | AcceptedWithWarnings
    | Rejected
    | SkippedDuplicate
    | Quarantined
    | FatalBatch

[<CLIMutable>]
type AuditRecord =
    { Kind: AuditEventKind
      RowNumber: int option
      SourceReference: string option
      TransactionId: string option
      VehicleId: string option
      Warnings: Warning list
      QuarantineReasons: QuarantineReason list
      RejectionReasons: RejectionReason list
      DuplicateSkipReason: DuplicateSkipReason option
      FatalError: FatalProcessingError option }

[<CLIMutable>]
type AuditRecordDto =
    { Status: string
      RowNumber: int option
      SourceReference: string option
      TransactionId: string option
      VehicleId: string option
      Warnings: string array
      QuarantineReasons: string array
      RejectionReasons: string array
      DuplicateSkipReason: string
      FatalError: string }

module AuditProjection =
    let status kind =
        match kind with
        | AuditEventKind.Accepted -> "accepted"
        | AuditEventKind.AcceptedWithWarnings -> "accepted_with_warnings"
        | AuditEventKind.Rejected -> "rejected"
        | AuditEventKind.SkippedDuplicate -> "skipped_duplicate"
        | AuditEventKind.Quarantined -> "quarantined"
        | AuditEventKind.FatalBatch -> "fatal_batch"

    let private transactionRecord kind warnings transaction =
        { Kind = kind
          RowNumber = Some transaction.SourceRowNumber
          SourceReference = Some transaction.ExternalReference
          TransactionId = Some transaction.TransactionId
          VehicleId = Some transaction.Vehicle.VehicleId
          Warnings = warnings
          QuarantineReasons = []
          RejectionReasons = []
          DuplicateSkipReason = None
          FatalError = None }

    let private projectClassified classified =
        match classified.Decision with
        | RowDecision.Accepted transaction ->
            transactionRecord AuditEventKind.Accepted [] transaction
        | RowDecision.AcceptedWithWarnings(transaction, warnings) ->
            transactionRecord AuditEventKind.AcceptedWithWarnings warnings transaction
        | RowDecision.Quarantined quarantined ->
            { Kind = AuditEventKind.Quarantined
              RowNumber = Some quarantined.Transaction.SourceRowNumber
              SourceReference = Some quarantined.Transaction.ExternalReference
              TransactionId = Some quarantined.Transaction.TransactionId
              VehicleId = Some quarantined.Transaction.Vehicle.VehicleId
              Warnings = quarantined.Warnings
              QuarantineReasons = QuarantineReasons.toList quarantined.Reasons
              RejectionReasons = []
              DuplicateSkipReason = None
              FatalError = None }
        | RowDecision.SkippedDuplicate skipped ->
            { Kind = AuditEventKind.SkippedDuplicate
              RowNumber = Some skipped.Row.RowNumber
              SourceReference = Some skipped.Row.ExternalReference
              TransactionId = None
              VehicleId = None
              Warnings = []
              QuarantineReasons = []
              RejectionReasons = []
              DuplicateSkipReason = Some skipped.Reason
              FatalError = None }
        | RowDecision.Rejected rejected ->
            { Kind = AuditEventKind.Rejected
              RowNumber = Some rejected.Row.RowNumber
              SourceReference = Some rejected.Row.ExternalReference
              TransactionId = None
              VehicleId = None
              Warnings = []
              QuarantineReasons = []
              RejectionReasons = rejected.Reasons
              DuplicateSkipReason = None
              FatalError = None }
        | RowDecision.Fatal fatal ->
            { Kind = AuditEventKind.FatalBatch
              RowNumber = Some classified.Row.RowNumber
              SourceReference = Some classified.Row.ExternalReference
              TransactionId = None
              VehicleId = None
              Warnings = []
              QuarantineReasons = []
              RejectionReasons = []
              DuplicateSkipReason = None
              FatalError = Some fatal }

    let project decision =
        let rows =
            match decision with
            | BatchDecision.Ready(rows, _) -> rows
            | BatchDecision.Blocked(rows, _, _) -> rows

        rows |> List.map projectClassified

    let toDto record =
        { Status = status record.Kind
          RowNumber = record.RowNumber
          SourceReference = record.SourceReference
          TransactionId = record.TransactionId
          VehicleId = record.VehicleId
          Warnings = record.Warnings |> List.map string |> List.toArray
          QuarantineReasons = record.QuarantineReasons |> List.map string |> List.toArray
          RejectionReasons = record.RejectionReasons |> List.map string |> List.toArray
          DuplicateSkipReason =
            record.DuplicateSkipReason
            |> Option.map string
            |> Option.defaultValue ""
          FatalError =
            record.FatalError
            |> Option.map string
            |> Option.defaultValue "" }
