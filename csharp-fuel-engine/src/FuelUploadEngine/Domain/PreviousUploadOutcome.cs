namespace FuelUploadEngine;

public abstract record PreviousUploadOutcome
{
    private PreviousUploadOutcome()
    {
    }

    public sealed record CanonicalFinalized : PreviousUploadOutcome;

    public sealed record RetryableFailure : PreviousUploadOutcome;

    public sealed record NonRetryableFailure : PreviousUploadOutcome;

    public sealed record FailedBeforeCanonicalFinalization : PreviousUploadOutcome;

    public sealed record FailedAfterCanonicalFinalization : PreviousUploadOutcome;
}
