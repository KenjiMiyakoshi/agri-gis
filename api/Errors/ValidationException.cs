using AgriGis.Api.Dto;

namespace AgriGis.Api.Errors;

// 属性スキーマ違反。422 にマップ + extensions.errors[] に AttributeErrorDto を載せる。
public sealed class ValidationException : ApiException
{
    public IReadOnlyList<AttributeErrorDto> Errors { get; }

    public ValidationException(IReadOnlyList<AttributeErrorDto> errors)
        : base("Validation failed")
    {
        Errors = errors;
    }
}
