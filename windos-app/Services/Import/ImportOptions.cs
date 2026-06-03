namespace AgriGis.Desktop.Services.Import;

// WC2 C104 / C105: appsettings.json: Import セクションをバインドする設定クラス。
// PHASE_C_DESIGN_P §6.10 の運用設定と整合。
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>
    /// SRID 検出失敗時のフォールバックポリシー。Reject / PromptUser / AssumeWgs84 のいずれか。
    /// デフォルト PromptUser (UI で手動 SRID 入力欄を表示)。
    /// </summary>
    public string SridFallbackPolicy { get; set; } = "PromptUser";

    /// <summary>
    /// .cpg ファイル不在 / 解析失敗時の文字コード fallback (OGR Open option 形式)。
    /// デフォルト CP932。
    /// </summary>
    public string DefaultDbfEncoding { get; set; } = "CP932";

    /// <summary>
    /// C'202 (WC'2): EPSG コードを持たないローカル CS の WKT 事前登録テーブル。
    /// 起動時に SridCatalogBootstrapper.Bootstrap() が SridConverter.RegisterWkt を一括呼び出し。
    /// 例: 旧日本測地系 平面直角座標系 II / IV 系 (和歌山等)。
    /// </summary>
    public List<SridCatalogEntry> SridCatalog { get; set; } = new();
}

// C'202 (WC'2): SridCatalog の 1 件分エントリ。
public sealed class SridCatalogEntry
{
    public int Srid { get; set; }
    public string Name { get; set; } = "";
    public string Wkt { get; set; } = "";
    public string Source { get; set; } = "";
}
