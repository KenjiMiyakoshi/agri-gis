using System.IO.Compression;

namespace AgriGis.Desktop.Services.Import.Packages;

// C'201 (WC'2): MapInfo TAB Package。
//
// TAB ファイル構成:
//   - .tab (必須): ヘッダ + データ参照
//   - .map (必須): geometry
//   - .dat (必須): 属性データ (dBase format)
//   - .id  (必須): index
//   - .ind (任意): 検索 index
//
// .tab ヘッダ抽出:
//   - "CharSet \"X\"" → CharSetHeader
//   - "CoordSys Earth Projection ..." → CoordSysLine (SridDetector に渡す)
public sealed class TabPackage : IImportPackage
{
    private readonly string _tempRoot;

    public string TabPath { get; init; } = "";
    public string? MapPath { get; init; }
    public string? DatPath { get; init; }
    public string? IdPath { get; init; }
    public string? IndPath { get; init; }

    /// <summary>.tab ヘッダから抽出した CharSet 値 (例: "WindowsLatin1")。</summary>
    public string? CharSetHeader { get; init; }

    /// <summary>.tab ヘッダから抽出した CoordSys 行 (SridDetector に渡す)。</summary>
    public string? CoordSysLine { get; init; }

    public string PrimaryPath => TabPath;
    public IReadOnlyList<string> MissingOptional { get; init; } = Array.Empty<string>();

    private TabPackage(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    /// <summary>
    /// zip ファイルを temp dir に展開し、TAB セット 1 つを検出して返す。
    /// .tab + .map + .dat + .id は必須、.ind は任意。
    /// </summary>
    public static async ValueTask<TabPackage> OpenAsync(string zipPath, CancellationToken ct)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("TAB zip not found", zipPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gis-tab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using (var fs = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false))
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var entryName = entry.FullName.Replace('\\', '/');
                    if (entryName.Split('/').Any(p => p == ".."))
                    {
                        throw new InvalidDataException($"Suspicious zip entry path: {entry.FullName}");
                    }

                    var destPath = Path.Combine(tempRoot, Path.GetFileName(entry.Name));
                    await using var es = entry.Open();
                    await using var os = File.Create(destPath);
                    await es.CopyToAsync(os, ct);
                }
            }

            var tabFiles = Directory.GetFiles(tempRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).Equals(".tab", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (tabFiles.Length == 0)
                throw new InvalidDataException("No .tab file found in zip");
            if (tabFiles.Length > 1)
                throw new InvalidDataException(
                    $"Multiple .tab files found in zip ({tabFiles.Length}); expected exactly 1");

            var tabPath = Path.GetFullPath(tabFiles[0]);
            var baseName = Path.GetFileNameWithoutExtension(tabPath);

            // sidecar 検出
            var (map, missMap) = TryFindSidecar(tempRoot, baseName, ".map");
            var (dat, missDat) = TryFindSidecar(tempRoot, baseName, ".dat");
            var (id, missId)   = TryFindSidecar(tempRoot, baseName, ".id");
            var (ind, missInd) = TryFindSidecar(tempRoot, baseName, ".ind");

            var missing = new List<string>();
            // 必須欠落は warning だけにせず即 throw 推奨だが、Shapefile 流儀踏襲で
            // 必須も MissingOptional に並べてユーザに見せ、GDAL の Open で失敗させる
            if (missMap) missing.Add(".map");
            if (missDat) missing.Add(".dat");
            if (missId)  missing.Add(".id");
            if (missInd) missing.Add(".ind");

            var (charSet, coordSys) = await ExtractHeadersAsync(tabPath, ct);

            return new TabPackage(tempRoot)
            {
                TabPath = tabPath,
                MapPath = map,
                DatPath = dat,
                IdPath  = id,
                IndPath = ind,
                CharSetHeader = charSet,
                CoordSysLine = coordSys,
                MissingOptional = missing
            };
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private static (string? Path, bool Missing) TryFindSidecar(string dir, string baseName, string ext)
    {
        var candidates = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
        foreach (var c in candidates)
        {
            var name = Path.GetFileNameWithoutExtension(c);
            if (!name.Equals(baseName, StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetExtension(c).Equals(ext, StringComparison.OrdinalIgnoreCase))
            {
                return (Path.GetFullPath(c), false);
            }
        }
        return (null, true);
    }

    private static async Task<(string? CharSet, string? CoordSys)> ExtractHeadersAsync(
        string tabPath, CancellationToken ct)
    {
        string? charSet = null;
        string? coordSys = null;
        // .tab ヘッダは ASCII 範囲。UTF-8 で先頭 50 行のみ読む
        using var reader = new StreamReader(tabPath, System.Text.Encoding.UTF8);
        for (int i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("CharSet", StringComparison.OrdinalIgnoreCase))
            {
                var quoteStart = trimmed.IndexOf('"');
                var quoteEnd = trimmed.LastIndexOf('"');
                if (quoteStart >= 0 && quoteEnd > quoteStart)
                {
                    charSet = trimmed.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                }
            }
            else if (trimmed.StartsWith("CoordSys", StringComparison.OrdinalIgnoreCase))
            {
                coordSys = trimmed;
            }
        }
        return (charSet, coordSys);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    public ValueTask DisposeAsync()
    {
        TryDeleteDirectory(_tempRoot);
        return ValueTask.CompletedTask;
    }
}
