# A401: ActorContext 削除 + ISessionStore / InMemorySessionStore 新設

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 0.5d |
| Depends on | なし |
| Blocks | A402, A403, A404 |

## 概要
`windos-app/Core/ActorContext.cs` を削除し、新規 `Auth/ISessionStore.cs` + `InMemorySessionStore.cs` に置き換える (H3 完全解消)。永続化は無し。

## 背景・目的
採択案「案 P」の WinForms セクション:
> `windos-app/Core/ActorContext.cs` **削除**（H3 完全解消）
> 新規 `windos-app/Auth/ISessionStore.cs` + `InMemorySessionStore.cs`（in-memory のみ、永続化なし）

ActorContext は X-Actor ヘッダ用の静的グローバルだったが、JWT 化に伴いセッション (access_token + user info) を保持する責務に変わる。

## スコープ
### 含む
- `windos-app/Core/ActorContext.cs` 削除（および参照箇所一掃）
- `windos-app/Auth/ISessionStore.cs` 新規:
  - `Session? Current { get; }`
  - `void Set(Session s)`, `void Clear()`
  - `event EventHandler? Changed`
- `windos-app/Auth/Session.cs` レコード: `AccessToken`, `ExpiresAt`, `UserId`, `LoginId`, `DisplayName`, `OrgId`, `Roles`, `IsGuest`, `IsAdmin`
- `windos-app/Auth/InMemorySessionStore.cs`: シングルトン、内部 `_current` フィールド
- DI 登録（既存 ServiceCollection 設定箇所に追加）

### 含まない
- LoginForm 実装 (A402)
- BearerHandler (A403)
- 401 再ログインフロー (A404)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `Core/ActorContext.cs` ファイルが存在しない
- [ ] `ActorContext.Current` 等の参照がコード中に残っていない（要 grep）
- [ ] `ISessionStore` は DI で `singleton` 登録
- [ ] `Session` は不変 record、`Roles` は `IReadOnlyList<string>`
- [ ] `Set` 後に `Changed` イベント発火
- [ ] `Clear` 後 `Current == null`

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Core\ActorContext.cs` (削除)
- `D:\proj\agri-gis\windos-app\Auth\ISessionStore.cs` (新規)
- `D:\proj\agri-gis\windos-app\Auth\Session.cs` (新規)
- `D:\proj\agri-gis\windos-app\Auth\InMemorySessionStore.cs` (新規)
- `D:\proj\agri-gis\windos-app\Program.cs` (DI 登録)
- ActorContext を参照していた既存箇所すべて（A403 で実質置換、ここではコンパイル通すための最低限の整理）

## 実装ノート
```csharp
// Auth/Session.cs
public sealed record Session(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles)
{
    public bool IsGuest => Roles.Contains("guest");
    public bool IsAdmin => Roles.Contains("admin");
}

// Auth/ISessionStore.cs
public interface ISessionStore
{
    Session? Current { get; }
    void Set(Session s);
    void Clear();
    event EventHandler? Changed;
}

// Auth/InMemorySessionStore.cs
public sealed class InMemorySessionStore : ISessionStore
{
    private Session? _current;
    public Session? Current => _current;
    public event EventHandler? Changed;
    public void Set(Session s) { _current = s; Changed?.Invoke(this, EventArgs.Empty); }
    public void Clear()         { _current = null; Changed?.Invoke(this, EventArgs.Empty); }
}
```

注意点:
- thread-safety: WinForms UI スレッド経由が原則だが、BearerHandler は別スレッドで読むので `_current` への代入は ref 型で原子的に行えば最低限 OK（必要なら `volatile` か `lock`）
- 既存テスト (`windos-app` 側があれば) で ActorContext を mock していた箇所を ISessionStore mock に置換

## テスト観点
- A505 の前提として ISessionStore が DI で取れる
- WinForms 単体テストは Phase A 範囲外（既存 0505 でモック化されていれば同等の置換）
