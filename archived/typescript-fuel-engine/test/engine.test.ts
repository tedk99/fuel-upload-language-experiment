import { describe, expect, it } from "vitest";
import {
  classifyBatch,
  classifyRow,
  type BatchClassificationInput,
  type CanonicalTransactionId,
  type DuplicateCheckResult,
  type ExternalTransactionId,
  type FuelKind,
  type IsoInstantText,
  type MerchantName,
  type ParsedFuelRow,
  type ProcessingFailureId,
  type PreviousDuplicateState,
  type RowId,
  type UploadAttemptId,
  type UploadMode,
  type ValidationConfig,
  type Vehicle,
  type VehicleId,
  type VehicleKey,
  type VehicleLookupResult,
} from "../src/index.js";

const diesel: FuelKind = { tag: "Diesel" };
const gasoline: FuelKind = { tag: "Gasoline" };
const normal: UploadMode = { tag: "Normal" };
const retry: UploadMode = { tag: "Retry" };
const recovery: UploadMode = { tag: "Recovery" };

const config: ValidationConfig = {
  allowedFuelKinds: [diesel, gasoline],
  fuelVolumeLitres: { minimumInclusive: 1, maximumInclusive: 500 },
  totalCostMinorUnits: { minimumInclusive: 1, maximumInclusive: 200_000 },
  odometerPolicy: { tag: "OdometerRequired" },
  warningLimits: {
    fuelVolumeLitres: { tag: "Enabled", thresholdExclusive: 250 },
    totalCostMinorUnits: { tag: "Enabled", thresholdExclusive: 100_000 },
  },
};

const vehicle: Vehicle = {
  vehicleId: "vehicle-1" as VehicleId,
  vehicleKey: "fleet-1" as VehicleKey,
  status: { tag: "Active" },
  minimumAllowedOdometer: { tag: "Provided", kilometres: 10_000 },
};

const foundVehicle: VehicleLookupResult = { tag: "VehicleFound", vehicle };
const noDuplicate: DuplicateCheckResult = { tag: "NoDuplicate" };

function row(overrides: Partial<ParsedFuelRow> = {}): ParsedFuelRow {
  return {
    rowId: "row-1" as RowId,
    sourceLineNumber: 2,
    externalTransactionId: "external-1" as ExternalTransactionId,
    vehicleKey: "fleet-1" as VehicleKey,
    purchasedAt: "2026-05-21T09:00:00Z" as IsoInstantText,
    merchantName: "Depot A" as MerchantName,
    fuelKind: diesel,
    fuelVolumeLitres: 100,
    totalCostMinorUnits: 50_000,
    odometer: { tag: "Provided", kilometres: 12_000 },
    ...overrides,
  };
}

function duplicate(previousState: PreviousDuplicateState): DuplicateCheckResult {
  return {
    tag: "DuplicateFound",
    duplicate: {
      externalTransactionId: "external-1" as ExternalTransactionId,
      previousState,
    },
  };
}

