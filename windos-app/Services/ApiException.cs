using AgriGis.Desktop.Core;

namespace AgriGis.Desktop.Services;

// API レスポンスのエラー応答 (ProblemDetails) を内包する例外。
// Form 側は ex.Problem.Errors で属性単位のエラーにアクセスできる。
public sealed class ApiException : Exception
{
    public int? Status => Problem.Status;
    public ProblemDetailsParser.ParsedProblem Problem { get; }

    public ApiException(string message, ProblemDetailsParser.ParsedProblem problem)
        : base(message)
    {
        Problem = problem;
    }
}
