using System;
using FuelUploadEngine.Models;

namespace FuelUploadEngine.Services
{
    public static class FuelRowValidator
    {
        // Throws on the *first* failure. The orchestrator catches the
        // exception and turns it into a Rejected decision. Yes, this means
        // you only ever see one validation error at a time even when many
        // fields are bad. That's been considered "fine" because callers
        // mostly just look at the first one anyway.
        public static void Validate(FuelRow row, ValidationConfig config)
        {
            if (row.QuantityLiters <= config.MinQuantityLiters)
                throw new ValidationException("QuantityNotPositive");

            if (row.QuantityLiters > config.MaxQuantityLiters)
                throw new ValidationException("QuantityExceedsMaximum");

            if (row.TotalCost <= 0m)
                throw new ValidationException("CostNotPositive");

            if (string.IsNullOrEmpty(row.VehicleRef))
                throw new ValidationException("VehicleRefRequired");

            if (string.IsNullOrEmpty(row.MerchantName))
                throw new ValidationException("MerchantRequired");

            if (row.OccurredOn > DateTime.UtcNow)
                throw new ValidationException("TransactionDateInFuture");
        }
    }
}
