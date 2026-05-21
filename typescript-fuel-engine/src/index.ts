export type Brand<TValue, TBrand extends string> = TValue & {
  readonly __brand: TBrand;
};

export type RowId = Brand<string, "RowId">;
export type ExternalTransactionId = Brand<string, "ExternalTransactionId">;
export type VehicleKey = Brand<string, "VehicleKey">;
export type VehicleId = Brand<string, "VehicleId">;
export type CanonicalTransactionId = Brand<string, "CanonicalTransactionId">;
export type UploadAttemptId = Brand<string, "UploadAttemptId">;
export type ProcessingFailureId = Brand<string, "ProcessingFailureId">;
export type IsoInstantText = Brand<string, "IsoInstantText">;
export type MerchantName = Brand<string, "MerchantName">;

export type NonEmptyArray<T> = readonly [T, ...T[]];

export type UploadMode =
  | { readonly tag: "Normal" }
  | { readonly tag: "Retry" }
  | { readonly tag: "Recovery" };

export type FuelKind =
  | { readonly tag: "Diesel" }
  | { readonly tag: "Gasoline" }
  | { readonly tag: "Electric" }
  | { readonly tag: "Hydrogen" };

export type OdometerReading =
  | { readonly tag: "Provided"; readonly kilometres: number }
  | { readonly tag: "NotProvided" };

export type ParsedFuelRow = {
  readonly rowId: RowId;
  readonly sourceLineNumber: number;
  readonly externalTransactionId: ExternalTransactionId;
  readonly vehicleKey: VehicleKey;
  readonly purchasedAt: IsoInstantText;
  readonly merchantName: MerchantName;
  readonly fuelKind: FuelKind;
  readonly fuelVolumeLitres: number;
  readonly totalCostMinorUnits: number;
  readonly odometer: OdometerReading;
};

export type VehicleStatus =
  | { readonly tag: "Active" }
  | { readonly tag: "Inactive"; readonly reason: VehicleInactiveReason };

export type VehicleInactiveReason =
  | { readonly tag: "Retired" }
  | { readonly tag: "Suspended" };

export type Vehicle = {
  readonly vehicleId: VehicleId;
  readonly vehicleKey: VehicleKey;
  readonly status: VehicleStatus;
  readonly minimumAllowedOdometer: OdometerReading;
};

export type VehicleLookupFailure =
  | { readonly tag: "VehicleLookupUnavailable"; readonly lookupName: VehicleKey };

export type VehicleLookupResult =
  | { readonly tag: "VehicleFound"; readonly vehicle: Vehicle }
  | { readonly tag: "VehicleNotFound"; readonly vehicleKey: VehicleKey }
  | { readonly tag: "VehicleLookupFailed"; readonly error: VehicleLookupFailure };

export type PreviousDuplicateState =
  | {
      readonly tag: "CanonicalFinalized";
      readonly canonicalTransactionId: CanonicalTransactionId;
    }
  | { readonly tag: "RetryableFailure"; readonly attemptId: UploadAttemptId }
  | { readonly tag: "NonRetryableFailure"; readonly attemptId: UploadAttemptId }
  | {
      readonly tag: "FailedBeforeCanonicalFinalization";
      readonly failureId: ProcessingFailureId;
    }
  | {
      readonly tag: "FailedAfterCanonicalFinalization";
      readonly failureId: ProcessingFailureId;
    };

export type DuplicateRecord = {
  readonly externalTransactionId: ExternalTransactionId;
  readonly previousState: PreviousDuplicateState;
};

export type DuplicateCheckFailure =
  | {
      readonly tag: "DuplicateCheckUnavailable";
      readonly externalTransactionId: ExternalTransactionId;
    };

export type DuplicateCheckResult =
  | { readonly tag: "NoDuplicate" }
  | { readonly tag: "DuplicateFound"; readonly duplicate: DuplicateRecord }
  | { readonly tag: "DuplicateCheckFailed"; readonly error: DuplicateCheckFailure };

export type OdometerPolicy =
  | { readonly tag: "OdometerRequired" }
  | { readonly tag: "OdometerOptional" };

export type NumericRange = {
  readonly minimumInclusive: number;
  readonly maximumInclusive: number;
};

export type WarningLimit =
  | { readonly tag: "Enabled"; readonly thresholdExclusive: number }
  | { readonly tag: "Disabled" };

