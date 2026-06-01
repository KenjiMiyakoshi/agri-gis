namespace AgriGis.Api.Errors;

// API 由来のエラー基底。ProblemDetailsMiddleware で HTTP コードへマップされる。
public abstract class ApiException : Exception
{
    protected ApiException(string message) : base(message) { }
}
