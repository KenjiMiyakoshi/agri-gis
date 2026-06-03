namespace AgriGis.Desktop.ViewModels;

// H5-203 (WH5-2): 編集可否ロジックを純関数として切り出し。
//
// 背景:
// - asOf 過去時点モード中は編集不可 (Phase E では過去時点の更新は不可)
// - guest ロールは常に編集不可
// - 旧 MainForm では ApplyGuestRestriction (FeatureLoaded 時) と
//   OnAsOfEnabledChanged (asOf トグル時) が個別に SetReadOnly を叩いていた
//   → asOf 解除時に guest 制限が再適用されないバグ予防
//
// 設計:
// - 純関数 Compute(isGuest, isAsOfActive) => bool readOnly
// - 呼び出し側 (MainForm) が AttributeEditor.SetReadOnly に渡す
public static class EditPolicy
{
    /// <summary>
    /// 編集 UI を read-only にすべきかを計算する。
    /// guest または asOf 過去時点モード中は true。
    /// </summary>
    public static bool ShouldBeReadOnly(bool isGuest, bool isAsOfActive)
        => isGuest || isAsOfActive;
}
