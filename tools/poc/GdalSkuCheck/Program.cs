// Phase C WC0 C100: MaxRev.Gdal.WindowsRuntime.Minimal SKU の Shapefile driver 含有確認 PoC
// 受け入れ条件:
//   1. GdalBase.ConfigureAll() がエラーなしで完了
//   2. OGR driver 一覧に "ESRI Shapefile" が存在
//   3. (情報目的) "MapInfo File" (TAB) と LIBKML の有無を記録 → Phase C' / Phase D 判断材料
//   4. ネイティブ DLL 配布サイズの目安を出力
//
// 出力: 標準出力 → docs/issues/PHASE_C_C100_POC_RESULT.md にコピペで貼り付ける

using System.Globalization;
using MaxRev.Gdal.Core;
using OSGeo.OGR;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var ci = CultureInfo.InvariantCulture;

Console.WriteLine("=== Phase C WC0 / C100: Minimal SKU PoC ===");
Console.WriteLine($"Run at:           {DateTimeOffset.UtcNow.ToString("o", ci)}");
Console.WriteLine($"Process arch:     {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"OS:               {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine();

// --- 1. GdalBase.ConfigureAll() ---
Console.WriteLine("[1/4] GdalBase.ConfigureAll() ...");
try
{
    GdalBase.ConfigureAll();
    Console.WriteLine("      OK");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"      FAILED: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine($"      → Phase C 着手不可。MaxRev.Gdal.WindowsRuntime (Full SKU) 切替 Issue 起票が必要");
    return 2;
}

// --- 2. OGR driver 列挙 + 重要 driver の存在確認 ---
Console.WriteLine();
Console.WriteLine("[2/4] OGR driver enumeration ...");
var driverCount = Ogr.GetDriverCount();
Console.WriteLine($"      Total drivers: {driverCount}");

var driverNames = new List<string>(driverCount);
for (int i = 0; i < driverCount; i++)
{
    using var d = Ogr.GetDriver(i);
    driverNames.Add(d.GetName());
}
driverNames.Sort(StringComparer.OrdinalIgnoreCase);

string[] mustHave = ["ESRI Shapefile"];
string[] niceToHave = ["MapInfo File", "LIBKML", "KML", "GeoJSON", "CSV"];

var missingMust = mustHave.Where(n => !driverNames.Any(d => string.Equals(d, n, StringComparison.OrdinalIgnoreCase))).ToList();
var presentMust = mustHave.Except(missingMust);

Console.WriteLine();
Console.WriteLine("      Must-have drivers:");
foreach (var n in mustHave)
{
    var present = driverNames.Any(d => string.Equals(d, n, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"        [{(present ? "✓" : "✗")}] {n}");
}

Console.WriteLine();
Console.WriteLine("      Nice-to-have drivers (Phase C' / Phase D 判断材料):");
foreach (var n in niceToHave)
{
    var present = driverNames.Any(d => string.Equals(d, n, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"        [{(present ? "✓" : "·")}] {n}");
}

// --- 3. 配布サイズの目安 ---
Console.WriteLine();
Console.WriteLine("[3/4] Distribution size estimates ...");
var appBaseDir = AppContext.BaseDirectory;
long nativeBytes = 0;
int nativeCount = 0;
foreach (var f in Directory.EnumerateFiles(appBaseDir, "*.dll", SearchOption.AllDirectories)
                            .Concat(Directory.EnumerateFiles(appBaseDir, "*.so", SearchOption.AllDirectories)))
{
    var name = Path.GetFileName(f).ToLowerInvariant();
    // Match: gdal*, ogr*, proj*, geos*, sqlite*, expat*, hdf*, netcdf*, openjp2*, tiff*, png*, jpeg*, webp*, zstd*, lz4*, xml2*, curl*, ssl*, crypto*, etc
    if (name.StartsWith("gdal", StringComparison.Ordinal) ||
        name.StartsWith("ogr", StringComparison.Ordinal) ||
        name.StartsWith("proj", StringComparison.Ordinal) ||
        name.StartsWith("geos", StringComparison.Ordinal) ||
        name.Contains("sqlite", StringComparison.Ordinal) ||
        name.Contains("expat", StringComparison.Ordinal) ||
        name.Contains("openjp2", StringComparison.Ordinal) ||
        name.Contains("tiff", StringComparison.Ordinal) ||
        name.Contains("png", StringComparison.Ordinal) ||
        name.Contains("jpeg", StringComparison.Ordinal) ||
        name.Contains("zstd", StringComparison.Ordinal) ||
        name.Contains("lz4", StringComparison.Ordinal) ||
        name.Contains("xml", StringComparison.Ordinal) ||
        name.Contains("curl", StringComparison.Ordinal))
    {
        var fi = new FileInfo(f);
        nativeBytes += fi.Length;
        nativeCount++;
    }
}
Console.WriteLine($"      Native DLLs under output dir: {nativeCount} files, {nativeBytes / 1024.0 / 1024.0:F1} MB");

long totalOutBytes = Directory.EnumerateFiles(appBaseDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
Console.WriteLine($"      Total build output:           {totalOutBytes / 1024.0 / 1024.0:F1} MB");

// --- 4. Go / No-Go 判定 ---
Console.WriteLine();
Console.WriteLine("[4/4] Go / No-Go judgement ...");
if (missingMust.Count > 0)
{
    Console.WriteLine($"      [NO-GO] missing must-have drivers: {string.Join(", ", missingMust)}");
    Console.WriteLine($"      → Action: MaxRev.Gdal.WindowsRuntime (Full SKU) 切替 Issue 起票、WC1 着手延期");
    return 1;
}
Console.WriteLine($"      [GO] all must-have drivers are present in Minimal SKU");
Console.WriteLine($"      → Action: WC1 (C101 / C102) 着手可");

Console.WriteLine();
Console.WriteLine("=== End of PoC ===");
return 0;
