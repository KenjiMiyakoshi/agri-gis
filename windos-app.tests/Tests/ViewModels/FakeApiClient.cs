using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// B505 (WB5): IApiClient の手書き Fake (Moq 不採用、依存最小化のため)。
// 各メソッドのコールカウントと結果を制御。
// E'201 (WE'2): asOf 引数追加 + BatchUpdateFeaturesAsync stub 追加。
public sealed class FakeApiClient : IApiClient
{
    public int CreateLayerCalls;
    public int StartImportJobCalls;
    public int BulkInsertCalls;
    public int FinalizeCalls;
    public string? LastFinalizeStatus;

    public Func<CreateLayerRequestDto, LayerAdminDto>? CreateLayerImpl;
    public Func<int, StartImportJobRequestDto, ImportJobDto>? StartImportJobImpl;
    public Func<int, BulkFeaturesRequestDto, BulkFeaturesResponseDto>? BulkInsertImpl;
    public Func<Guid, FinalizeImportJobRequestDto, ImportJobDto>? FinalizeImpl;

    public Task<LoginResponseDto> LoginAsync(string loginId, string password, CancellationToken ct)
        => throw new NotImplementedException();
    public Task<IReadOnlyList<LayerDto>> GetLayersAsync(DateOnly? asOf, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LayerDto>>(Array.Empty<LayerDto>());
    public Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, DateOnly? asOf, CancellationToken ct)
        => throw new NotImplementedException();
    public Task<FeatureDto> GetFeatureAsync(Guid entityId, DateOnly? asOf, CancellationToken ct)
        => throw new NotImplementedException();
    public Task<CreateFeatureResultDto> CreateFeatureAsync(CreateFeatureRequestDto req, CancellationToken ct)
        => throw new NotImplementedException();
    public Task<PatchFeatureResultDto> UpdateFeatureAsync(Guid entityId, UpdateFeatureRequestDto req, int ifMatchVersion, CancellationToken ct)
        => throw new NotImplementedException();
    public Task DeleteFeatureAsync(Guid entityId, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<LayerAdminDto>> ListLayersAdminAsync(bool includeDeleted, DateOnly? asOf, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LayerAdminDto>>(Array.Empty<LayerAdminDto>());

    public Task<LayerAdminDto> CreateLayerAsync(CreateLayerRequestDto req, CancellationToken ct)
    {
        CreateLayerCalls++;
        var dto = CreateLayerImpl?.Invoke(req) ?? new LayerAdminDto(
            LayerId: 100, LayerName: req.LayerName, LayerType: req.LayerType,
            GeometryType: req.GeometryType, SourceFormat: req.SourceFormat,
            SourceSrid: req.SourceSrid, Description: req.Description,
            SchemaVersion: 1, Schema: req.Schema ?? new LayerSchemaDto(Array.Empty<SchemaFieldDto>()),
            CreatedBy: null, CreatedOrgId: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        return Task.FromResult(dto);
    }

    public Task<LayerAdminDto> UpdateLayerAsync(int layerId, UpdateLayerRequestDto req, CancellationToken ct)
        => throw new NotImplementedException();
    public Task DeleteLayerAsync(int layerId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<ImportJobDto> StartImportJobAsync(int layerId, StartImportJobRequestDto req, CancellationToken ct)
    {
        StartImportJobCalls++;
        var dto = StartImportJobImpl?.Invoke(layerId, req) ?? new ImportJobDto(
            JobId: Guid.NewGuid(), LayerId: layerId, Status: "running",
            TotalCount: req.TotalCount, InsertedCount: 0,
            StartedAt: DateTimeOffset.UtcNow, FinishedAt: null, ErrorText: null);
        return Task.FromResult(dto);
    }

    public Task<ImportJobDto> GetImportJobAsync(Guid jobId, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<ImportJobDto> FinalizeImportJobAsync(Guid jobId, FinalizeImportJobRequestDto req, CancellationToken ct)
    {
        FinalizeCalls++;
        LastFinalizeStatus = req.Status;
        var dto = FinalizeImpl?.Invoke(jobId, req) ?? new ImportJobDto(
            JobId: jobId, LayerId: 100, Status: req.Status,
            TotalCount: null, InsertedCount: 0,
            StartedAt: DateTimeOffset.UtcNow, FinishedAt: DateTimeOffset.UtcNow,
            ErrorText: req.ErrorText);
        return Task.FromResult(dto);
    }

    public Task<BulkFeaturesResponseDto> BulkInsertFeaturesAsync(int layerId, BulkFeaturesRequestDto req, CancellationToken ct)
    {
        BulkInsertCalls++;
        var res = BulkInsertImpl?.Invoke(layerId, req) ?? new BulkFeaturesResponseDto(
            InsertedCount: req.Features.Count,
            FeatureIds: req.Features.Select((_, i) => (long)(BulkInsertCalls * 1000 + i)).ToList());
        return Task.FromResult(res);
    }

    public Task<CreateSelectionResponseDto> CreateSelectionAsync(
        IReadOnlyList<Guid> entityIds, string? colorHex, CancellationToken ct)
        => Task.FromResult(new CreateSelectionResponseDto(Guid.NewGuid(), "session", entityIds.Count));

    public Task DeleteSelectionAsync(Guid sid, CancellationToken ct)
        => Task.CompletedTask;

    public Task LogoutAsync(CancellationToken ct)
        => Task.CompletedTask;

    public Task<LayerStyleDto> GetLayerStyleAsync(int layerId, DateOnly? asOf, CancellationToken ct)
        => Task.FromResult(new LayerStyleDto(
            System.Text.Json.JsonDocument.Parse("{\"themes\":{}}").RootElement.Clone()));

    public Task<LayerStyleDto> UpdateLayerStyleAsync(int layerId, LayerStyleDto style, CancellationToken ct)
        => Task.FromResult(style);

    // E'201 (WE'2): BatchUpdateFeaturesAsync stub
    public Task<FeatureBatchUpdateResponseDto> BatchUpdateFeaturesAsync(
        FeatureBatchUpdateRequestDto req, CancellationToken ct)
        => Task.FromResult(new FeatureBatchUpdateResponseDto(
            Results: req.EntityIds.Select((id, i) => new FeatureBatchUpdateResultDto(
                id, req.IfMatchVersions[i] + 1, DateOnly.FromDateTime(DateTime.UtcNow))).ToList(),
            Count: req.EntityIds.Count));
}
