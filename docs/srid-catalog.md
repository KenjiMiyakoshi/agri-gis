# SridCatalog — ローカル CS の事前登録 (Phase C' C'4)

EPSG コードを持たないローカル座標系 (和歌山旧測地系等) の WKT を `appsettings.json` で事前登録し、TAB/MIF のインポート時に SRID 解決可能にする。

## 背景

Phase C で `SridConverter.RegisterWkt(int srid, string wkt)` API を公開済。しかし WKT 本体は未収録で、TAB の CoordSys が EPSG コードを持たない場合は `SridResolutionState.Unknown` で詰まる。

Phase C' で WKT 本体を `appsettings.json` 経由で収録し、起動時に一括 RegisterWkt する仕組みを整える。

## 採用方針

### appsettings.json スキーマ

```json
{
  "Import": {
    "SridCatalog": [
      {
        "Srid": 99001,
        "Name": "旧日本測地系 平面直角座標系 II 系 (Tokyo Datum)",
        "Wkt": "PROJCS[\"Tokyo Datum II\",GEOGCS[\"Tokyo\",DATUM[\"Tokyo\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",33],PARAMETER[\"central_meridian\",131],PARAMETER[\"scale_factor\",0.9999],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]",
        "Source": "国土地理院 旧日本測地系 (Bessel 1841 楕円体), GSI ガイドライン"
      },
      {
        "Srid": 99004,
        "Name": "旧日本測地系 平面直角座標系 IV 系 (Tokyo Datum, 和歌山)",
        "Wkt": "PROJCS[\"...\"]",
        "Source": "..."
      }
    ]
  }
}
```

### SridCatalogEntry DTO

```csharp
public sealed class SridCatalogEntry
{
    public int Srid { get; set; }
    public string Name { get; set; } = "";
    public string Wkt { get; set; } = "";
    public string Source { get; set; } = "";
}
```

`ImportOptions.SridCatalog: List<SridCatalogEntry>` に追加。

### SridCatalogBootstrapper

```csharp
public sealed class SridCatalogBootstrapper : IHostedService  // .NET 8 console 用は IStartable に変更
{
    private readonly IOptions<ImportOptions> _opts;
    private readonly ILogger<SridCatalogBootstrapper>? _logger;

    public void Bootstrap()
    {
        foreach (var entry in _opts.Value.SridCatalog ?? Array.Empty<SridCatalogEntry>())
        {
            try
            {
                SridConverter.RegisterWkt(entry.Srid, entry.Wkt);
                _logger?.LogInformation("[SridCatalog] registered SRID {Srid} ({Name})",
                    entry.Srid, entry.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("[SridCatalog] failed to register SRID {Srid}: {Message}",
                    entry.Srid, ex.Message);
            }
        }
    }
}
```

`Program.cs` で起動時に呼ぶ:

```csharp
sp.GetRequiredService<SridCatalogBootstrapper>().Bootstrap();
```

### SRID 範囲の割り当て

- **99001-99099**: 旧日本測地系 (Tokyo Datum) 平面直角座標系 I-XIX 系
- **99100-99199**: 自治体固有 CS (将来拡張)
- **99900-99999**: 個別検証用

EPSG レンジ (2000-3000、6000-7000 等) と衝突しないよう注意。

### WKT 出典

各 entry の `Source` フィールドに必ず出典 URL or 文献名を残す:
- 国土地理院 (https://www.gsi.go.jp/)
- proj DB (https://proj.org/usage/projections.html)
- GSI ガイドライン (`/maps/gisplan/`)

## TAB CoordSys 行との照合

TAB の CoordSys 行例:
```
CoordSys Earth Projection 8, 1000, "m", 131, 33, 0.9999, 0, 0 Bounds (...)
```

`OgrSridDetector` がこの行を WKT に変換 (OGR 内部関数経由) → `SridConverter` の既知 WKT 集合と照合 → 一致する SRID を返す。

照合は **文字列完全一致** ではなく、`OGRSpatialReference.IsSame()` 相当の数値比較 (proj 内部関数)。

## 受入条件

1. `appsettings.json` に旧日本測地系 II + IV の 2 件を WKT 本体込みで収録
2. 起動時に `SridCatalogBootstrapper.Bootstrap()` が呼ばれ、`SridConverter.IsSupported(99001)` == true
3. 和歌山 TAB ファイルを Step1 で選択 → `SridResolutionState.Detected` で SRID=99004 (もしくは合致する SRID)
4. Step3 投入 → feature_current に EPSG:4326 で書き込み (`ST_Transform(99004 → 4326)`)
5. `SridCatalogBootstrapperTests` で「未知 WKT」「不正 WKT」「2 重登録」のケース pass

## テスト

- `SridCatalogBootstrapperTests` (windos-app.tests)
  - 正常系: 1 件登録 → `SridConverter.IsSupported(99001)` == true
  - 不正 WKT: ログ警告 + 後続 entry の登録は継続
  - 重複 SRID: 上書きされる (後勝ち)
- 手動 E2E: 実 TAB ファイル投入 → feature_current の coords を psql で確認

## 関連

- `PHASE_C_PRIME_INDEX.md`
- `windos-app/Services/Import/SridConverter.cs` (`RegisterWkt` API)
- `windos-app/Services/Import/ImportOptions.cs` (`SridCatalog[]` 追加先)
