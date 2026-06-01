namespace AgriGis.Api.Errors;

// 楽観ロック衝突（If-Match の expected_version が現行 version と不一致）。409 にマップ。
public sealed class VersionConflictException : ApiException
{
    public VersionConflictException() : base("Version conflict") { }
    public VersionConflictException(string message) : base(message) { }
}
