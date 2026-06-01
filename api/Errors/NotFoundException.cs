namespace AgriGis.Api.Errors;

// リソース不存在。404 にマップ。
public sealed class NotFoundException : ApiException
{
    public NotFoundException(string message) : base(message) { }
}
