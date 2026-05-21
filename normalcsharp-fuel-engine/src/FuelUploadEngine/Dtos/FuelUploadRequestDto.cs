using System.Collections.Generic;
using FuelUploadEngine.Models;

namespace FuelUploadEngine.Dtos
{
    public class FuelUploadRequestDto
    {
        public string Mode { get; set; }
        public decimal MaxQuantityLiters { get; set; }
        public decimal MaxUnitCost { get; set; }
        public List<FuelUploadRowDto> Rows { get; set; } = new List<FuelUploadRowDto>();
    }

    public class FuelUploadRowDto
    {
        public int RowNumber { get; set; }
        public FuelRow Row { get; set; }
        public string VehicleLookupStatus { get; set; }   // "Found" | "NotFound" | "Ambiguous" | "Unavailable"
        public Vehicle Vehicle { get; set; }              // populated if VehicleLookupStatus == "Found"
        public string DuplicateStatus { get; set; }       // "NotDuplicate" | "Duplicate" | "Unavailable"
        public string PreviousOutcome { get; set; }       // populated if DuplicateStatus == "Duplicate"
    }
}
