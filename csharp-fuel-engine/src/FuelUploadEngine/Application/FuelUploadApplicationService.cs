namespace FuelUploadEngine.Application;

public sealed class FuelUploadApplicationService
{
    public FuelUploadMapResult<FuelUploadResponseDto> Classify(FuelUploadRequestDto request)
    {
        var mapped = FuelUploadMapper.ToDomainRequest(request);
        if (mapped is FuelUploadMapResult<BatchClassificationRequest>.Failure failure)
        {
            return new FuelUploadMapResult<FuelUploadResponseDto>.Failure(failure.Errors);
        }

        var success = (FuelUploadMapResult<BatchClassificationRequest>.Success)mapped;
        var decision = FuelUploadDecider.ClassifyBatch(success.Value);
        return new FuelUploadMapResult<FuelUploadResponseDto>.Success(FuelUploadMapper.ToResponseDto(decision));
    }
}
