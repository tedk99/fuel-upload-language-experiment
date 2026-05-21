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

    [Fact]
    public void Repository_vehicle_match_leads_to_normal_classification()
    {
        var result = RepositoryService(
            new VehicleRepositoryResult.Success(new VehicleLookupResult.Found(
                new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")))),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.NotDuplicate(new TransactionKey("txn-1"))))
            .Classify(RepositoryRequest());

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Equal("accepted", response.Decisions[0].Outcome);
        Assert.Equal("vehicle-1", response.Decisions[0].VehicleId);
    }

    [Fact]
    public void Repository_missing_vehicle_uses_existing_not_found_behavior()
    {
        var result = RepositoryService(
            new VehicleRepositoryResult.Success(new VehicleLookupResult.NotFound(new VehicleIdentifier("REG-1"))),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.NotDuplicate(new TransactionKey("txn-1"))))
            .Classify(RepositoryRequest());

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Equal("rejected", response.Decisions[0].Outcome);
        Assert.Equal(nameof(RejectionCode.VehicleNotFound), response.Decisions[0].RejectionCode);
    }

    [Fact]
    public void Repository_duplicate_state_leads_to_skipped_duplicate()
    {
        var result = RepositoryService(
            new VehicleRepositoryResult.Success(new VehicleLookupResult.Found(
                new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")))),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.Duplicate(
                new DuplicateState(
                    new TransactionKey("existing"),
                    new PreviousUploadOutcome.CanonicalFinalized()))))
            .Classify(RepositoryRequest());

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Equal("skipped_duplicate", response.Decisions[0].Outcome);
        Assert.Equal("existing", response.Decisions[0].TransactionKey);
    }

    [Fact]
    public void Repository_failure_is_typed_and_not_a_validation_error()
    {
        var repositoryError = new VehicleRepositoryError(VehicleRepositoryErrorCode.TimedOut, "vehicle store timed out");
        var result = RepositoryService(
            new VehicleRepositoryResult.Failure(repositoryError),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.NotDuplicate(new TransactionKey("txn-1"))))
            .Classify(RepositoryRequest());

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Equal("fatal", response.Decisions[0].Outcome);
        Assert.Equal(nameof(FatalErrorCode.VehicleLookupUnavailable), response.Decisions[0].FatalCode);
        Assert.Empty(response.Decisions[0].ValidationErrors);
        Assert.Equal(VehicleRepositoryErrorCode.TimedOut, repositoryError.Code);
    }

    [Fact]
    public void Quarantine_still_works_with_repository_backed_service()
    {
        var request = RepositoryRequest() with
        {
            Rows = [RepositoryRow() with { MerchantName = "Manual fuel entry" }]
        };

        var result = RepositoryService(
            new VehicleRepositoryResult.Success(new VehicleLookupResult.Found(
                new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")))),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.NotDuplicate(new TransactionKey("txn-1"))))
            .Classify(request);

        var response = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(result).Value;
        Assert.Equal("quarantined", response.Decisions[0].Outcome);
        Assert.Equal(1, response.QuarantinedRows);
    }

    [Fact]
    public void Repository_backed_service_summary_matches_application_summary()
    {
        var request = RepositoryRequest();
        var repositoryResult = RepositoryService(
            new VehicleRepositoryResult.Success(new VehicleLookupResult.Found(
                new Vehicle(new VehicleId("vehicle-1"), new VehicleIdentifier("REG-1")))),
            new DuplicateRepositoryResult.Success(new DuplicateCheckResult.NotDuplicate(new TransactionKey("txn-1"))))
            .Classify(request);
        var dtoResult = new FuelUploadApplicationService().Classify(
            request with { Rows = [ValidRow(1)] });

        var repositoryResponse = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(repositoryResult).Value;
        var dtoResponse = Assert.IsType<FuelUploadMapResult<FuelUploadResponseDto>.Success>(dtoResult).Value;
        Assert.Equal(dtoResponse.TotalRows, repositoryResponse.TotalRows);
        Assert.Equal(dtoResponse.AcceptedTransactions, repositoryResponse.AcceptedTransactions);
        Assert.Equal(dtoResponse.UploadableTransactions, repositoryResponse.UploadableTransactions);
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

    private static FuelUploadRequestDto RepositoryRequest()
    {
        return ValidRequest() with { Rows = [RepositoryRow()] };
    }

    private static FuelUploadRowDto RepositoryRow()
    {
        return ValidRow(1) with
        {
            VehicleLookupStatus = null,
            VehicleId = null,
            DuplicateStatus = null,
            TransactionKey = null
        };
    }

    private static RepositoryFuelUploadApplicationService RepositoryService(
        VehicleRepositoryResult vehicleResult,
        DuplicateRepositoryResult duplicateResult)
    {
        return new RepositoryFuelUploadApplicationService(
            new FakeVehicleRepository(vehicleResult),
            new FakeDuplicateRepository(duplicateResult));
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

    private sealed class FakeVehicleRepository(VehicleRepositoryResult result) : IVehicleRepository
    {
        public VehicleRepositoryResult Lookup(VehicleIdentifier identifier) => result;
    }

    private sealed class FakeDuplicateRepository(DuplicateRepositoryResult result) : IDuplicateRepository
    {
        public DuplicateRepositoryResult Lookup(DuplicateLookup lookup) => result;
    }
}