export type ValidationConfig = {
  readonly allowedFuelKinds: NonEmptyArray<FuelKind>;
  readonly fuelVolumeLitres: NumericRange;
  readonly totalCostMinorUnits: NumericRange;
  readonly odometerPolicy: OdometerPolicy;
  readonly warningLimits: {
    readonly fuelVolumeLitres: WarningLimit;
    readonly totalCostMinorUnits: WarningLimit;
  };
};

export type ValidationError =
  | {
      readonly tag: "FuelKindNotAllowed";
      readonly fuelKind: FuelKind;
      readonly allowedFuelKinds: NonEmptyArray<FuelKind>;
    }
  | {
      readonly tag: "FuelVolumeOutOfRange";
      readonly actualLitres: number;
      readonly allowedRange: NumericRange;
    }
  | {
      readonly tag: "TotalCostOutOfRange";
      readonly actualMinorUnits: number;
      readonly allowedRange: NumericRange;
    }
  | { readonly tag: "VehicleMissing"; readonly vehicleKey: VehicleKey }
  | { readonly tag: "VehicleInactive"; readonly vehicleId: VehicleId; readonly reason: VehicleInactiveReason }
  | { readonly tag: "OdometerMissing"; readonly policy: { readonly tag: "OdometerRequired" } }
  | {
      readonly tag: "OdometerBelowVehicleMinimum";
      readonly actualKilometres: number;
      readonly minimumKilometres: number;
    };

export type RejectionReason =
  | { readonly tag: "ValidationFailed"; readonly errors: NonEmptyArray<ValidationError> };

export type UploadWarning =
  | {
      readonly tag: "FuelVolumeAboveWarningLimit";
      readonly actualLitres: number;
      readonly thresholdExclusive: number;
    }
  | {
      readonly tag: "TotalCostAboveWarningLimit";
      readonly actualMinorUnits: number;
      readonly thresholdExclusive: number;
    }
  | { readonly tag: "OdometerMissingButAllowed" };

export type FatalProcessingError =
  | {
      readonly tag: "InvalidValidationConfig";
      readonly problems: NonEmptyArray<ValidationConfigProblem>;
    }
  | { readonly tag: "VehicleLookupFatal"; readonly error: VehicleLookupFailure }
  | { readonly tag: "DuplicateCheckFatal"; readonly error: DuplicateCheckFailure };

export type ValidationConfigProblem =
  | { readonly tag: "FuelVolumeRangeInvalid"; readonly range: NumericRange }
  | { readonly tag: "TotalCostRangeInvalid"; readonly range: NumericRange };

export type FuelTransaction = {
  readonly externalTransactionId: ExternalTransactionId;
  readonly vehicleId: VehicleId;
  readonly purchasedAt: IsoInstantText;
  readonly merchantName: MerchantName;
  readonly fuelKind: FuelKind;
  readonly fuelVolumeLitres: number;
  readonly totalCostMinorUnits: number;
  readonly odometer: OdometerReading;
};

export type DuplicateSkipReason =
  | { readonly tag: "DuplicateInNormalMode" }
  | { readonly tag: "DuplicateNotRetryable" }
  | { readonly tag: "DuplicateNotRecoverable" };

export type RowDecision =
  | {
      readonly tag: "AcceptedTransaction";
      readonly rowId: RowId;
      readonly transaction: FuelTransaction;
    }
  | {
      readonly tag: "SkippedDuplicate";
      readonly rowId: RowId;
      readonly mode: UploadMode;
      readonly duplicate: DuplicateRecord;
      readonly reason: DuplicateSkipReason;
    }
  | {
      readonly tag: "RejectedRow";
      readonly rowId: RowId;
      readonly reasons: NonEmptyArray<RejectionReason>;
    }
  | {
      readonly tag: "WarningWithTransaction";
      readonly rowId: RowId;
      readonly transaction: FuelTransaction;
      readonly warnings: NonEmptyArray<UploadWarning>;
    }
  | {
      readonly tag: "FatalProcessingError";
      readonly rowId: RowId;
      readonly error: FatalProcessingError;
    };

export type RowClassificationInput = {
  readonly row: ParsedFuelRow;
  readonly vehicleLookup: VehicleLookupResult;
  readonly duplicateCheck: DuplicateCheckResult;
};

export type BatchClassificationInput = {
  readonly mode: UploadMode;
  readonly config: ValidationConfig;
  readonly rows: readonly RowClassificationInput[];
};

