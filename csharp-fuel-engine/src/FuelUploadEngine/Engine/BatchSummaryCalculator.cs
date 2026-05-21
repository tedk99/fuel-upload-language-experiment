namespace FuelUploadEngine;

public static class BatchSummaryCalculator
{
    public static BatchSummary Summarize(IReadOnlyCollection<RowDecision> decisions)
    {
        var acceptedWithoutWarnings = decisions.OfType<RowDecision.AcceptedTransaction>().Count();
        var acceptedWithWarnings = decisions.OfType<RowDecision.AcceptedTransactionWithWarnings>().ToArray();
        var quarantined = decisions.OfType<RowDecision.QuarantinedRow>().ToArray();
        var acceptedTransactions = acceptedWithoutWarnings + acceptedWithWarnings.Length;
        var fatalRows = decisions.OfType<RowDecision.FatalProcessingError>().Count();

        return new BatchSummary(
            TotalRows: decisions.Count,
            AcceptedTransactions: acceptedTransactions,
            AcceptedWithoutWarnings: acceptedWithoutWarnings,
            AcceptedWithWarnings: acceptedWithWarnings.Length,
            QuarantinedRows: quarantined.Length,
            SkippedDuplicates: decisions.OfType<RowDecision.SkippedDuplicate>().Count(),
            RejectedRows: decisions.OfType<RowDecision.RejectedRow>().Count(),
            FatalRows: fatalRows,
            WarningCount: acceptedWithWarnings.Sum(decision => decision.Warnings.Count)
                + quarantined.Sum(decision => decision.Warnings.Count),
            UploadableTransactions: fatalRows == 0 ? acceptedTransactions : 0);
    }
}
