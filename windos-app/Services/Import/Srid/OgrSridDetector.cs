using AgriGis.Desktop.Services.Import.Packages;
using Microsoft.Extensions.Options;
using OSGeo.OSR;

namespace AgriGis.Desktop.Services.Import.Srid;

// WC2 C104: .prj ファイルから OGR SpatialReference を読み、AuthorityCode で SRID を確定する。
// 失敗時は ImportOptions.SridFallbackPolicy に従って 3 値 (Reject / PromptUser / AssumeWgs84)
// のいずれかの SridResolutionState を返す。
public sealed class OgrSridDetector : ISridDetector
{
    private readonly IOptions<ImportOptions> _options;

    public OgrSridDetector(IOptions<ImportOptions> options)
    {
        _options = options;
    }

    public ValueTask<SridDetectionResult> DetectAsync(ShapefilePackage package, CancellationToken ct)
    {
        // .prj 不在は即 fallback
        if (string.IsNullOrEmpty(package.PrjPath) || !File.Exists(package.PrjPath))
        {
            return ValueTask.FromResult(ApplyFallback());
        }

        try
        {
            var wkt = File.ReadAllText(package.PrjPath);
            if (string.IsNullOrWhiteSpace(wkt))
            {
                return ValueTask.FromResult(ApplyFallback());
            }

            // OGR SpatialReference 経由で AuthorityCode 抽出
            using var srs = new SpatialReference("");
            var ret = srs.ImportFromWkt(ref wkt);
            if (ret != 0)
            {
                return ValueTask.FromResult(ApplyFallback());
            }

            srs.AutoIdentifyEPSG();
            var auth = srs.GetAuthorityCode(null);
            if (!string.IsNullOrEmpty(auth) && int.TryParse(auth, out var srid))
            {
                return ValueTask.FromResult(new SridDetectionResult(srid, SridResolutionState.Detected));
            }
            return ValueTask.FromResult(ApplyFallback());
        }
        catch (IOException)
        {
            return ValueTask.FromResult(ApplyFallback());
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(ApplyFallback());
        }
    }

    private SridDetectionResult ApplyFallback()
    {
        var policy = _options.Value.SridFallbackPolicy ?? "PromptUser";
        return policy.Trim() switch
        {
            "Reject" => new SridDetectionResult(null, SridResolutionState.Rejected),
            "AssumeWgs84" => new SridDetectionResult(4326, SridResolutionState.FallbackToWgs84),
            _ => new SridDetectionResult(null, SridResolutionState.FallbackToPrompt)
        };
    }
}
