using AgriGis.Desktop.Services.Import.Packages;

namespace AgriGis.Desktop.Services.Import.Srid;

// WC2 C104: ユーザが手動で SRID を指定した場合に使う ISridDetector 実装。
// PromptUser フォールバック → ViewModel が手動入力を受け取り → 本 Detector に差し替えて再検出。
public sealed class ManualSridDetector : ISridDetector
{
    private readonly int _srid;

    public ManualSridDetector(int srid)
    {
        if (srid <= 0) throw new ArgumentOutOfRangeException(nameof(srid), "SRID must be positive");
        _srid = srid;
    }

    public ValueTask<SridDetectionResult> DetectAsync(ShapefilePackage package, CancellationToken ct)
        => ValueTask.FromResult(new SridDetectionResult(_srid, SridResolutionState.Detected));
}
