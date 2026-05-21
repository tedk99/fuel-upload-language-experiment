using FuelUploadEngine.Models;

namespace FuelUploadEngine.Services
{
    public static class DuplicatePolicy
    {
        // Returns true if the duplicate should be UPLOADED ANYWAY (recovery
        // semantics). Returns false if it should be skipped.
        public static bool ShouldAcceptDuplicate(string mode, string previousOutcome, out string skipReason)
        {
            skipReason = "";

            // Normal mode: never accept duplicates.
            if (mode == UploadModes.Normal)
            {
                skipReason = "NormalModeDuplicate";
                return false;
            }

            // Retry mode: only previously-retryable failures.
            if (mode == UploadModes.Retry)
            {
                if (previousOutcome == PreviousOutcomes.RetryableFailure)
                    return true;
                skipReason = "RetryModeNotRetryable";
                return false;
            }

            // Conservative recovery: only failures that didn't reach canonical
            // finalization. Anything past that point is too risky to redo.
            if (mode == UploadModes.ConservativeRecovery)
            {
                if (previousOutcome == PreviousOutcomes.FailedBeforeCanonicalFinalization)
                    return true;
                skipReason = "ConservativeRecoveryAlreadyCanonical";
                return false;
            }

            // Aggressive recovery: same as conservative, plus we also retry
            // anything that failed BEFORE the canonical write.
            if (mode == UploadModes.AggressiveRecovery)
            {
                if (previousOutcome == PreviousOutcomes.FailedBeforeCanonicalFinalization)
                    return true;
                // NOTE: we should also retry FailedAfterCanonicalFinalizationWithoutKey here
                // -- the aggressive contract says when the canonical key never landed,
                // we should re-upload. But this branch was forgotten. See planted bug #3.
                skipReason = "AggressiveRecoverySkipped";
                return false;
            }

            // Unknown mode -- treat conservatively (skip).
            skipReason = "UnknownMode";
            return false;
        }
    }
}
