# A201: BCrypt.Net-Next + JwtBearer + appsettings/環境変数

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 1d |
| Depends on | A101 |
| Blocks | A202, A203, A205, A206, A207, A502 |

## 概要
API プロジェクトに BCrypt.Net-Next と JwtBearer 認証を導入し、`AGRI_GIS_JWT_SECRET` 環境変数を 32byte 強制で読み込む基盤を作る。

## 背景・目的
採択案「案 P」の認証基盤セクション:
> access only (8h)、HS256 + 環境変数 `AGRI_GIS_JWT_SECRET`（32byte 強制チェック）
> claims: `sub` (UUID), `name` (login_id), `role` (複数値発行)、`org_id`、標準 `iss`/`aud`/`exp`/`iat`/`jti`
> `ISigningKeyProvider` 抽象 **なし**
> BCrypt.Net-Next（work factor 11）

## スコープ
### 含む
- NuGet: `BCrypt.Net-Next` (4.0.3+), `Microsoft.AspNetCore.Authentication.JwtBearer`
- `appsettings.json` に `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes` (480)
- `AGRI_GIS_JWT_SECRET` 環境変数を Program.cs 起動時に検証（NULL/<32byte で起動失敗、明示的なエラーメッセージ）
- `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` を Program.cs に追加
- `TokenValidationParameters`: ValidateIssuer/Audience/Lifetime/IssuerSigningKey 全 true、ClockSkew=0
- 設定読込ヘルパ `JwtOptions.cs`

### 含まない
- ICurrentUser / HttpContextCurrentUser (A202)
- ProblemDetails 統合 (A203)
- ログインエンドポイント (A205)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `AGRI_GIS_JWT_SECRET` が NULL で起動 → 起動失敗、エラーメッセージに「AGRI_GIS_JWT_SECRET environment variable required (>= 32 bytes UTF-8)」
- [ ] 31 byte の secret で起動 → 同様に失敗
- [ ] 32 byte 以上の secret で正常起動
- [ ] HS256 で署名した JWT が `[Authorize]` 付きエンドポイントを通る
- [ ] 改竄 / 期限切れ JWT は 401
- [ ] `appsettings.Development.json` にダミーの Jwt 設定を入れて他環境変数だけで動作

## 影響ファイル
- `D:\proj\agri-gis\api\agri-gis-api.csproj` (NuGet)
- `D:\proj\agri-gis\api\Program.cs`
- `D:\proj\agri-gis\api\Auth\JwtOptions.cs` (新規)
- `D:\proj\agri-gis\api\appsettings.json`
- `D:\proj\agri-gis\api\appsettings.Development.json`

## 実装ノート
```csharp
// Auth/JwtOptions.cs
public sealed class JwtOptions
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenLifetimeMinutes { get; init; } = 480;
    public required byte[] SecretBytes { get; init; }

    public static JwtOptions Load(IConfiguration config)
    {
        var secret = Environment.GetEnvironmentVariable("AGRI_GIS_JWT_SECRET");
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException(
                "AGRI_GIS_JWT_SECRET environment variable required (>= 32 bytes UTF-8)");
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32)
            throw new InvalidOperationException(
                $"AGRI_GIS_JWT_SECRET must be at least 32 bytes UTF-8 (got {bytes.Length})");
        return new JwtOptions
        {
            Issuer   = config["Jwt:Issuer"]   ?? throw new InvalidOperationException("Jwt:Issuer missing"),
            Audience = config["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience missing"),
            AccessTokenLifetimeMinutes = int.Parse(config["Jwt:AccessTokenLifetimeMinutes"] ?? "480"),
            SecretBytes = bytes,
        };
    }
}

// Program.cs (抜粋)
var jwt = JwtOptions.Load(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.SecretBytes),
            ClockSkew = TimeSpan.Zero,
        };
    });
builder.Services.AddAuthorization();
```

注意点:
- ClockSkew=0 はテストで時刻ずれの影響を排除
- `ISigningKeyProvider` 抽象は作らない (採択案明示)

## テスト観点
- A504 (JwtValidationTests): 改竄 JWT で 401、期限切れで 401、iss/aud 不一致で 401
- A504 (InitialAdminSeedTests): secret 不足で起動失敗
