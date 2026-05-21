module FuelUpload.Summary
  ( summarizeRows
  , outcomeFromRows
  ) where

import Data.List.NonEmpty (NonEmpty (..))
import FuelUpload.Domain.Decision
import FuelUpload.Domain.Primitive

summarizeRows :: [RowDecision] -> BatchSummary
summarizeRows =
  foldr addDecision emptySummary
  where
    emptySummary =
      BatchSummary
        { summaryAccepted = 0
        , summaryAcceptedWithWarnings = 0
        , summarySkippedDuplicates = 0
        , summaryRejected = 0
        , summaryFatal = 0
        , summaryTotalRows = 0
        }

    addDecision decision summary =
      case decision of
        Accepted _ ->
          countAccepted summary
        AcceptedWithWarnings _ _ ->
          countAcceptedWithWarnings summary
        SkippedDuplicate _ ->
          countSkipped summary
        Rejected _ ->
          countRejected summary
        Fatal _ ->
          countFatal summary

    countAccepted summary =
      bumpTotal summary
        { summaryAccepted = summaryAccepted summary + 1
        }

    countAcceptedWithWarnings summary =
      bumpTotal summary
        { summaryAccepted = summaryAccepted summary + 1
        , summaryAcceptedWithWarnings = summaryAcceptedWithWarnings summary + 1
        }

    countSkipped summary =
      bumpTotal summary
        { summarySkippedDuplicates = summarySkippedDuplicates summary + 1
        }

    countRejected summary =
      bumpTotal summary
        { summaryRejected = summaryRejected summary + 1
        }

    countFatal summary =
      bumpTotal summary
        { summaryFatal = summaryFatal summary + 1
        }

    bumpTotal summary =
      summary {summaryTotalRows = summaryTotalRows summary + 1}

outcomeFromRows :: [RowDecision] -> BatchOutcome
outcomeFromRows rows =
  case fatalErrors rows of
    firstFatal : remainingFatals ->
      BatchBlockedByFatal (firstFatal :| remainingFatals)
    [] ->
      BatchUploadable

fatalErrors :: [RowDecision] -> [FatalError]
fatalErrors =
  foldr collectFatal []
  where
    collectFatal decision errors =
      case decision of
        Fatal fatalError -> fatalError : errors
        Accepted _ -> errors
        AcceptedWithWarnings _ _ -> errors
        SkippedDuplicate _ -> errors
        Rejected _ -> errors
