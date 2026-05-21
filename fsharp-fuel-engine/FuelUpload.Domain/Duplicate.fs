namespace FuelUpload.Domain

[<RequireQualifiedAccess>]
type PreviousAttemptState =
    | Finalized
    | RetryableFailure
    | NonRetryableFailure
    | FailedBeforeCanonicalFinalization

[<RequireQualifiedAccess>]
type DuplicateCheckResult =
    | NoDuplicate
    | Duplicate of PreviousAttemptState
    | Fatal of FatalProcessingError

[<RequireQualifiedAccess>]
type DuplicateSkipReason =
    | NormalModeDuplicate
    | RetryModeDuplicateAlreadyFinalized
    | RetryModeDuplicateNotRetryable of PreviousAttemptState
    | RecoveryModeDuplicateAlreadyCanonicalized of PreviousAttemptState
