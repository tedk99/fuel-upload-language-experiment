namespace FuelUploadEngine.Tests;

public sealed class FuelUploadDeciderTests
{
    private static readonly ValidationConfig StrictConfig = new(
        MaximumQuantity: 120m,
        MaximumUnitPrice: 5.00m,
        WarningQuantity: 80m,
        WarningUnitPrice: 3.50m,
        SuspiciousQuantity: 33m,
        SuspiciousTotalCost: 99m,
        Today: new DateOnly(2026, 5, 21));

    private static readonly Vehicle KnownVehicle = new(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1"));

    [Fact]
    public void ClassifyRow_accepts_valid_non_duplicate_row()
    {
        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            FoundVehicle(),
            NotDuplicate("new-key"),
            StrictConfig,
            UploadMode.Normal);

        var accepted = Assert.IsType<RowDecision.AcceptedTransaction>(decision);
        Assert.Equal(new TransactionKey("new-key"), accepted.Transaction.Key);
        Assert.Equal(45.50m, accepted.Transaction.GrossAmount);
    }

    [Fact]
    public void ClassifyRow_rejects_validation_errors_instead_of_accepting()
    {
        var invalid = ValidRow(quantity: 0m, unitPrice: 6m);

        var decision = FuelUploadDecider.ClassifyRow(
            invalid,
            FoundVehicle(),
            NotDuplicate("new-key"),
            StrictConfig,
            UploadMode.Normal);

        var rejected = Assert.IsType<RowDecision.RejectedRow>(decision);
        var reason = Assert.IsType<RejectionReason.ValidationFailed>(rejected.Reason);
        Assert.Contains(reason.Errors, error => error.Code == ValidationErrorCode.NonPositiveQuantity);
        Assert.Contains(reason.Errors, error => error.Code == ValidationErrorCode.UnitPriceExceedsMaximum);
    }

    [Fact]
    public void ClassifyRow_rejects_missing_vehicle_with_typed_reason()
    {
        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            new VehicleLookupResult.NotFound(new VehicleIdentifier("REG-1")),
            NotDuplicate("new-key"),
            StrictConfig,
            UploadMode.Normal);

