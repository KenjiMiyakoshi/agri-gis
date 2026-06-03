using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AgriGis.Desktop.Services.Import.Srid;

// C'202 (WC'2): 起動時に ImportOptions.SridCatalog を SridConverter.RegisterWkt に一括登録する。
//
// 設計:
// - Program.cs から `Bootstrap()` を Application.Run の前に呼ぶ
// - 不正 WKT (ProjNet 例外) は警告ログのみ、他 entry の登録は継続
// - 重複 SRID は SridConverter.RegisterWkt の「後勝ち」セマンティクスに従う
public sealed class SridCatalogBootstrapper
{
    private readonly IOptions<ImportOptions> _options;
    private readonly SridConverter _converter;

    public SridCatalogBootstrapper(IOptions<ImportOptions> options, SridConverter converter)
    {
        _options = options;
        _converter = converter;
    }

    /// <summary>登録された entry 数を返す (warnings は LoadResult.Warnings)。</summary>
    public LoadResult Bootstrap()
    {
        var registered = 0;
        var warnings = new List<string>();
        var entries = _options.Value.SridCatalog ?? new List<SridCatalogEntry>();
        foreach (var entry in entries)
        {
            if (entry.Srid <= 0 || string.IsNullOrWhiteSpace(entry.Wkt))
            {
                warnings.Add($"[SridCatalog] skipped invalid entry srid={entry.Srid} name={entry.Name}");
                continue;
            }
            try
            {
                _converter.RegisterWkt(entry.Srid, entry.Wkt);
                Trace.WriteLine($"[SridCatalog] registered SRID {entry.Srid} ({entry.Name})");
                registered++;
            }
            catch (Exception ex)
            {
                warnings.Add($"[SridCatalog] failed to register SRID {entry.Srid}: {ex.Message}");
            }
        }
        return new LoadResult(registered, warnings);
    }

    public sealed record LoadResult(int Registered, IReadOnlyList<string> Warnings);
}
