namespace FuelUploadEngine.Models
{
    public class BatchSummary
    {
        public int TotalRows { get; set; }
        public int Accepted { get; set; }
        public int AcceptedWithWarnings { get; set; }
        public int Quarantined { get; set; }
        public int Skipped { get; set; }
        public int Rejected { get; set; }
        public int Fatal { get; set; }
    }
}
