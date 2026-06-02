namespace AgriGis.Api.Options;

// WB3 B203: バルク投入の運用設定。appsettings.json: BulkInsert section に対応。
public sealed class BulkInsertOptions
{
    public const string SectionName = "BulkInsert";

    public int MaxCountPerChunk { get; set; } = 5000;
    public int ChunkDefaultSize { get; set; } = 1000;
}
