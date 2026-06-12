using System.Text.Json;
using AgriGis.Api.Auth;
using AgriGis.Api.Dto;
using AgriGis.Api.Errors;
using Npgsql;

namespace AgriGis.Api.Endpoints;

// F'302 (Phase F' WF'3): /api/user/preferences/{key}
//   - 自己リソース (current user の preference のみアクセス可)
//   - admin / general / guest 全 role
//   - key は alphanumeric + underscore + dash のみ (XSS / SQL injection 防止)
//
// 主用途 (Phase F' WF'3): layer_order_v1 で CheckedListBox の表示順を保存
public static class UserPreferenceEndpoints
{
    private static readonly System.Text.RegularExpressions.Regex KeyRegex =
        new(@"^[a-zA-Z0-9_-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static RouteGroupBuilder MapUserPreferenceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/preferences/{key}", async (
            string key, ICurrentUser user, NpgsqlDataSource db) =>
        {
            ValidateKey(key);
            await using var cmd = db.CreateCommand(
                "SELECT key, value, updated_at FROM user_preference WHERE user_id = @uid AND key = @key");
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("key", key);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return Results.NotFound();
            }
            var k = r.GetString(0);
            var valueJson = r.GetString(1);
            using var doc = JsonDocument.Parse(valueJson);
            var updatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc));
            return Results.Ok(new UserPreferenceDto(k, doc.RootElement.Clone(), updatedAt));
        });

        group.MapPut("/preferences/{key}", async (
            string key, UserPreferencePutDto req, ICurrentUser user, NpgsqlDataSource db) =>
        {
            ValidateKey(key);
            var valueJson = req.Value.GetRawText();

            const string sql = @"
                INSERT INTO user_preference (user_id, key, value, updated_at)
                VALUES (@uid, @key, @value::jsonb, now())
                ON CONFLICT (user_id, key)
                DO UPDATE SET value = EXCLUDED.value, updated_at = now()
                RETURNING key, value, updated_at";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue("uid", user.UserId);
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("value", valueJson);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                throw new InvalidOperationException("UPSERT RETURNING returned no rows");
            }
            var k = r.GetString(0);
            var returnedValueJson = r.GetString(1);
            using var doc = JsonDocument.Parse(returnedValueJson);
            var updatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc));
            return Results.Ok(new UserPreferenceDto(k, doc.RootElement.Clone(), updatedAt));
        });

        return group;
    }

    private static void ValidateKey(string key)
    {
        if (!KeyRegex.IsMatch(key))
        {
            throw new ValidationException(new[]
            {
                new AttributeErrorDto("key", "format",
                    "key must match ^[a-zA-Z0-9_-]{1,64}$")
            });
        }
    }
}
