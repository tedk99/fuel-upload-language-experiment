using FuelUploadEngine.Application;

namespace FuelUploadEngine.Tests;

public sealed class FuelUploadImportBoundaryTests
{
    [Fact]
    public void Valid_imported_row_maps_and_classifies()
    {
        var result = FuelUploadImportMapper.Classify(ValidImportRequest());

        var success = Assert.IsType<FuelImportMapResult<FuelUploadResponseDto>.Success>(result);
        Assert.Equal(1, success.Value.TotalRows);
        Assert.Equal(1, success.Value.AcceptedTransactions);
        Assert.Equal("accepted", success.Value.Decisions[0].Outcome);
    }

    [Fact]
    public void Missing_required_cell_returns_typed_import_error()
    {
        var request = ValidImportRequest() with
        {
            Rows = [ValidImportRow(1) with { VehicleIdentifier = " " }]
        };

        var failure = Assert.IsType<FuelImportMapResult<FuelUploadRequestDto>.Failure>(
            FuelUploadImportMapper.ToApplicationRequest(request));

        Assert.Contains(
            failure.Errors,
            error => error.Code == FuelImportErrorCode.MissingRequiredCell
                && error.Field == "rows[0].vehicleIdentifier");
    }

    [Fact]
    public void Bad_numeric_value_returns_typed_import_error()
    {
        var request = ValidImportRequest() with
        {
            Rows = [ValidImportRow(1) with { Quantity = "not-a-number" }]
        };

        var failure = Assert.IsType<FuelImportMapResult<FuelUploadRequestDto>.Failure>(
            FuelUploadImportMapper.ToApplicationRequest(request));

        Assert.Contains(
            failure.Errors,
            error => error.Code == FuelImportErrorCode.InvalidNumber
                && error.Field == "rows[0].quantity");
    }

    [Fact]
    public void Unknown_upload_mode_returns_typed_import_error()
    {
        var request = ValidImportRequest() with { UploadMode = "recover-eventually" };

        var failure = Assert.IsType<FuelImportMapResult<FuelUploadRequestDto>.Failure>(
            FuelUploadImportMapper.ToApplicationRequest(request));

        Assert.Contains(
            failure.Errors,
            error => error.Code == FuelImportErrorCode.InvalidUploadMode
                && error.Field == "uploadMode");
    }

    [Fact]
    public void Quarantine_still_works_through_imported_input()
    {
        var request = ValidImportRequest() with
        {
            Rows = [ValidImportRow(1) with { MerchantName = "Manual fuel entry" }]
        };

        var response = Assert.IsType<FuelImportMapResult<FuelUploadResponseDto>.Success>(
            FuelUploadImportMapper.Classify(request)).Value;

        Assert.Equal(1, response.QuarantinedRows);
        Assert.Equal("quarantined", response.Decisions[0].Outcome);
    }

    [Fact]
    public void Import_mapper_does_not_recompute_summary_independently()
    {
        var response = Assert.IsType<FuelImportMapResult<FuelUploadResponseDto>.Success>(
            FuelUploadImportMapper.Classify(ValidImportRequest())).Value;

        Assert.Equal(1, response.TotalRows);
        Assert.Equal(1, response.UploadableTransactions);
        Assert.Equal(
            response.AcceptedTransactions + response.AcceptedWithWarnings,
            response.UploadableTransactions);
    }

    private static ImportBatchRequest ValidImportRequest()
    {
        return new ImportBatchRequest(
            "normal",
            MaximumQuantity: "120",
            MaximumUnitPrice: "5",
            WarningQuantity: "80",
            WarningUnitPrice: "3.5",
            SuspiciousQuantity: "33",
            SuspiciousTotalCost: "99",
            Today: "2026-05-21",
            Rows: [ValidImportRow(1)]);
    }

    private static ImportedFuelRow ValidImportRow(int rowNumber)
    {
        return new ImportedFuelRow(
            RowNumber: rowNumber.ToString(),
            VehicleIdentifier: "REG-1",
            TransactionDate: "2026-05-20",
            Quantity: "13",
            UnitPrice: "3.50",
            MerchantName: "Depot Fuel",
            ExternalReference: $"line-{rowNumber}",
            VehicleLookupStatus: "found",
            VehicleId: "vehicle-1",
            AmbiguousVehicleIds: null,
            VehicleLookupError: null,
            DuplicateStatus: "not_duplicate",
            TransactionKey: $"txn-{rowNumber}",
            PreviousOutcome: null,
            CanonicalTransactionKeyPresent: "true",
            DuplicateError: null);
    }
}
