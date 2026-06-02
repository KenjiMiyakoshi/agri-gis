using System.IO.Compression;

namespace AgriGis.Desktop.Services.Import.Packages;

// WC2 C103: Shapefile zip を temp dir に実展開し、必須/任意ファイルを公開する。
//
// 設計判断 (PHASE_C_DESIGN_P §6.4 / 論点 1 / 実装リスク Design 決定 3):
//   - /vsizip/ 仮想 FS は採用しない (CPG auto-detect サイレント文字化けリスク)
//   - Path.GetTempPath() 配下に gis-shp-{Guid} で衝突回避
//   - .shp は必須、複数同梱は InvalidDataException
//   - .shx / .dbf / .prj / .cpg は任意 (不在は警告のみ、MissingOptionalExtensions で公開)
//   - IAsyncDisposable で再帰削除
public sealed class ShapefilePackage : IAsyncDisposable
{
    private readonly string _tempRoot;

    public string ShpPath { get; init; } = "";
    public string? ShxPath { get; init; }
    public string? DbfPath { get; init; }
    public string? PrjPath { get; init; }
    public string? CpgPath { get; init; }

    /// <summary>
    /// 任意 sidecar (.shx / .dbf / .prj / .cpg) で zip 内に見つからなかった拡張子のリスト。
    /// 呼び出し側でログ出力に使う。
    /// </summary>
    public IReadOnlyList<string> MissingOptionalExtensions { get; init; } = Array.Empty<string>();

    private ShapefilePackage(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    /// <summary>
    /// zip ファイルを temp dir に展開し、Shapefile セット 1 つを検出して返す。
    /// </summary>
    /// <exception cref="FileNotFoundException">zip が存在しない</exception>
    /// <exception cref="InvalidDataException">zip 内に .shp が複数 or 0 個</exception>
    public static async ValueTask<ShapefilePackage> OpenAsync(string zipPath, CancellationToken ct)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Shapefile zip not found", zipPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gis-shp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 1. 展開
            await using (var fs = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false))
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name)) continue; // ディレクトリエントリ

                    // Zip Slip 防御: '..' を含むエントリ名を拒否
                    var entryName = entry.FullName.Replace('\\', '/');
                    if (entryName.Split('/').Any(p => p == ".."))
                    {
                        throw new InvalidDataException($"Suspicious zip entry path: {entry.FullName}");
                    }

                    // フラットな構造で展開 (Shapefile のサブディレクトリ構造は無視、
                    // 拡張子セットがそろっていれば動く想定)
                    var destPath = Path.Combine(tempRoot, Path.GetFileName(entry.Name));
                    await using var es = entry.Open();
                    await using var os = File.Create(destPath);
                    await es.CopyToAsync(os, ct);
                }
            }

            // 2. .shp を探す (大文字小文字無視)
            var shpFiles = Directory.GetFiles(tempRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).Equals(".shp", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (shpFiles.Length == 0)
                throw new InvalidDataException("No .shp file found in zip");
            if (shpFiles.Length > 1)
                throw new InvalidDataException(
                    $"Multiple .shp files found in zip ({shpFiles.Length}); expected exactly 1");

            var shpPath = Path.GetFullPath(shpFiles[0]);
            var baseName = Path.GetFileNameWithoutExtension(shpPath);

            // 3. 任意 sidecar (案 P §6.4)
            var (shx, miss1) = TryFindSidecar(tempRoot, baseName, ".shx");
            var (dbf, miss2) = TryFindSidecar(tempRoot, baseName, ".dbf");
            var (prj, miss3) = TryFindSidecar(tempRoot, baseName, ".prj");
            var (cpg, miss4) = TryFindSidecar(tempRoot, baseName, ".cpg");

            var missing = new List<string>();
            if (miss1) missing.Add(".shx");
            if (miss2) missing.Add(".dbf");
            if (miss3) missing.Add(".prj");
            if (miss4) missing.Add(".cpg");

            return new ShapefilePackage(tempRoot)
            {
                ShpPath = shpPath,
                ShxPath = shx,
                DbfPath = dbf,
                PrjPath = prj,
                CpgPath = cpg,
                MissingOptionalExtensions = missing
            };
        }
        catch
        {
            // 展開途中で失敗 → temp 削除して再 throw
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort: temp dir 削除失敗は障害にしない
        }
    }

    public ValueTask DisposeAsync()
    {
        TryDeleteDirectory(_tempRoot);
        return ValueTask.CompletedTask;
    }
}