export type BatchSummary = {
  readonly totalRows: number;
  readonly acceptedRows: number;
  readonly warningRows: number;
  readonly skippedDuplicateRows: number;
  readonly rejectedRows: number;
  readonly fatalRows: number;
  readonly transactionRows: number;
};

export type BatchDecision =
  | {
      readonly tag: "BatchUploadable";
      readonly rowDecisions: readonly RowDecision[];
      readonly summary: BatchSummary;
      readonly uploadableTransactions: readonly FuelTransaction[];
    }
  | {
      readonly tag: "BatchBlockedByFatalError";
      readonly rowDecisions: readonly RowDecision[];
      readonly summary: BatchSummary;
      readonly fatalErrors: NonEmptyArray<FatalProcessingError>;
      readonly uploadableTransactions: readonly [];
    };

type DuplicatePolicyDecision =
  | { readonly tag: "DuplicateAllowed" }
  | {
      readonly tag: "DuplicateSkipped";
      readonly duplicate: DuplicateRecord;
      readonly reason: DuplicateSkipReason;
    };

export function classifyRow(
  input: RowClassificationInput,
  mode: UploadMode,
  config: ValidationConfig,
): RowDecision {
  const configProblems = validateConfig(config);
  if (configProblems.length > 0) {
    return {
      tag: "FatalProcessingError",
      rowId: input.row.rowId,
      error: {
        tag: "InvalidValidationConfig",
        problems: toNonEmpty(configProblems),
      },
    };
  }

  if (input.vehicleLookup.tag === "VehicleLookupFailed") {
    return {
      tag: "FatalProcessingError",
      rowId: input.row.rowId,
      error: { tag: "VehicleLookupFatal", error: input.vehicleLookup.error },
    };
  }

  if (input.duplicateCheck.tag === "DuplicateCheckFailed") {
    return {
      tag: "FatalProcessingError",
      rowId: input.row.rowId,
      error: { tag: "DuplicateCheckFatal", error: input.duplicateCheck.error },
    };
  }

  const validationErrors = validateRow(input.row, input.vehicleLookup, config);
  if (validationErrors.length > 0) {
    return {
      tag: "RejectedRow",
      rowId: input.row.rowId,
      reasons: [{ tag: "ValidationFailed", errors: toNonEmpty(validationErrors) }],
    };
  }

  const duplicateDecision = classifyDuplicate(input.duplicateCheck, mode);
  if (duplicateDecision.tag === "DuplicateSkipped") {
    return {
      tag: "SkippedDuplicate",
      rowId: input.row.rowId,
      mode,
      duplicate: duplicateDecision.duplicate,
      reason: duplicateDecision.reason,
    };
  }

  if (input.vehicleLookup.tag !== "VehicleFound") {
    return {
      tag: "RejectedRow",
      rowId: input.row.rowId,
      reasons: [
        {
          tag: "ValidationFailed",
          errors: [{ tag: "VehicleMissing", vehicleKey: input.vehicleLookup.vehicleKey }],
        },
      ],
    };
  }

  const transaction = buildTransaction(input.row, input.vehicleLookup.vehicle);
  const warnings = collectWarnings(input.row, config);

  if (warnings.length > 0) {
    return {
      tag: "WarningWithTransaction",
      rowId: input.row.rowId,
      transaction,
      warnings: toNonEmpty(warnings),
    };
  }

  return {
    tag: "AcceptedTransaction",
    rowId: input.row.rowId,
    transaction,
  };
}

export function classifyBatch(input: BatchClassificationInput): BatchDecision {
  const rowDecisions = input.rows.map((rowInput) => classifyRow(rowInput, input.mode, input.config));
  const summary = summarize(rowDecisions);
  const fatalErrors = rowDecisions.flatMap((decision) =>
    decision.tag === "FatalProcessingError" ? [decision.error] : [],
  );

  if (fatalErrors.length > 0) {
    return {
      tag: "BatchBlockedByFatalError",
      rowDecisions,
      summary,
      fatalErrors: toNonEmpty(fatalErrors),
      uploadableTransactions: [],
    };
  }

  return {
    tag: "BatchUploadable",
    rowDecisions,
    summary,
    uploadableTransactions: collectTransactions(rowDecisions),
  };
}