        var rejected = Assert.IsType<RowDecision.RejectedRow>(decision);
        Assert.IsType<RejectionReason.VehicleNotFound>(rejected.Reason);
        Assert.Equal(RejectionCode.VehicleNotFound, rejected.Reason.Code);
    }

    [Fact]
    public void ClassifyRow_returns_fatal_error_for_unavailable_lookup()
    {
        var fatal = new FatalError(FatalErrorCode.VehicleLookupUnavailable, "lookup transport failed");

        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            new VehicleLookupResult.Unavailable(fatal),
            NotDuplicate("new-key"),
            StrictConfig,
            UploadMode.Normal);

        var fatalDecision = Assert.IsType<RowDecision.FatalProcessingError>(decision);
        Assert.Equal(fatal, fatalDecision.Error);
    }

    [Fact]
    public void ClassifyRow_skips_duplicate_in_normal_mode()
    {
        var duplicate = Duplicate(new PreviousUploadOutcome.RetryableFailure());

        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            FoundVehicle(),
            duplicate,
            StrictConfig,
            UploadMode.Normal);

        var skipped = Assert.IsType<RowDecision.SkippedDuplicate>(decision);
        Assert.Equal(DuplicateSkipCode.DuplicateInNormalMode, skipped.Reason);
    }

    [Theory]
    [MemberData(nameof(RetryDuplicateCases))]
    public void ClassifyRow_retry_accepts_only_explicitly_retryable_duplicates(
        PreviousUploadOutcome previousOutcome,
        bool shouldAccept)
    {
        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            FoundVehicle(),
            Duplicate(previousOutcome),
            StrictConfig,
            UploadMode.Retry);

        if (shouldAccept)
        {
            var accepted = Assert.IsType<RowDecision.AcceptedTransaction>(decision);
            Assert.Equal(new TransactionKey("existing-key"), accepted.Transaction.Key);
        }
        else
        {
            var skipped = Assert.IsType<RowDecision.SkippedDuplicate>(decision);
            Assert.Equal(DuplicateSkipCode.PreviousAttemptNotRetryable, skipped.Reason);
        }
    }

    [Theory]
    [MemberData(nameof(RecoveryDuplicateCases))]
    public void ClassifyRow_recovery_accepts_only_failures_before_canonical_finalization(
        PreviousUploadOutcome previousOutcome,
        bool shouldAccept)
    {
        var decision = FuelUploadDecider.ClassifyRow(
            ValidRow(),
            FoundVehicle(),
            Duplicate(previousOutcome),
            StrictConfig,
            UploadMode.Recovery);

        if (shouldAccept)
        {
            var accepted = Assert.IsType<RowDecision.AcceptedTransaction>(decision);
            Assert.Equal(new TransactionKey("existing-key"), accepted.Transaction.Key);
        }
        else
        {
            var skipped = Assert.IsType<RowDecision.SkippedDuplicate>(decision);
            Assert.Equal(DuplicateSkipCode.PreviousAttemptAlreadyCanonicalized, skipped.Reason);
        }
    }

    [Fact]
    public void ClassifyRow_warnings_do_not_block_accepted_transactions()
    {
        var warningRow = ValidRow(quantity: 90m, unitPrice: 4m);

        var decision = FuelUploadDecider.ClassifyRow(
            warningRow,
            FoundVehicle(),
            NotDuplicate("warning-key"),
            StrictConfig,
            UploadMode.Normal);

        var acceptedWithWarnings = Assert.IsType<RowDecision.AcceptedTransactionWithWarnings>(decision);
        Assert.Equal(new TransactionKey("warning-key"), acceptedWithWarnings.Transaction.Key);
        Assert.Contains(acceptedWithWarnings.Warnings, warning => warning.Code == WarningCode.QuantityAboveWarningThreshold);
        Assert.Contains(acceptedWithWarnings.Warnings, warning => warning.Code == WarningCode.UnitPriceAboveWarningThreshold);
    }

    [Fact]
    public void ClassifyRow_quarantines_suspicious_row_with_typed_reason()
    {
        var suspiciousRow = ValidRow(merchantName: "Manual fuel entry");

        var decision = FuelUploadDecider.ClassifyRow(
            suspiciousRow,
            FoundVehicle(),
            NotDuplicate("manual-key"),
            StrictConfig,
            UploadMode.Normal);

        var quarantined = Assert.IsType<RowDecision.QuarantinedRow>(decision);
        Assert.Equal(new TransactionKey("manual-key"), quarantined.Transaction.Key);
        Assert.Contains(
            quarantined.Reasons,
            reason => reason.Code == QuarantineReasonCode.SuspiciousMerchantName);
        Assert.NotEmpty(quarantined.Reasons);
    }

    [Fact]
    public void ClassifyRow_validation_error_is_rejected_not_quarantined()
    {
        var invalidSuspiciousRow = ValidRow(quantity: 0m, merchantName: "manual review");

        var decision = FuelUploadDecider.ClassifyRow(
            invalidSuspiciousRow,
            FoundVehicle(),
            NotDuplicate("invalid-key"),
            StrictConfig,
            UploadMode.Normal);

        Assert.IsType<RowDecision.RejectedRow>(decision);
    }

    [Fact]
    public void ClassifyRow_duplicate_normal_mode_is_skipped_not_quarantined()
    {
        var suspiciousDuplicate = ValidRow(merchantName: "test merchant");

        var decision = FuelUploadDecider.ClassifyRow(
            suspiciousDuplicate,
            FoundVehicle(),
            Duplicate(new PreviousUploadOutcome.CanonicalFinalized()),
            StrictConfig,
            UploadMode.Normal);

        Assert.IsType<RowDecision.SkippedDuplicate>(decision);
    }

    [Fact]
    public void ClassifyRow_warning_does_not_become_quarantine()
    {
        var warningRow = ValidRow(quantity: 90m, unitPrice: 4m);

        var decision = FuelUploadDecider.ClassifyRow(
            warningRow,
            FoundVehicle(),
            NotDuplicate("warning-key"),
            StrictConfig,
            UploadMode.Normal);

        Assert.IsType<RowDecision.AcceptedTransactionWithWarnings>(decision);
    }

    [Fact]
    public void ClassifyBatch_blocks_uploadable_transactions_when_any_row_is_fatal()
    {
        var request = new BatchClassificationRequest(
            new[]
            {
                new BatchRowInput(ValidRow(rowNumber: 1), FoundVehicle(), NotDuplicate("accepted-key")),
                new BatchRowInput(
                    ValidRow(rowNumber: 2),
                    FoundVehicle(),
                    new DuplicateCheckResult.Unavailable(new FatalError(
                        FatalErrorCode.DuplicateCheckUnavailable,
                        "duplicate service timed out")))
            },
            StrictConfig,
            UploadMode.Normal);

        var decision = FuelUploadDecider.ClassifyBatch(request);

        Assert.True(decision.HasFatalErrors);
        Assert.Equal(2, decision.Summary.TotalRows);
        Assert.Equal(1, decision.Summary.AcceptedTransactions);
        Assert.Equal(1, decision.Summary.FatalRows);
        Assert.Equal(0, decision.Summary.UploadableTransactions);
    }

    [Fact]
    public void ClassifyBatch_quarantined_row_does_not_upload_or_block_and_appears_in_summary()
    {
        var request = new BatchClassificationRequest(
            new[]
            {
                new BatchRowInput(ValidRow(rowNumber: 1), FoundVehicle(), NotDuplicate("accepted-key")),
                new BatchRowInput(ValidRow(rowNumber: 2, quantity: 33m), FoundVehicle(), NotDuplicate("quarantined-key"))
            },
            StrictConfig,
            UploadMode.Normal);

        var decision = FuelUploadDecider.ClassifyBatch(request);

        Assert.False(decision.HasFatalErrors);
        Assert.Equal(2, decision.Summary.TotalRows);
        Assert.Equal(1, decision.Summary.AcceptedTransactions);
        Assert.Equal(1, decision.Summary.QuarantinedRows);
        Assert.Equal(1, decision.Summary.UploadableTransactions);
        Assert.Contains(decision.RowDecisions, row => row is RowDecision.QuarantinedRow);
    }

    [Fact]
    public void ClassifyBatch_derives_summary_counts_from_row_decisions()
    {
        var request = new BatchClassificationRequest(
            new[]
            {
                new BatchRowInput(ValidRow(rowNumber: 1), FoundVehicle(), NotDuplicate("accepted-key")),
                new BatchRowInput(ValidRow(rowNumber: 2, quantity: 90m), FoundVehicle(), NotDuplicate("warning-key")),
                new BatchRowInput(ValidRow(rowNumber: 3), FoundVehicle(), Duplicate(new PreviousUploadOutcome.CanonicalFinalized())),
                new BatchRowInput(ValidRow(rowNumber: 4, quantity: 999m), FoundVehicle(), NotDuplicate("rejected-key")),
                new BatchRowInput(
                    ValidRow(rowNumber: 5),
                    new VehicleLookupResult.Unavailable(new FatalError(
                        FatalErrorCode.VehicleLookupUnavailable,
                        "lookup offline")),
                    NotDuplicate("fatal-key"))
            },
            StrictConfig,
            UploadMode.Normal);

        var decision = FuelUploadDecider.ClassifyBatch(request);

        Assert.Equal(5, decision.Summary.TotalRows);
        Assert.Equal(2, decision.Summary.AcceptedTransactions);
        Assert.Equal(1, decision.Summary.AcceptedWithoutWarnings);
        Assert.Equal(1, decision.Summary.AcceptedWithWarnings);
        Assert.Equal(0, decision.Summary.QuarantinedRows);
        Assert.Equal(1, decision.Summary.SkippedDuplicates);
        Assert.Equal(1, decision.Summary.RejectedRows);
        Assert.Equal(1, decision.Summary.FatalRows);
        Assert.Equal(1, decision.Summary.WarningCount);
        Assert.Equal(0, decision.Summary.UploadableTransactions);
    }

    public static IEnumerable<object[]> RetryDuplicateCases()
    {
        yield return new object[] { new PreviousUploadOutcome.RetryableFailure(), true };
        yield return new object[] { new PreviousUploadOutcome.CanonicalFinalized(), false };
        yield return new object[] { new PreviousUploadOutcome.NonRetryableFailure(), false };
        yield return new object[] { new PreviousUploadOutcome.FailedBeforeCanonicalFinalization(), false };
        yield return new object[] { new PreviousUploadOutcome.FailedAfterCanonicalFinalization(), false };
    }

    public static IEnumerable<object[]> RecoveryDuplicateCases()
    {
        yield return new object[] { new PreviousUploadOutcome.FailedBeforeCanonicalFinalization(), true };
        yield return new object[] { new PreviousUploadOutcome.CanonicalFinalized(), false };
        yield return new object[] { new PreviousUploadOutcome.RetryableFailure(), false };
        yield return new object[] { new PreviousUploadOutcome.NonRetryableFailure(), false };
        yield return new object[] { new PreviousUploadOutcome.FailedAfterCanonicalFinalization(), false };
    }

    private static FuelRow ValidRow(
        int rowNumber = 1,
        decimal quantity = 13m,
        decimal unitPrice = 3.50m,
        string merchantName = "Depot Fuel")
    {
        return new FuelRow(
            new RowNumber(rowNumber),
            new VehicleIdentifier("REG-1"),
            new DateOnly(2026, 5, 20),
            quantity,
            unitPrice,
            merchantName,
            new ExternalReference($"line-{rowNumber}"));
    }

    private static VehicleLookupResult FoundVehicle()
    {
        return new VehicleLookupResult.Found(KnownVehicle);
    }

    private static DuplicateCheckResult NotDuplicate(string key)
    {
        return new DuplicateCheckResult.NotDuplicate(new TransactionKey(key));
    }

    private static DuplicateCheckResult Duplicate(PreviousUploadOutcome previousOutcome)
    {
        return new DuplicateCheckResult.Duplicate(new DuplicateState(new TransactionKey("existing-key"), previousOutcome));
    }
}
