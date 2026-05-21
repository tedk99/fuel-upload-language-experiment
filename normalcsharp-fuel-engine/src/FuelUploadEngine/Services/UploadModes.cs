namespace FuelUploadEngine.Services
{
    // We use string constants for the mode rather than an enum because the
    // request comes in as a JSON string and it's "easier" to just pass it
    // through. (Spoiler: it isn't.)
    public static class UploadModes
    {
        public const string Normal = "Normal";
        public const string Retry = "Retry";
        public const string ConservativeRecovery = "ConservativeRecovery";
        public const string AggressiveRecovery = "AggressiveRecovery";
    }

    public static class PreviousOutcomes
    {
        public const string Finalized = "Finalized";
        public const string RetryableFailure = "RetryableFailure";
        public const string NonRetryableFailure = "NonRetryableFailure";
        public const string FailedBeforeCanonicalFinalization = "FailedBeforeCanonicalFinalization";
        public const string FailedAfterCanonicalFinalizationWithKey = "FailedAfterCanonicalFinalizationWithKey";
        public const string FailedAfterCanonicalFinalizationWithoutKey = "FailedAfterCanonicalFinalizationWithoutKey";
    }

    public static class VehicleLookupStatuses
    {
        public const string Found = "Found";
        public const string NotFound = "NotFound";
        public const string Ambiguous = "Ambiguous";
        public const string Unavailable = "Unavailable";
    }

    public static class DuplicateStatuses
    {
        public const string NotDuplicate = "NotDuplicate";
        public const string Duplicate = "Duplicate";
        public const string Unavailable = "Unavailable";
    }
}
