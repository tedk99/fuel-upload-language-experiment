using System.Collections.Generic;

namespace FuelUploadEngine.Models
{
    // One flat class for all possible outcomes. The "Status" string says
    // which kind it is; the other fields are populated or left null depending.
    public class RowDecision
    {
        public int RowNumber;
        public string Status;                     // "Accepted" | "AcceptedWithWarnings" | "Quarantined" | "Skipped" | "Rejected" | "Fatal"
        public FuelTransaction Transaction;       // null when Status is Rejected / Fatal / Skipped
        public Vehicle Vehicle;                   // null when vehicle lookup failed
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> QuarantineReasons = new List<string>();
        public string SkipReason;                 // populated when Status == "Skipped"
        public string FatalMessage;               // populated when Status == "Fatal"
    }
}
