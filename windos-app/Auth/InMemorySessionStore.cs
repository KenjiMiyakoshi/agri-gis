namespace AgriGis.Desktop.Auth;

// プロセスメモリ上でセッションを保持する単純な実装。永続化なし。
// HDD 盗難時のリスク低減と引き換えに、アプリ再起動で再ログインを要求する。
public sealed class InMemorySessionStore : ISessionStore
{
    private Session? _current;

    public Session? Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public void Set(Session session)
    {
        Volatile.Write(ref _current, session);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Volatile.Write(ref _current, null);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
