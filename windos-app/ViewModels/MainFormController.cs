using AgriGis.Desktop.Auth;
using AgriGis.Desktop.Dto;
using AgriGis.Desktop.Services;

namespace AgriGis.Desktop.ViewModels;

// H5-101 (WH5-1): MainForm から layers 管理 + Unauthorized 復旧経路を切り出した Controller。
//
// 設計:
// - UI 非依存 (ComboBox / Form を直接操作しない)
// - ReloadResult を返し、呼び出し側 (MainForm) が ComboBox 更新 + 選択 index 設定を行う
// - HandleUnauthorizedAsync は MainForm からデリゲートを受け取り、復旧後に再 reload
// - これにより MainForm の HandleUnauthorizedAsync 内の reload 重複実装を解消
public sealed class MainFormController
{
    private readonly IApiClient _api;
    private readonly ISessionStore _session;
    private readonly AsOfState _asOf;
    private IReadOnlyList<LayerDto> _layers = Array.Empty<LayerDto>();

    public MainFormController(IApiClient api, ISessionStore session, AsOfState asOf)
    {
        _api = api;
        _session = session;
        _asOf = asOf;
    }

    /// <summary>現在保持している layer 一覧 (read only)。</summary>
    public IReadOnlyList<LayerDto> Layers => _layers;

    /// <summary>現在の session (read only)。</summary>
    public Session? CurrentSession => _session.Current;

    /// <summary>
    /// API から layer 一覧を再取得し、以前の選択 layer_id を維持できる restore index を返す。
    /// </summary>
    public async Task<ReloadResult> ReloadAsync(int? prevSelectedLayerId, CancellationToken ct)
    {
        _layers = await _api.GetLayersAsync(_asOf.Current, ct);
        var restoreIndex = ComputeRestoreIndex(_layers, prevSelectedLayerId);
        return new ReloadResult(_layers, restoreIndex);
    }

    /// <summary>
    /// 401 検知時のユーザー再認証フロー。
    /// loginProvider がログインフォームを返し、結果が OK なら再 reload して true を返す。
    /// キャンセル時は false (呼び出し側で Close 等の処理)。
    /// </summary>
    public async Task<bool> TryRecoverUnauthorizedAsync(
        Func<bool> showLoginAndReturnSuccess,
        CancellationToken ct)
    {
        _session.Clear();
        var success = showLoginAndReturnSuccess();
        if (!success) return false;
        // 再ログイン後の reload (prevSelectedLayerId = null で先頭選択)
        _layers = await _api.GetLayersAsync(_asOf.Current, ct);
        return true;
    }

    /// <summary>
    /// 純関数: layer 一覧と前回の selected layer_id から復元すべき index を計算する。
    /// 0 件 → -1、prev が見つからなければ 0 (先頭)、見つかればその index。
    /// </summary>
    public static int ComputeRestoreIndex(IReadOnlyList<LayerDto> layers, int? prevSelectedLayerId)
    {
        if (layers.Count == 0) return -1;
        if (prevSelectedLayerId is not { } pid) return 0;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].LayerId == pid) return i;
        }
        return 0;
    }
}

/// <summary>
/// ReloadAsync の結果。呼び出し側 (MainForm) が ComboBox 更新と
/// SelectedIndex 設定に使う。
/// </summary>
public sealed record ReloadResult(IReadOnlyList<LayerDto> Layers, int RestoreIndex);
