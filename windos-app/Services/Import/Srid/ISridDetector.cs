using AgriGis.Desktop.Services.Import.Packages;

namespace AgriGis.Desktop.Services.Import.Srid;

// WC1 C102 で契約を確定、WC2 C104 で OgrSridDetector / ManualSridDetector を実装。
//
// 3 値設定駆動フォールバック (`appsettings.json: Import:SridFallbackPolicy`):
//   - Reject:       .prj 検出失敗で Rejected を返す (UI は Next 非活性)
//   - PromptUser:   FallbackToPrompt を返し、UI 側で手動 SRID 入力欄を表示 (デフォルト)
//   - AssumeWgs84:  FallbackToWgs84 を返し、audit_log.meta_jsonb.srid_inferred=true を埋める

public enum SridResolutionState
{
    /// <summary>.prj から OGR SpatialReference.AuthorityCode で SRID を確定できた</summary>
    Detected,

    /// <summary>.prj 不在 or 検出失敗。ユーザに手動入力を促す (`PromptUser` ポリシー時のデフォルト)</summary>
    FallbackToPrompt,

    /// <summary>.prj 不在 or 検出失敗。設定で AssumeWgs84 が選択されたため 4326 として扱う</summary>
    FallbackToWgs84,

    /// <summary>.prj 不在 or 検出失敗。設定で Reject が選択されたためインポート不可</summary>
    Rejected
}

public sealed record SridDetectionResult(int? Srid, SridResolutionState State);

public interface ISridDetector
{
    // C'101 (WC'1): IImportPackage 受け取りに変更 (MIF/TAB 対応)
    ValueTask<SridDetectionResult> DetectAsync(IImportPackage package, CancellationToken ct);
}
