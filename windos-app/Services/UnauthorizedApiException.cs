using AgriGis.Desktop.Core;

namespace AgriGis.Desktop.Services;

// 401 Unauthorized 専用の ApiException 派生。MainForm が catch して再ログインフローを発動する。
public sealed class UnauthorizedApiException : Exception
{
    public ProblemDetailsParser.ParsedProblem Problem { get; }

    public UnauthorizedApiException(ProblemDetailsParser.ParsedProblem problem)
        : base("401 Unauthorized")
    {
        Problem = problem;
    }
}
