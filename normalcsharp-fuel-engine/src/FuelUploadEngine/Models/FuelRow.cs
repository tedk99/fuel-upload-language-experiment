using System;

namespace FuelUploadEngine.Models
{
    public class FuelRow
    {
        public int RowNumber { get; set; }
        public string VehicleRef { get; set; }
        public string SourceId { get; set; }
        public DateTime OccurredOn { get; set; }
        public decimal QuantityLiters { get; set; }
        public decimal TotalCost { get; set; }
        public string MerchantName { get; set; }
        public int Odometer { get; set; }
    }
}
