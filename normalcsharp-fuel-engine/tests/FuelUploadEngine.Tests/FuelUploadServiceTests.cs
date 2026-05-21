using System;
using System.Collections.Generic;
using FuelUploadEngine.Dtos;
using FuelUploadEngine.Models;
using FuelUploadEngine.Services;
using Xunit;

namespace FuelUploadEngine.Tests
{
    public class FuelUploadServiceTests
    {
        private static FuelRow Row(decimal qty = 50m, decimal cost = 100m, string merchant = "Shell")
        {
            return new FuelRow
            {
                RowNumber = 1,
                VehicleRef = "TRUCK-7",
                SourceId = "src-1",
                OccurredOn = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                QuantityLiters = qty,
                TotalCost = cost,
                MerchantName = merchant,
                Odometer = 12345,
            };
        }

        private static Vehicle FoundVehicle()
        {
            return new Vehicle
            {
                VehicleId = "veh-1",
                LicensePlate = "ABC-123",
                FuelType = "Diesel",
            };
        }

        private static FuelUploadRequestDto Request(
            string mode,
            FuelUploadRowDto row)
        {
            return new FuelUploadRequestDto
            {
                Mode = mode,
                MaxQuantityLiters = 500m,
                MaxUnitCost = 10m,
                Rows = new List<FuelUploadRowDto> { row },
            };
        }

        private static FuelUploadRowDto Found(FuelRow row, string duplicateStatus = "NotDuplicate", string previousOutcome = null)
        {
            return new FuelUploadRowDto
            {
                RowNumber = row.RowNumber,
                Row = row,
                VehicleLookupStatus = "Found",
                Vehicle = FoundVehicle(),
                DuplicateStatus = duplicateStatus,
                PreviousOutcome = previousOutcome,
            };
        }

        // ---------------- HAPPY PATH ----------------

        [Fact]
        public void HappyPath_RowIsAccepted()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(UploadModes.Normal, Found(Row())));

