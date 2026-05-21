using FuelUploadEngine.Application;

namespace FuelUploadEngine.Tests;

public sealed class FuelUploadApplicationBoundaryTests
{
    [Fact]
    public void Valid_dto_maps_and_classifies()
    {
        var result = new FuelUploadApplicationService().Classify(ValidRequest());

        var success = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result);
        Assert.Equal(1, success.Value.TotalRows);
        Assert.Equal(1, success.Value.AcceptedTransactions);
        Assert.Equal("accepted", success.Value.Decisions[0].Outcome);
        Assert.Equal("txn-1", success.Value.Decisions[0].TransactionKey);
    }

    [Fact]
    public void Invalid_dto_returns_typed_mapping_error()
    {
        var request = ValidRequest() with { UploadMode = "recover-eventually" };

        var result = FuelUploadMapper.ToDomainRequest(request);

        var failure = Assert.IsType<FuelUploadMapResult<BatchClassificationRequest>.Failure>(result);
        Assert.Contains(
            failure.Errors,
            error => error.Code == FuelUploadMappingErrorCode.InvalidUploadMode
                && error.Field == "uploadMode");
    }

    [Fact]
    public void Response_dto_represents_all_decision_shapes()
    {
        var request = ValidRequest() with
        {
            Rows =
            [
                ValidRow(1),
                ValidRow(2) with { Quantity = 90m, TransactionKey = "txn-2" },
                ValidRow(3) with { Quantity = 33m, TransactionKey = "txn-3" },
                ValidRow(4) with
                {
                    DuplicateStatus = "duplicate",
                    TransactionKey = "existing",
                    PreviousOutcome = "canonical_finalized"
                },
                ValidRow(5) with { Quantity = 0m, TransactionKey = "txn-5" },
                ValidRow(6) with
                {
                    DuplicateStatus = "unavailable",
                    TransactionKey = null,
                    DuplicateError = "duplicate store timed out"
                }
            ]
        };

        var result = new FuelUploadApplicationService().Classify(request);

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Contains(response.Decisions, decision => decision.Outcome == "accepted");
        Assert.Contains(response.Decisions, decision => decision.Outcome == "accepted_with_warnings");
        Assert.Contains(response.Decisions, decision => decision.Outcome == "quarantined");
        Assert.Contains(response.Decisions, decision => decision.Outcome == "skipped_duplicate");
        Assert.Contains(response.Decisions, decision => decision.Outcome == "rejected");
        Assert.Contains(response.Decisions, decision => decision.Outcome == "fatal");
    }

    [Fact]
    public void Response_summary_uses_domain_summary()
    {
        var decision = new BatchDecision(
            [new RowDecision.AcceptedTransaction(Transaction("txn-1"))],
            new BatchSummary(
                TotalRows: 42,
                AcceptedTransactions: 7,
                AcceptedWithoutWarnings: 7,
                AcceptedWithWarnings: 0,
                QuarantinedRows: 5,
                SkippedDuplicates: 4,
                RejectedRows: 3,
                FatalRows: 2,
                WarningCount: 99,
                UploadableTransactions: 0));

        var response = FuelUploadMapper.ToResponseDto(decision);

        Assert.Equal(42, response.TotalRows);
        Assert.Equal(7, response.AcceptedTransactions);
        Assert.Equal(5, response.QuarantinedRows);
        Assert.Equal(99, response.WarningCount);
        Assert.True(response.HasFatalErrors);
    }

    private static FuelUploadRequestDto ValidRequest()
    {
        return new FuelUploadRequestDto(
            "normal",
            MaximumQuantity: 120m,
            MaximumUnitPrice: 5m,
            WarningQuantity: 80m,
            WarningUnitPrice: 3.5m,
            SuspiciousQuantity: 33m,
            SuspiciousTotalCost: 99m,
            Today: "2026-05-21",
            Rows: [ValidRow(1)]);
    }

    private static FuelUploadRowDto ValidRow(int rowNumber)
    {
        return new FuelUploadRowDto(
            rowNumber,
            VehicleIdentifier: "REG-1",
            TransactionDate: "2026-05-20",
            Quantity: 13m,
            UnitPrice: 3.50m,
            MerchantName: "Depot Fuel",
            ExternalReference: $"line-{rowNumber}",
            VehicleLookupStatus: "found",
            VehicleId: "vehicle-1",
            AmbiguousVehicleIds: null,
            VehicleLookupError: null,
            DuplicateStatus: "not_duplicate",
            TransactionKey: $"txn-{rowNumber}",
            PreviousOutcome: null,
            CanonicalTransactionKeyPresent: true,
            DuplicateError: null);
    }

    private static FuelTransaction Transaction(string key)
    {
        return new FuelTransaction(
            new TransactionKey(key),
            new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")),
            new DateOnly(2026, 5, 20),
            10m,
            2m,
            20m,
            new ExternalReference("line-1"));
    }
}