function validateConfig(config: ValidationConfig): readonly ValidationConfigProblem[] {
  const problems: ValidationConfigProblem[] = [];

  if (config.fuelVolumeLitres.minimumInclusive > config.fuelVolumeLitres.maximumInclusive) {
    problems.push({ tag: "FuelVolumeRangeInvalid", range: config.fuelVolumeLitres });
  }

  if (config.totalCostMinorUnits.minimumInclusive > config.totalCostMinorUnits.maximumInclusive) {
    problems.push({ tag: "TotalCostRangeInvalid", range: config.totalCostMinorUnits });
  }

  return problems;
}

function validateRow(
  row: ParsedFuelRow,
  vehicleLookup: Exclude<VehicleLookupResult, { readonly tag: "VehicleLookupFailed" }>,
  config: ValidationConfig,
): readonly ValidationError[] {
  const errors: ValidationError[] = [];

  if (!isFuelKindAllowed(row.fuelKind, config.allowedFuelKinds)) {
    errors.push({
      tag: "FuelKindNotAllowed",
      fuelKind: row.fuelKind,
      allowedFuelKinds: config.allowedFuelKinds,
    });
  }

  if (!isWithin(row.fuelVolumeLitres, config.fuelVolumeLitres)) {
    errors.push({
      tag: "FuelVolumeOutOfRange",
      actualLitres: row.fuelVolumeLitres,
      allowedRange: config.fuelVolumeLitres,
    });
  }

  if (!isWithin(row.totalCostMinorUnits, config.totalCostMinorUnits)) {
    errors.push({
      tag: "TotalCostOutOfRange",
      actualMinorUnits: row.totalCostMinorUnits,
      allowedRange: config.totalCostMinorUnits,
    });
  }

  if (vehicleLookup.tag === "VehicleNotFound") {
    errors.push({ tag: "VehicleMissing", vehicleKey: vehicleLookup.vehicleKey });
    return errors;
  }

  switch (vehicleLookup.vehicle.status.tag) {
    case "Active":
      break;
    case "Inactive":
      errors.push({
        tag: "VehicleInactive",
        vehicleId: vehicleLookup.vehicle.vehicleId,
        reason: vehicleLookup.vehicle.status.reason,
      });
      break;
    default:
      assertNever(vehicleLookup.vehicle.status);
  }

  switch (config.odometerPolicy.tag) {
    case "OdometerRequired":
      if (row.odometer.tag === "NotProvided") {
        errors.push({ tag: "OdometerMissing", policy: config.odometerPolicy });
      }
      break;
    case "OdometerOptional":
      break;
    default:
      assertNever(config.odometerPolicy);
  }

  if (
    row.odometer.tag === "Provided" &&
    vehicleLookup.vehicle.minimumAllowedOdometer.tag === "Provided" &&
    row.odometer.kilometres < vehicleLookup.vehicle.minimumAllowedOdometer.kilometres
  ) {
    errors.push({
      tag: "OdometerBelowVehicleMinimum",
      actualKilometres: row.odometer.kilometres,
      minimumKilometres: vehicleLookup.vehicle.minimumAllowedOdometer.kilometres,
    });
  }

  return errors;
}

function classifyDuplicate(
  check: Exclude<DuplicateCheckResult, { readonly tag: "DuplicateCheckFailed" }>,
  mode: UploadMode,
): DuplicatePolicyDecision {
  switch (check.tag) {
    case "NoDuplicate":
      return { tag: "DuplicateAllowed" };
    case "DuplicateFound":
      return classifyDuplicateFound(check.duplicate, mode);
    default:
      return assertNever(check);
  }
}

function classifyDuplicateFound(duplicate: DuplicateRecord, mode: UploadMode): DuplicatePolicyDecision {
  switch (mode.tag) {
    case "Normal":
      return {
        tag: "DuplicateSkipped",
        duplicate,
        reason: { tag: "DuplicateInNormalMode" },
      };
    case "Retry":
      return duplicate.previousState.tag === "RetryableFailure"
        ? { tag: "DuplicateAllowed" }
        : {
            tag: "DuplicateSkipped",
            duplicate,
            reason: { tag: "DuplicateNotRetryable" },
          };
    case "Recovery":
      return duplicate.previousState.tag === "FailedBeforeCanonicalFinalization"
        ? { tag: "DuplicateAllowed" }
        : {
            tag: "DuplicateSkipped",
            duplicate,
            reason: { tag: "DuplicateNotRecoverable" },
          };
    default:
      return assertNever(mode);
  }
}

