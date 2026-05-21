using System;
using System.Collections.Generic;
using FuelUploadEngine.Dtos;
using FuelUploadEngine.Models;

namespace FuelUploadEngine.Services
{
    public class FuelUploadService
    {
        public FuelUploadResponseDto Process(FuelUploadRequestDto request)
        {
            var response = new FuelUploadResponseDto();
            response.Decisions = new List<RowDecision>();
            response.Summary = new BatchSummary();

            var config = new ValidationConfig();
            if (request.MaxQuantityLiters > 0) config.MaxQuantityLiters = request.MaxQuantityLiters;
            if (request.MaxUnitCost > 0) config.MaxUnitCost = request.MaxUnitCost;

            foreach (var input in request.Rows)
            {
                var decision = new RowDecision();
                decision.RowNumber = input.RowNumber;

                try
                {
                    ProcessOneRow(input, request.Mode, config, decision);
                }
                catch (ValidationException vex)
                {
                    decision.Status = "Rejected";
                    decision.Errors.Add(vex.Message);
                }
                catch (Exception ex)
                {
                    // Belt and braces: any unexpected exception becomes a
                    // fatal row error so the batch can keep going.
                    decision.Status = "Fatal";
                    decision.FatalMessage = ex.Message;
                }

                response.Decisions.Add(decision);
                LogDecision(decision);
            }

            ComputeSummary(response);
            return response;
        }

        private void ProcessOneRow(
            FuelUploadRowDto input,
            string mode,
            ValidationConfig config,
            RowDecision decision)
        {
            // Vehicle lookup outcomes are propagated as strings from upstream.
            if (input.VehicleLookupStatus == VehicleLookupStatuses.Unavailable)
            {
                decision.Status = "Fatal";
                decision.FatalMessage = "VehicleLookupUnavailable";
                return;
            }

            FuelRowValidator.Validate(input.Row, config);

            if (input.VehicleLookupStatus == VehicleLookupStatuses.NotFound)
            {
                decision.Status = "Rejected";
                decision.Errors.Add("VehicleNotFound:" + input.Row.VehicleRef);
                return;
            }

            if (input.VehicleLookupStatus == VehicleLookupStatuses.Ambiguous)
            {
                decision.Status = "Rejected";
                decision.Errors.Add("AmbiguousVehicle:" + input.Row.VehicleRef);
                return;
            }

            // Vehicle is Found here. (We trust the upstream contract.)
            decision.Vehicle = input.Vehicle;

            if (input.DuplicateStatus == DuplicateStatuses.Unavailable)
            {
                decision.Status = "Fatal";
                decision.FatalMessage = "DuplicateCheckUnavailable";
                return;
            }

            if (input.DuplicateStatus == DuplicateStatuses.Duplicate)
            {
                string skipReason;
                bool accept = DuplicatePolicy.ShouldAcceptDuplicate(mode, input.PreviousOutcome, out skipReason);
                if (!accept)
                {
                    decision.Status = "Skipped";
                    decision.SkipReason = skipReason;
                    return;
                }
                // fall through to acceptance path
            }

            BuildAcceptedDecision(input, config, decision);
        }

        private void BuildAcceptedDecision(
            FuelUploadRowDto input,
            ValidationConfig config,
            RowDecision decision)
        {
            var tx = new FuelTransaction();
            tx.TransactionId = Guid.NewGuid().ToString();
            tx.VehicleId = input.Vehicle.VehicleId;
            tx.OccurredOn = input.Row.OccurredOn;
            tx.QuantityLiters = input.Row.QuantityLiters;
            tx.TotalCost = input.Row.TotalCost;
            tx.UnitCost = input.Row.QuantityLiters > 0
                ? input.Row.TotalCost / input.Row.QuantityLiters
                : 0m;
            tx.MerchantName = input.Row.MerchantName;
            decision.Transaction = tx;

            // Warnings (non-fatal flags).
            if (input.Row.QuantityLiters >= config.QuantityWarnThreshold)
                decision.Warnings.Add("HighQuantity");
            if (tx.UnitCost >= config.UnitCostWarnThreshold)
                decision.Warnings.Add("HighUnitCost");

            // Quarantine reasons (accepted, but flagged for review).
            if (LooksLikeSuspiciousMerchant(input.Row.MerchantName))
                decision.QuarantineReasons.Add("SuspiciousMerchantName");

            if (decision.QuarantineReasons.Count > 0)
            {
                decision.Status = "Quarantined";
            }
            else if (decision.Warnings.Count > 0)
            {
                decision.Status = "AcceptedWithWarnings";
            }
            else
            {
                decision.Status = "Accepted";
            }
        }

        private static bool LooksLikeSuspiciousMerchant(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string upper = name.ToUpper();
            return upper.Contains("TEST") || upper.Contains("XXX");
        }

        private void LogDecision(RowDecision d)
        {
            // Quick debug line that the team added so they could see what's
            // happening in dev. It prints the license plate so you can spot
            // the row. Looks innocent...
            Console.WriteLine(
                "row " + d.RowNumber +
                " plate=" + d.Vehicle.LicensePlate +
                " status=" + d.Status);
        }

        private void ComputeSummary(FuelUploadResponseDto response)
        {
            var s = response.Summary;
            s.TotalRows = response.Decisions.Count;
            foreach (var d in response.Decisions)
            {
                switch (d.Status)
                {
                    case "Accepted": s.Accepted++; break;
                    case "AcceptedWithWarnings": s.AcceptedWithWarnings++; break;
                    case "Quarantined": s.Quarantined++; break;
                    case "Skipped": s.Skipped++; break;
                    case "Rejected": s.Rejected++; break;
                    case "Fatal": s.Fatal++; break;
                    // No default branch -- any "new" status string we forget
                    // to handle here will silently fail to be counted. The
                    // totals won't add up but no error is raised.
                }
            }
        }
    }
}
