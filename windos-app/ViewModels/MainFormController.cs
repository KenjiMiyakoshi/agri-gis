using System.Text.Json;
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
    // F302 (Phase F WF3): 複数 layer 同時表示の ON/OFF 状態。LayerId の集合。
    private readonly HashSet<int> _visibleLayerIds = new();
    // F'303 (Phase F' WF'3): z-order 保持の順序付きリスト (上位 = 前面)。
    //   不変条件: _orderedLayerIds は _visibleLayerIds と要素一致 (set としては等価)。
    private readonly List<int> _orderedLayerIds = new();
    // F'305 (Phase F' WF'3): 永続化対象キー
    public const string LayerOrderPreferenceKey = "layer_order_v1";

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

    /// <summary>F302: 現在 ON になっている layer_id 集合 (read only snapshot)。</summary>
    public IReadOnlySet<int> VisibleLayerIds => _visibleLayerIds;

    /// <summary>F'303: 現在 ON の layer_id を z-order 順 (上位 = 前面) で取得。</summary>
    public IReadOnlyList<int> OrderedLayerIds => _orderedLayerIds;

    /// <summary>F302: layer 表示状態の切替 (CheckedListBox の ItemCheck から呼ぶ)。</summary>
    /// <remarks>F'303: ON で追加 (末尾)、OFF で削除。_orderedLayerIds も同期維持。</remarks>
    public void SetLayerVisible(int layerId, bool visible)
    {
        if (visible)
        {
            if (_visibleLayerIds.Add(layerId))
            {
                _orderedLayerIds.Add(layerId);
            }
        }
        else
        {
            if (_visibleLayerIds.Remove(layerId))
            {
                _orderedLayerIds.Remove(layerId);
            }
        }
    }

    /// <summary>F'303 (Phase F' WF'3): z-order を並べ替える。
    /// newOrder は _visibleLayerIds と要素一致している必要がある (差分があれば例外)。
    /// </summary>
    public void ReorderLayers(IReadOnlyList<int> newOrder)
    {
        if (newOrder.Count != _orderedLayerIds.Count)
            throw new InvalidOperationException("ReorderLayers: count mismatch");
        foreach (var id in newOrder)
        {
            if (!_visibleLayerIds.Contains(id))
                throw new InvalidOperationException($"ReorderLayers: unknown layerId {id}");
        }
        _orderedLayerIds.Clear();
        _orderedLayerIds.AddRange(newOrder);
    }

    /// <summary>F'305: 永続化された layer 順序を取得 (API 呼び出し、未設定時 null)。</summary>
    public async Task<IReadOnlyList<int>?> LoadLayerOrderAsync(CancellationToken ct)
    {
        var pref = await _api.GetUserPreferenceAsync(LayerOrderPreferenceKey, ct);
        if (pref is null) return null;
        if (pref.Value.ValueKind != JsonValueKind.Array) return null;
        var ids = new List<int>(pref.Value.GetArrayLength());
        foreach (var el in pref.Value.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>F'305: 現在の OrderedLayerIds を永続化。</summary>
    public Task SaveLayerOrderAsync(CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToElement(_orderedLayerIds);
        return _api.PutUserPreferenceAsync(LayerOrderPreferenceKey,
            new UserPreferencePutDto(json), ct);
    }

    /// <summary>F302: layer_id から layer 情報を取得 (見つからなければ null)。</summary>
    /// <remarks>AttributeEditor の canEdit 判定で使用。</remarks>
    public LayerDto? GetLayerById(int layerId)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].LayerId == layerId) return _layers[i];
        }
        return null;
    }

    /// <summary>
    /// API から layer 一覧を再取得し、以前の選択 layer_id を維持できる restore index を返す。
    /// F302: VisibleLayerIds は再 reload しても保持 (既に削除された layer は集合から落とす)。
    /// 初回 (まだ集合が空) の場合は、先頭 1 件を初期 ON にする。
    /// </summary>
    public async Task<ReloadResult> ReloadAsync(int? prevSelectedLayerId, CancellationToken ct)
    {
        _layers = await _api.GetLayersAsync(_asOf.Current, ct);
        // F302/F'303: 既存 _visibleLayerIds のうち、現在 _layers に存在しないものを削除 (両方同期)
        var availableIds = new HashSet<int>(_layers.Select(l => l.LayerId));
        _visibleLayerIds.RemoveWhere(id => !availableIds.Contains(id));
        _orderedLayerIds.RemoveAll(id => !availableIds.Contains(id));
        // 初回ロード時 (集合が空 + 層がある) は先頭を ON
        if (_visibleLayerIds.Count == 0 && _layers.Count > 0)
        {
            _visibleLayerIds.Add(_layers[0].LayerId);
            _orderedLayerIds.Add(_layers[0].LayerId);
        }
        var restoreIndex = ComputeRestoreIndex(_layers, prevSelectedLayerId);
        return new ReloadResult(_layers, restoreIndex);
    }

    /// <summary>F'305 (Phase F' WF'3): 永続化された順序を適用 (起動時に呼ぶ)。
    /// 現在の _layers に存在する layer_id のみ採用、存在しないものは無視。
    /// 永続化された集合外の layer は順序の末尾に追加 (新規 layer)。
    /// </summary>
    public void ApplyPersistedLayerOrder(IReadOnlyList<int> persistedOrder)
    {
        var availableIds = new HashSet<int>(_layers.Select(l => l.LayerId));
        var prevVisible = new HashSet<int>(_visibleLayerIds);
        _visibleLayerIds.Clear();
        _orderedLayerIds.Clear();

        // 永続化順を反映 (available + 元 visible だったもののみ)
        foreach (var id in persistedOrder)
        {
            if (availableIds.Contains(id) && prevVisible.Contains(id))
            {
                _visibleLayerIds.Add(id);
                _orderedLayerIds.Add(id);
            }
        }
        // 元 visible だが永続化順に無いもの (新規 layer 等) は末尾に追加
        foreach (var id in prevVisible)
        {
            if (availableIds.Contains(id) && !_visibleLayerIds.Contains(id))
            {
                _visibleLayerIds.Add(id);
                _orderedLayerIds.Add(id);
            }
        }
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
