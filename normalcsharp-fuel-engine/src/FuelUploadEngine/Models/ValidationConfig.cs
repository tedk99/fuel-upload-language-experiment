namespace FuelUploadEngine.Models
{
    public class ValidationConfig
    {
        public decimal MinQuantityLiters { get; set; } = 0m;
        public decimal MaxQuantityLiters { get; set; } = 500m;
        public decimal QuantityWarnThreshold { get; set; } = 300m;
        public decimal MaxUnitCost { get; set; } = 10m;
        public decimal UnitCostWarnThreshold { get; set; } = 5m;
    }
}
