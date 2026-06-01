namespace AgriGis.Api.Errors;

// X-Actor ヘッダ欠落・空白で投出。400 にマップ。
public sealed class MissingActorException : ApiException
{
    public MissingActorException() : base("X-Actor header is required") { }
}