describe("classifyRow", () => {
  it("accepts a valid non-duplicate row without warnings", () => {
    const decision = classifyRow({ row: row(), vehicleLookup: foundVehicle, duplicateCheck: noDuplicate }, normal, config);

    expect(decision.tag).toBe("AcceptedTransaction");
    if (decision.tag === "AcceptedTransaction") {
      expect(decision.transaction.vehicleId).toBe(vehicle.vehicleId);
      expect(decision.transaction.externalTransactionId).toBe("external-1");
    }
  });

  it("rejects validation errors and never returns a transaction", () => {
    const decision = classifyRow(
      {
        row: row({ fuelVolumeLitres: 0 }),
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      normal,
      config,
    );

    expect(decision.tag).toBe("RejectedRow");
    if (decision.tag === "RejectedRow") {
      expect(decision.reasons).toHaveLength(1);
      expect(decision.reasons[0].tag).toBe("ValidationFailed");
      expect(decision.reasons[0].errors[0].tag).toBe("FuelVolumeOutOfRange");
    }
  });

  it("skips duplicate rows in normal mode", () => {
    const decision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate({
          tag: "CanonicalFinalized",
          canonicalTransactionId: "canonical-1" as CanonicalTransactionId,
        }),
      },
      normal,
      config,
    );

    expect(decision).toMatchObject({
      tag: "SkippedDuplicate",
      reason: { tag: "DuplicateInNormalMode" },
    });
  });

  it("skips retry duplicates unless the previous attempt is explicitly retryable", () => {
    const retryableDecision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate({ tag: "RetryableFailure", attemptId: "attempt-1" as UploadAttemptId }),
      },
      retry,
      config,
    );

    const nonRetryableDecision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate({ tag: "NonRetryableFailure", attemptId: "attempt-2" as UploadAttemptId }),
      },
      retry,
      config,
    );

    expect(retryableDecision.tag).toBe("AcceptedTransaction");
    expect(nonRetryableDecision).toMatchObject({
      tag: "SkippedDuplicate",
      reason: { tag: "DuplicateNotRetryable" },
    });
  });

  it("accepts recovery duplicates only when the prior failure happened before canonical finalization", () => {
    const recoverableDecision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate({
          tag: "FailedBeforeCanonicalFinalization",
          failureId: "failure-1" as ProcessingFailureId,
        }),
      },
      recovery,
      config,
    );

    const finalizedDecision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: duplicate({
          tag: "FailedAfterCanonicalFinalization",
          failureId: "failure-2" as ProcessingFailureId,
        }),
      },
      recovery,
      config,
    );

    expect(recoverableDecision.tag).toBe("AcceptedTransaction");
    expect(finalizedDecision).toMatchObject({
      tag: "SkippedDuplicate",
      reason: { tag: "DuplicateNotRecoverable" },
    });
  });

  it("returns warnings with a transaction and does not block upload", () => {
    const optionalConfig: ValidationConfig = {
      ...config,
      odometerPolicy: { tag: "OdometerOptional" },
    };

    const decision = classifyRow(
      {
        row: row({ fuelVolumeLitres: 300, totalCostMinorUnits: 120_000, odometer: { tag: "NotProvided" } }),
        vehicleLookup: foundVehicle,
        duplicateCheck: noDuplicate,
      },
      normal,
      optionalConfig,
    );

    expect(decision.tag).toBe("WarningWithTransaction");
    if (decision.tag === "WarningWithTransaction") {
      expect(decision.transaction.externalTransactionId).toBe("external-1");
      expect(decision.warnings.map((warning) => warning.tag)).toEqual([
        "FuelVolumeAboveWarningLimit",
        "TotalCostAboveWarningLimit",
        "OdometerMissingButAllowed",
      ]);
    }
  });

  it("returns fatal errors for unavailable processing dependencies", () => {
    const decision = classifyRow(
      {
        row: row(),
        vehicleLookup: foundVehicle,
        duplicateCheck: {
          tag: "DuplicateCheckFailed",
          error: { tag: "DuplicateCheckUnavailable", externalTransactionId: "external-1" as ExternalTransactionId },
        },
      },
      normal,
      config,
    );

    expect(decision).toMatchObject({
      tag: "FatalProcessingError",
      error: { tag: "DuplicateCheckFatal" },
    });
  });
});

describe("classifyBatch", () => {
  it("derives summary counts from row decisions", () => {
    const optionalConfig: ValidationConfig = {
      ...config,
      odometerPolicy: { tag: "OdometerOptional" },
    };
    const input: BatchClassificationInput = {
      mode: normal,
      config: optionalConfig,
      rows: [
        { row: row({ rowId: "row-accepted" as RowId }), vehicleLookup: foundVehicle, duplicateCheck: noDuplicate },
        {
          row: row({ rowId: "row-warning" as RowId, odometer: { tag: "NotProvided" } }),
          vehicleLookup: foundVehicle,
          duplicateCheck: noDuplicate,
        },
        {
          row: row({ rowId: "row-rejected" as RowId, fuelVolumeLitres: 0 }),
          vehicleLookup: foundVehicle,
          duplicateCheck: noDuplicate,
        },
        {
          row: row({ rowId: "row-skipped" as RowId }),
          vehicleLookup: foundVehicle,
          duplicateCheck: duplicate({
            tag: "CanonicalFinalized",
            canonicalTransactionId: "canonical-1" as CanonicalTransactionId,
          }),
        },
      ],
    };

    const decision = classifyBatch(input);

    expect(decision.tag).toBe("BatchUploadable");
    expect(decision.summary).toEqual({
      totalRows: 4,
      acceptedRows: 1,
      warningRows: 1,
      skippedDuplicateRows: 1,
      rejectedRows: 1,
      fatalRows: 0,
      transactionRows: 2,
    });
    expect(decision.uploadableTransactions).toHaveLength(2);
  });

  it("blocks the entire batch when any row has a fatal processing error", () => {
    const decision = classifyBatch({
      mode: normal,
      config,
      rows: [
        { row: row({ rowId: "row-accepted" as RowId }), vehicleLookup: foundVehicle, duplicateCheck: noDuplicate },
        {
          row: row({ rowId: "row-fatal" as RowId }),
          vehicleLookup: { tag: "VehicleLookupFailed", error: { tag: "VehicleLookupUnavailable", lookupName: "fleet-1" as VehicleKey } },
          duplicateCheck: noDuplicate,
        },
      ],
    });

    expect(decision.tag).toBe("BatchBlockedByFatalError");
    expect(decision.summary).toMatchObject({
      totalRows: 2,
      acceptedRows: 1,
      fatalRows: 1,
      transactionRows: 1,
    });
    expect(decision.uploadableTransactions).toEqual([]);
    if (decision.tag === "BatchBlockedByFatalError") {
      expect(decision.fatalErrors[0].tag).toBe("VehicleLookupFatal");
    }
  });
});
