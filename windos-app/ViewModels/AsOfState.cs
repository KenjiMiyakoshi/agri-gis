namespace AgriGis.Desktop.ViewModels;

// E'203 (WE'2): MainForm の過去時点モード (asOf) 状態保持クラス。
//
// 設計:
// - Current = null: 現在時点モード (= valid_to='9999-12-31'、編集可)
// - Current != null: 過去時点モード (= 指定日付の asOf クエリ、編集 disable)
// - Changed イベントで監視者 (MainForm / ApiClient 呼び出し側) に通知
//
// MainForm はビューイベント (CheckBox / DateTimePicker) を受け、この状態クラスに
// 委譲することで、ロジックを切り出して unit test 可能にする (将来 H5 でフォーム
// 分割するときの足場)。
public sealed class AsOfState
{
    private DateOnly? _current;

    public DateOnly? Current => _current;

    /// <summary>過去時点モード時は true (= 編集 UI を read-only にする)</summary>
    public bool IsReadOnly => _current is not null;

    /// <summary>状態変化時の通知。新しい値が渡される。</summary>
    public event EventHandler<DateOnly?>? Changed;

    /// <summary>
    /// CheckBox トグル時。enabled=true なら defaultValue を current にセット、
    /// enabled=false なら null (現在時点モード)。
    /// </summary>
    public void SetEnabled(bool enabled, DateOnly defaultValue)
    {
        var next = enabled ? (DateOnly?)defaultValue : null;
        if (next == _current) return;
        _current = next;
        Changed?.Invoke(this, _current);
    }

    /// <summary>DateTimePicker 変更時。過去時点モードを保ったまま日付を更新。</summary>
    public void SetValue(DateOnly value)
    {
        if (_current == value) return;
        _current = value;
        Changed?.Invoke(this, _current);
    }

    /// <summary>過去時点モード解除 (= 現在時点モード)。</summary>
    public void Disable()
    {
        if (_current is null) return;
        _current = null;
        Changed?.Invoke(this, _current);
    }
}
