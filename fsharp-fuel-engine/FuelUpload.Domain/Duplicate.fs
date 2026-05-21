namespace FuelUpload.Domain

[<RequireQualifiedAccess>]
type PreviousAttemptState =
    | Finalized
    | RetryableFailure
    | NonRetryableFailure
    | FailedBeforeCanonicalFinalization
    | FailedAfterCanonicalizationWithCanonicalTransactionKey
    | FailedAfterCanonicalizationWithoutCanonicalTransactionKey

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
