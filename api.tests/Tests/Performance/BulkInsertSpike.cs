using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgriGis.Api.Tests.Fixtures;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AgriGis.Api.Tests.Tests.Performance;

// B506 (WB0): fn_feature_insert × N の所要時間を計測してチャンクサイズ既定値の根拠とする。
// CI では [Trait("Category","Performance")] でデフォルト除外。
// ローカル実行: `dotnet test --filter Category=Performance`
[Collection(PostgisCollection.Name)]
[Trait("Category", "Performance")]
public sealed class BulkInsertSpike : IAsyncLifetime
{
    private readonly PostgisContainerFixture _pg;
    private readonly ITestOutputHelper _output;
    private const int FeatureCount = 5000;

    public BulkInsertSpike(PostgisContainerFixture pg, ITestOutputHelper output)
    {
        _pg = pg;
        _output = output;
    }

    public Task InitializeAsync() => DbReset.RunAsync(_pg.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task Insert_ChunkSize_MeasureElapsed(int chunkSize)
    {
        var fixturePath = SolutionRoot.Resolve("db/fixtures/perf/sample_5000.geojson");
        if (!File.Exists(fixturePath))
        {
            await PerfFixtureGenerator.GenerateAsync(fixturePath, FeatureCount);
            _output.WriteLine($"generated fixture: {fixturePath}");
        }

        var features = await LoadFeaturesAsync(fixturePath);
        Assert.Equal(FeatureCount, features.Count);

        var aliceUserId = SeedUsers.AliceId;
        var orgId = SeedUsers.OrgId;
        var actor = "Alice Admin";
        const int layerId = 2; // サンプル観測点

        var totalSw = Stopwatch.StartNew();
        long maxChunkMs = 0;
        long minChunkMs = long.MaxValue;
        int chunkCount = 0;

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        for (int offset = 0; offset < features.Count; offset += chunkSize)
        {
            var chunk = features.Skip(offset).Take(chunkSize).ToList();
            var requestId = Guid.NewGuid();

            var chunkSw = Stopwatch.StartNew();
            await using var tx = await conn.BeginTransactionAsync();
            foreach (var feature in chunk)
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT fn_feature_insert(@l, @e, @g, @a::jsonb, @act, @r, @u, @o)", conn, tx);
                cmd.Parameters.AddWithValue("l", layerId);
                cmd.Parameters.AddWithValue("e", Guid.NewGuid());
                cmd.Parameters.AddWithValue("g", feature.GeometryJson);
                cmd.Parameters.AddWithValue("a", feature.PropertiesJson);
                cmd.Parameters.AddWithValue("act", actor);
                cmd.Parameters.AddWithValue("r", requestId.ToString());
                cmd.Parameters.AddWithValue("u", aliceUserId);
                cmd.Parameters.AddWithValue("o", orgId);
                await cmd.ExecuteScalarAsync();
            }
            await tx.CommitAsync();
            chunkSw.Stop();

            chunkCount++;
            maxChunkMs = Math.Max(maxChunkMs, chunkSw.ElapsedMilliseconds);
            minChunkMs = Math.Min(minChunkMs, chunkSw.ElapsedMilliseconds);
        }

        totalSw.Stop();
        var totalMs = totalSw.ElapsedMilliseconds;
        var perFeatureUs = (totalMs * 1000.0) / FeatureCount;

        _output.WriteLine($"--- BulkInsert spike: chunkSize={chunkSize}, total={FeatureCount} ---");
        _output.WriteLine($"  total elapsed   : {totalMs} ms  ({totalMs / 1000.0:F2} s)");
        _output.WriteLine($"  per feature     : {perFeatureUs:F1} us  ({1_000_000.0 / perFeatureUs:F0}/s)");
        _output.WriteLine($"  chunks          : {chunkCount}");
        _output.WriteLine($"  chunk min / max : {minChunkMs} / {maxChunkMs} ms");
        _output.WriteLine($"  per-chunk avg   : {totalMs / (double)chunkCount:F0} ms");

        // 結果ファイルへの追記 (docs/layer-import.md の元データ)
        var resultLine = string.Format(CultureInfo.InvariantCulture,
            "chunk={0,4}  total={1,5}ms ({2:F2}s)  per_feature={3:F1}us  chunks={4}  chunk_min={5}ms  chunk_max={6}ms",
            chunkSize, totalMs, totalMs / 1000.0, perFeatureUs, chunkCount, minChunkMs, maxChunkMs);
        var resultFile = SolutionRoot.Resolve("db/fixtures/perf/wb0_results.txt");
        await File.AppendAllTextAsync(resultFile, resultLine + Environment.NewLine);

        // ガード: チャンク最大が CommandTimeout (デフォルト 30s) を超えていたら警告
        Assert.True(maxChunkMs < 60_000,
            $"max chunk elapsed ({maxChunkMs} ms) exceeded 60s; consider smaller chunkSize");
    }

    private static async Task<IReadOnlyList<FeatureSnapshot>> LoadFeaturesAsync(string path)
    {
        using var fs = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(fs);
        var features = doc.RootElement.GetProperty("features");
        var list = new List<FeatureSnapshot>(features.GetArrayLength());
        foreach (var f in features.EnumerateArray())
        {
            list.Add(new FeatureSnapshot(
                GeometryJson: f.GetProperty("geometry").GetRawText(),
                PropertiesJson: f.GetProperty("properties").GetRawText()));
        }
        return list;
    }

    private sealed record FeatureSnapshot(string GeometryJson, string PropertiesJson);
}

internal static class PerfFixtureGenerator
{
    // 帯広駅 (約 lon=143.20, lat=42.92) 周辺の擬似観測点を seed 固定で生成。
    // 形式は GeoJSON FeatureCollection (EPSG:4326)、properties は { name, crop?, observed_at }。
    public static async Task GenerateAsync(string path, int count)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var rng = new Random(20260602); // 再現性のため固定 seed
        const double centerLon = 143.20;
        const double centerLat = 42.92;
        const double spread = 0.10; // ± 約 10 km

        var sb = new StringBuilder(count * 200);
        sb.AppendLine("{");
        sb.AppendLine("  \"type\": \"FeatureCollection\",");
        sb.AppendLine("  \"crs\": { \"type\": \"name\", \"properties\": { \"name\": \"EPSG:4326\" } },");
        sb.AppendLine("  \"features\": [");

        var crops = new[] { "soy", "wheat", "corn", "potato", "beet" };
        for (int i = 0; i < count; i++)
        {
            var lon = centerLon + (rng.NextDouble() * 2 - 1) * spread;
            var lat = centerLat + (rng.NextDouble() * 2 - 1) * spread;
            var name = $"point_{i:D5}";
            var crop = crops[rng.Next(crops.Length)];

            sb.Append("    { \"type\": \"Feature\", \"geometry\": { \"type\": \"Point\", \"coordinates\": [");
            sb.Append(lon.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append(", ");
            sb.Append(lat.ToString("F6", CultureInfo.InvariantCulture));
            sb.Append("] }, \"properties\": { \"name\": \"");
            sb.Append(name);
            sb.Append("\" } }");
            if (i < count - 1) sb.Append(',');
            sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.Append('}');

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }
}
