namespace FuelUpload.Domain

open System

[<CLIMutable>]
type ValidationConfig =
    { RequireExternalReference: bool
      MinFuelVolumeGallons: decimal
      MaxFuelVolumeGallons: decimal
      MinTotalCost: decimal
      MaxTotalCost: decimal
      AllowFutureTransactions: bool
      ProcessingDate: DateTimeOffset
      HighFuelVolumeWarningGallons: decimal
      HighCostPerGallonWarning: decimal
      StaleTransactionWarningDays: int }

[<RequireQualifiedAccess>]
type ValidationError =
    | MissingVehicleKey
    | MissingMerchantName
    | MissingExternalReference
    | InvalidFuelVolume of actual: decimal * minimum: decimal * maximum: decimal
    | InvalidTotalCost of actual: decimal * minimum: decimal * maximum: decimal
    | InvalidOdometer of actual: decimal
    | FutureTransaction of occurredAt: DateTimeOffset * processingDate: DateTimeOffset

[<RequireQualifiedAccess>]
type Warning =
    | HighFuelVolume of actual: decimal * threshold: decimal
    | HighCostPerGallon of actual: decimal * threshold: decimal
    | StaleTransaction of ageDays: int * thresholdDays: int

[<RequireQualifiedAccess>]
type VehicleRejectionReason =
    | UnknownVehicle
    | AmbiguousVehicle of candidates: Vehicle list

[<RequireQualifiedAccess>]
type RejectionReason =
    | ValidationFailed of ValidationError list
    | VehicleRejected of VehicleRejectionReason

module Validation =
    let private isBlank (value: string) =
        String.IsNullOrWhiteSpace value

    let validateConfig (config: ValidationConfig) =
        [ if config.MinFuelVolumeGallons < 0m then
              FatalProcessingError.InvalidValidationConfig "Minimum fuel volume cannot be negative."
          if config.MaxFuelVolumeGallons < config.MinFuelVolumeGallons then
              FatalProcessingError.InvalidValidationConfig "Maximum fuel volume cannot be lower than minimum fuel volume."
          if config.MinTotalCost < 0m then
              FatalProcessingError.InvalidValidationConfig "Minimum total cost cannot be negative."
          if config.MaxTotalCost < config.MinTotalCost then
              FatalProcessingError.InvalidValidationConfig "Maximum total cost cannot be lower than minimum total cost."
          if config.HighFuelVolumeWarningGallons < 0m then
              FatalProcessingError.InvalidValidationConfig "High fuel volume warning threshold cannot be negative."
          if config.HighCostPerGallonWarning < 0m then
              FatalProcessingError.InvalidValidationConfig "High cost per gallon warning threshold cannot be negative."
          if config.StaleTransactionWarningDays < 0 then
              FatalProcessingError.InvalidValidationConfig "Stale transaction warning days cannot be negative." ]

    let validateRow (config: ValidationConfig) (row: ParsedFuelRow) =
        [ if isBlank row.VehicleKey then
              ValidationError.MissingVehicleKey
          if isBlank row.MerchantName then
              ValidationError.MissingMerchantName
          if config.RequireExternalReference && isBlank row.ExternalReference then
              ValidationError.MissingExternalReference
          if row.FuelVolumeGallons < config.MinFuelVolumeGallons
             || row.FuelVolumeGallons > config.MaxFuelVolumeGallons then
              ValidationError.InvalidFuelVolume(
                  row.FuelVolumeGallons,
                  config.MinFuelVolumeGallons,
                  config.MaxFuelVolumeGallons
              )
          if row.TotalCost < config.MinTotalCost || row.TotalCost > config.MaxTotalCost then
              ValidationError.InvalidTotalCost(row.TotalCost, config.MinTotalCost, config.MaxTotalCost)
          if row.OdometerMiles < 0m then
              ValidationError.InvalidOdometer row.OdometerMiles
          if not config.AllowFutureTransactions && row.OccurredAt > config.ProcessingDate then
              ValidationError.FutureTransaction(row.OccurredAt, config.ProcessingDate) ]

    let warningsFor (config: ValidationConfig) (row: ParsedFuelRow) =
        [ if row.FuelVolumeGallons > config.HighFuelVolumeWarningGallons then
              Warning.HighFuelVolume(row.FuelVolumeGallons, config.HighFuelVolumeWarningGallons)
          if row.FuelVolumeGallons > 0m then
              let costPerGallon = row.TotalCost / row.FuelVolumeGallons

              if costPerGallon > config.HighCostPerGallonWarning then
                  Warning.HighCostPerGallon(costPerGallon, config.HighCostPerGallonWarning)

          let age = config.ProcessingDate.Date - row.OccurredAt.Date

          if age.TotalDays > float config.StaleTransactionWarningDays then
              Warning.StaleTransaction(int age.TotalDays, config.StaleTransactionWarningDays) ]
