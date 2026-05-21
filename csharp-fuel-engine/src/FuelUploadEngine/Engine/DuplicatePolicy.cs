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
            UploadMode.Recovery when duplicate.PreviousOutcome is PreviousUploadOutcome.FailedBeforeCanonicalFinalization => null,
            UploadMode.Recovery => new RowDecision.SkippedDuplicate(
                row.RowNumber,
                duplicate,
                DuplicateSkipCode.PreviousAttemptAlreadyCanonicalized),
            _ => throw new InvalidOperationException("Unhandled upload mode.")
        };
    }
}
