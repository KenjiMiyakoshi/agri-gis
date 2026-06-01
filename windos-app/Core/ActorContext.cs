namespace AgriGis.Desktop.Core;

// 現在のユーザー識別子。本サイクルでは Environment.UserName を暫定値とする。
// 将来、認証導入時にここを差し替える (Func<string> ベース等)。
public static class ActorContext
{
    public static string Current { get; } = Environment.UserName;
}
