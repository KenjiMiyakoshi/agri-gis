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
}
