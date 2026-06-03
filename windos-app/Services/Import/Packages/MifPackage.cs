using System.IO.Compression;

namespace AgriGis.Desktop.Services.Import.Packages;

// C'102 (WC'1): MIF/MID (MapInfo Interchange Format) Package。
// ShapefilePackage と同様に zip 展開 → 一時 dir 配置 → 必須/任意ファイル公開。
//
// MIF ファイル構成:
//   - .mif (必須): ヘッダ + geometry (テキスト)
//   - .mid (任意だが推奨): 属性データ (CSV-like)
//
// MIF ヘッダ抽出:
//   - "CharSet \"WindowsLatin1\"" 等の行から CharSetHeader として保持
//   - "CoordSys ..." 行から CoordSysLine として保持 (SridDetector の補助に渡す)
public sealed class MifPackage : IImportPackage
{
    private readonly string _tempRoot;

    public string MifPath { get; init; } = "";
    public string? MidPath { get; init; }

    /// <summary>.mif ヘッダから抽出した CharSet 値 (例: "WindowsLatin1")。</summary>
    public string? CharSetHeader { get; init; }

    /// <summary>.mif ヘッダから抽出した CoordSys 行 (SridDetector に渡す)。</summary>
    public string? CoordSysLine { get; init; }

    public string PrimaryPath => MifPath;
    public IReadOnlyList<string> MissingOptional { get; init; } = Array.Empty<string>();

    private MifPackage(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    /// <summary>
    /// zip ファイルを temp dir に展開し、MIF/MID セット 1 つを検出して返す。
    /// </summary>
    /// <exception cref="FileNotFoundException">zip が存在しない</exception>
    /// <exception cref="InvalidDataException">zip 内に .mif が複数 or 0 個</exception>
    public static async ValueTask<MifPackage> OpenAsync(string zipPath, CancellationToken ct)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("MIF zip not found", zipPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gis-mif-{Guid.NewGuid():N}");
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

            // 2. .mif を探す (大文字小文字無視)
            var mifFiles = Directory.GetFiles(tempRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).Equals(".mif", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (mifFiles.Length == 0)
                throw new InvalidDataException("No .mif file found in zip");
            if (mifFiles.Length > 1)
                throw new InvalidDataException(
                    $"Multiple .mif files found in zip ({mifFiles.Length}); expected exactly 1");

            var mifPath = Path.GetFullPath(mifFiles[0]);
            var baseName = Path.GetFileNameWithoutExtension(mifPath);

            // 3. 任意 sidecar (.mid)
            string? midPath = null;
            var missing = new List<string>();
            var candidates = Directory.GetFiles(tempRoot, "*", SearchOption.TopDirectoryOnly);
            foreach (var c in candidates)
            {
                var name = Path.GetFileNameWithoutExtension(c);
                if (!name.Equals(baseName, StringComparison.OrdinalIgnoreCase)) continue;
                var ext = Path.GetExtension(c);
                if (ext.Equals(".mid", StringComparison.OrdinalIgnoreCase))
                {
                    midPath = Path.GetFullPath(c);
                }
            }
            if (midPath is null) missing.Add(".mid");

            // 4. .mif ヘッダから CharSet / CoordSys 行を抽出 (先頭 50 行)
            var (charSet, coordSys) = await ExtractHeadersAsync(mifPath, ct);

            return new MifPackage(tempRoot)
            {
                MifPath = mifPath,
                MidPath = midPath,
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

    private static async Task<(string? CharSet, string? CoordSys)> ExtractHeadersAsync(
        string mifPath, CancellationToken ct)
    {
        string? charSet = null;
        string? coordSys = null;
        // .mif ヘッダは ASCII 範囲 (CharSet 行も含む)、CharSet 行で文字コードが決まる前に
        // 読まないといけないため UTF-8 (= ASCII compatible) で先頭数行のみ読む。
        using var reader = new StreamReader(mifPath, System.Text.Encoding.UTF8);
        for (int i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("CharSet", StringComparison.OrdinalIgnoreCase))
            {
                // "CharSet \"WindowsLatin1\"" → "WindowsLatin1"
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
            // "Data" 行は MIF body の開始、それ以降はヘッダ外
            if (trimmed.Equals("Data", StringComparison.OrdinalIgnoreCase)) break;
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
