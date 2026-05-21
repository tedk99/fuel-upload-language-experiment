namespace FuelUploadEngine;

public static class DuplicatePolicy
{
    public static RowDecision? ClassifyDuplicate(
        FuelRow row,
        Vehicle vehicle,
        DuplicateState duplicate,
        ValidationConfig validationConfig,
        UploadMode mode)
    {
        return mode switch
        {
            UploadMode.Normal => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.DuplicateInNormalMode),
            UploadMode.Retry when duplicate.PreviousOutcome is PreviousUploadOutcome.RetryableFailure => null,
            UploadMode.Retry => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptNotRetryable),
            UploadMode.ConservativeRecovery when duplicate.PreviousOutcome is PreviousUploadOutcome.FailedBeforeCanonicalFinalization => null,
            UploadMode.ConservativeRecovery => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptAlreadyCanonicalized),
            UploadMode.AggressiveRecovery when duplicate.PreviousOutcome is PreviousUploadOutcome.FailedBeforeCanonicalFinalization => null,
            UploadMode.AggressiveRecovery
                when duplicate.PreviousOutcome is PreviousUploadOutcome.FailedAfterCanonicalFinalization
                    && duplicate.CanonicalTransactionKey is CanonicalTransactionKeyState.Missing => null,
            UploadMode.AggressiveRecovery => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptAlreadyCanonicalized),
            _ => throw new InvalidOperationException("Unhandled upload mode.")
        };
    }
}
