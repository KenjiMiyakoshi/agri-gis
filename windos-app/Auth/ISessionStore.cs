namespace AgriGis.Desktop.Auth;

// ログインセッションの保持インターフェース。Phase A は in-memory のみ。
// Set/Clear で Changed イベントを発火し、Form 側が UI を再評価できる。
public interface ISessionStore
{
    Session? Current { get; }

    void Set(Session session);
    void Clear();

    event EventHandler? Changed;
}
