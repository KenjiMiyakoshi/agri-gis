using AgriGis.Api.Dto;
using AgriGis.Api.Errors;

namespace AgriGis.Api.Shared;

// E201 (WE2): asOf=YYYY-MM-DD パーサ共通化。Phase A FeatureEndpoints.ParseAsOf を移植。
// Phase A 流儀: DateOnly のみ、ISO datetime は 422 ValidationException。
public static class AsOfParser
{
    public static DateOnly? TryParse(string? asOf)
    {
        if (asOf is null) return null;
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
        {
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("asOf", "format", "asOf must be YYYY-MM-DD")
            });
        }
        return d;
    }
}