function buildTransaction(row: ParsedFuelRow, vehicle: Vehicle): FuelTransaction {
  return {
    externalTransactionId: row.externalTransactionId,
    vehicleId: vehicle.vehicleId,
    purchasedAt: row.purchasedAt,
    merchantName: row.merchantName,
    fuelKind: row.fuelKind,
    fuelVolumeLitres: row.fuelVolumeLitres,
    totalCostMinorUnits: row.totalCostMinorUnits,
    odometer: row.odometer,
  };
}

function collectWarnings(row: ParsedFuelRow, config: ValidationConfig): readonly UploadWarning[] {
  const warnings: UploadWarning[] = [];

  switch (config.warningLimits.fuelVolumeLitres.tag) {
    case "Enabled":
      if (row.fuelVolumeLitres > config.warningLimits.fuelVolumeLitres.thresholdExclusive) {
        warnings.push({
          tag: "FuelVolumeAboveWarningLimit",
          actualLitres: row.fuelVolumeLitres,
          thresholdExclusive: config.warningLimits.fuelVolumeLitres.thresholdExclusive,
        });
      }
      break;
    case "Disabled":
      break;
    default:
      assertNever(config.warningLimits.fuelVolumeLitres);
  }

  switch (config.warningLimits.totalCostMinorUnits.tag) {
    case "Enabled":
      if (row.totalCostMinorUnits > config.warningLimits.totalCostMinorUnits.thresholdExclusive) {
        warnings.push({
          tag: "TotalCostAboveWarningLimit",
          actualMinorUnits: row.totalCostMinorUnits,
          thresholdExclusive: config.warningLimits.totalCostMinorUnits.thresholdExclusive,
        });
      }
      break;
    case "Disabled":
      break;
    default:
      assertNever(config.warningLimits.totalCostMinorUnits);
  }

  if (config.odometerPolicy.tag === "OdometerOptional" && row.odometer.tag === "NotProvided") {
    warnings.push({ tag: "OdometerMissingButAllowed" });
  }

  return warnings;
}

function summarize(rowDecisions: readonly RowDecision[]): BatchSummary {
  return rowDecisions.reduce<BatchSummary>(
    (summary, decision) => {
      switch (decision.tag) {
        case "AcceptedTransaction":
          return {
            ...summary,
            acceptedRows: summary.acceptedRows + 1,
            transactionRows: summary.transactionRows + 1,
          };
        case "WarningWithTransaction":
          return {
            ...summary,
            warningRows: summary.warningRows + 1,
            transactionRows: summary.transactionRows + 1,
          };
        case "SkippedDuplicate":
          return { ...summary, skippedDuplicateRows: summary.skippedDuplicateRows + 1 };
        case "RejectedRow":
          return { ...summary, rejectedRows: summary.rejectedRows + 1 };
        case "FatalProcessingError":
          return { ...summary, fatalRows: summary.fatalRows + 1 };
        default:
          return assertNever(decision);
      }
    },
    {
      totalRows: rowDecisions.length,
      acceptedRows: 0,
      warningRows: 0,
      skippedDuplicateRows: 0,
      rejectedRows: 0,
      fatalRows: 0,
      transactionRows: 0,
    },
  );
}

function collectTransactions(rowDecisions: readonly RowDecision[]): readonly FuelTransaction[] {
  return rowDecisions.flatMap((decision) => {
    switch (decision.tag) {
      case "AcceptedTransaction":
      case "WarningWithTransaction":
        return [decision.transaction];
      case "SkippedDuplicate":
      case "RejectedRow":
      case "FatalProcessingError":
        return [];
      default:
        return assertNever(decision);
    }
  });
}

function isFuelKindAllowed(fuelKind: FuelKind, allowedFuelKinds: NonEmptyArray<FuelKind>): boolean {
  return allowedFuelKinds.some((allowed) => allowed.tag === fuelKind.tag);
}

function isWithin(value: number, range: NumericRange): boolean {
  return value >= range.minimumInclusive && value <= range.maximumInclusive;
}

function toNonEmpty<T>(items: readonly T[]): NonEmptyArray<T> {
  if (items.length === 0) {
    throw new Error("Expected a non-empty array.");
  }

  return items as NonEmptyArray<T>;
}

export function assertNever(value: never): never {
  throw new Error(`Unhandled union member: ${JSON.stringify(value)}`);
}