            Assert.Single(resp.Decisions);
            Assert.Equal("Accepted", resp.Decisions[0].Status);
            Assert.Equal(1, resp.Summary.Accepted);
            Assert.NotNull(resp.Decisions[0].Transaction);
        }

        [Fact]
        public void HighQuantity_TriggersWarning()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(UploadModes.Normal, Found(Row(qty: 400m, cost: 800m))));

            Assert.Equal("AcceptedWithWarnings", resp.Decisions[0].Status);
            Assert.Contains("HighQuantity", resp.Decisions[0].Warnings);
        }

        [Fact]
        public void SuspiciousMerchant_QuarantinesRow()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(UploadModes.Normal, Found(Row(merchant: "TEST STATION"))));

            Assert.Equal("Quarantined", resp.Decisions[0].Status);
            Assert.Contains("SuspiciousMerchantName", resp.Decisions[0].QuarantineReasons);
        }

        [Fact]
        public void Duplicate_InNormalMode_IsSkipped()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(
                UploadModes.Normal,
                Found(Row(), duplicateStatus: "Duplicate", previousOutcome: PreviousOutcomes.RetryableFailure)));

            Assert.Equal("Skipped", resp.Decisions[0].Status);
            Assert.Equal("NormalModeDuplicate", resp.Decisions[0].SkipReason);
        }

        [Fact]
        public void Duplicate_InRetryMode_RetryableFailure_IsAccepted()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(
                UploadModes.Retry,
                Found(Row(), duplicateStatus: "Duplicate", previousOutcome: PreviousOutcomes.RetryableFailure)));

            Assert.Equal("Accepted", resp.Decisions[0].Status);
        }

        [Fact]
        public void Duplicate_InAggressiveRecovery_FailedBeforeCanonical_IsAccepted()
        {
            var svc = new FuelUploadService();
            var resp = svc.Process(Request(
                UploadModes.AggressiveRecovery,
                Found(Row(), duplicateStatus: "Duplicate", previousOutcome: PreviousOutcomes.FailedBeforeCanonicalFinalization)));

            Assert.Equal("Accepted", resp.Decisions[0].Status);
        }
    }

    // ---------------- BUG DEMONSTRATIONS ----------------
    //
    // These tests intentionally pin the *current buggy behavior* so juniors
    // can see the failure modes the type system doesn't catch. Fixing the
    // bug would make the corresponding test fail -- which is the point.

    public class FuelUploadServiceBugTests
    {
        private static FuelUploadRequestDto Request(string mode, FuelUploadRowDto row)
        {
            return new FuelUploadRequestDto
            {
                Mode = mode,
                MaxQuantityLiters = 500m,
                MaxUnitCost = 10m,
                Rows = new List<FuelUploadRowDto> { row },
            };
        }

        private static FuelRow GoodRow()
        {
            return new FuelRow
            {
                RowNumber = 1,
                VehicleRef = "TRUCK-7",
                SourceId = "src-1",
                OccurredOn = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                QuantityLiters = 50m,
                TotalCost = 100m,
                MerchantName = "Shell",
                Odometer = 12345,
            };
        }

        private static Vehicle FoundVehicle() => new Vehicle
        {
            VehicleId = "veh-1",
            LicensePlate = "ABC-123",
            FuelType = "Diesel",
        };

        [Fact]
        public void Bug1_NullRefInLoggerWhenVehicleNotFound()
        {
            // VehicleLookupStatus = "NotFound" leaves decision.Vehicle == null,
            // and the debug logger does decision.Vehicle.LicensePlate without
            // a null check. Bug: the whole batch crashes on the first
            // not-found row.
            var svc = new FuelUploadService();
            var row = new FuelUploadRowDto
            {
                RowNumber = 1,
                Row = GoodRow(),
                VehicleLookupStatus = "NotFound",
                DuplicateStatus = "NotDuplicate",
            };
            Assert.Throws<NullReferenceException>(() => svc.Process(Request(UploadModes.Normal, row)));
        }

        [Fact]
        public void Bug2_RetryModeIsCaseSensitive()
        {
            // The mode is a string. The producer sent "retry" (lowercase, as
            // most JSON APIs send enums). The string comparison is the default
            // ordinal one, so it doesn't match UploadModes.Retry == "Retry".
            // The duplicate gets skipped as UnknownMode instead of being
            // retried.
            var svc = new FuelUploadService();
            var row = new FuelUploadRowDto
            {
                RowNumber = 1,
                Row = GoodRow(),
                VehicleLookupStatus = "Found",
                Vehicle = FoundVehicle(),
                DuplicateStatus = "Duplicate",
                PreviousOutcome = PreviousOutcomes.RetryableFailure,
            };
            var resp = svc.Process(Request("retry", row));

            Assert.Equal("Skipped", resp.Decisions[0].Status);
            Assert.Equal("UnknownMode", resp.Decisions[0].SkipReason);
        }

        [Fact]
        public void Bug3_AggressiveRecovery_MissesFailedAfterCanonicalWithoutKey()
        {
            // The aggressive recovery contract says: if the canonical key
            // never landed, we should re-upload. That branch isn't in the
            // code -- so the row gets skipped instead of accepted. The type
            // system can't catch it because previousOutcome is a string.
            var svc = new FuelUploadService();
            var row = new FuelUploadRowDto
            {
                RowNumber = 1,
                Row = GoodRow(),
                VehicleLookupStatus = "Found",
                Vehicle = FoundVehicle(),
                DuplicateStatus = "Duplicate",
                PreviousOutcome = PreviousOutcomes.FailedAfterCanonicalFinalizationWithoutKey,
            };
            var resp = svc.Process(Request(UploadModes.AggressiveRecovery, row));

            Assert.Equal("Skipped", resp.Decisions[0].Status);
            Assert.Equal("AggressiveRecoverySkipped", resp.Decisions[0].SkipReason);
        }

        [Fact]
        public void Bug4_DecisionListIsMutableByCaller()
        {
            // RowDecision.Errors is a public mutable List<string> field --
            // the caller can mutate it after Process() returns. Nothing
            // stops downstream code from corrupting the response.
            var svc = new FuelUploadService();
            var row = new FuelUploadRowDto
            {
                RowNumber = 1,
                Row = GoodRow(),
                VehicleLookupStatus = "Found",
                Vehicle = FoundVehicle(),
                DuplicateStatus = "NotDuplicate",
            };
            var resp = svc.Process(Request(UploadModes.Normal, row));

            resp.Decisions[0].Errors.Add("oops, mutated from outside");
            Assert.Contains("oops, mutated from outside", resp.Decisions[0].Errors);
        }
    }
}
