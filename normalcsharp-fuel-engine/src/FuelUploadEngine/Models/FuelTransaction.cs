using System;

namespace FuelUploadEngine.Models
{
    public class FuelTransaction
    {
        public string TransactionId { get; set; }
        public string VehicleId { get; set; }
        public DateTime OccurredOn { get; set; }
        public decimal QuantityLiters { get; set; }
        public decimal TotalCost { get; set; }
        public decimal UnitCost { get; set; }
        public string MerchantName { get; set; }
    }
}
