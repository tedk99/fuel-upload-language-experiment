using System.Collections.Generic;
using FuelUploadEngine.Models;

namespace FuelUploadEngine.Dtos
{
    public class FuelUploadResponseDto
    {
        public List<RowDecision> Decisions { get; set; }
        public BatchSummary Summary { get; set; }
    }
}
