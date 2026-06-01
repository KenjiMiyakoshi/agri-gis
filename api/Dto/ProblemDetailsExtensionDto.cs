namespace AgriGis.Api.Dto;

// ProblemDetails の extensions.errors[] 要素
// 主にバリデーションエラーで属性単位の理由を返す
public sealed record AttributeErrorDto(string AttributeKey, string Code, string Message);
