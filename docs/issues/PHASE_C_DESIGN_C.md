# Phase C Design — 案 C: ILayerSource 横展開最小

> Phase B で確立した `ILayerSource` パターンを**そのまま**横展開する案。新クラスは原則 `GdalLayerSource` 1 本のみ、新規ヘルパは行内 private メソッドで吸収し、SridConverter / Chunker / 契約テスト基盤は既存資産を一切いじらず追加だけで通す。「Phase B 申し送りを忠実に最小コストで履行する」リファレンス案。

## 0. コンセプト

- **Phase B 抽象の素直な横展開**: `ILayerSource` の契約 (`SourceFormat / SourceSrid / InferSchemaAsync / ReadFeaturesAsync`) を厳守し、SHP/MIF/TAB を `GdalLayerSource` 1 クラスに集約。形式分岐は **ctor 引数 `sourceFormat`** で内部に閉じ、外側からは GeoJsonLayerSource / CsvLayerSource と同じ顔をする
- **「足し算だけ」原則**: 既存 `SridConverter` / `Chunker` / `IInferenceStrategy` の契約には触らない。`SridConverter` には WKT を **追加するだけ** で API 変更なし、`Chunker` は OGR 由来 `IAsyncEnumerable<GeoJsonFeature>` をそのまま受け取る
- **UI 最小改変**: `ImportWizardForm` Step1 の「Phase C 対応予定」ラベルを削るだけ。新 options カラム / 新 ComboBox / 文字コード選択 UI などは **Phase D 以降に持ち越し**
- **文字コードは CP932 固定** (日本市場前提)。`.cpg` 解釈 / `CharSet` 句解析 / ユーザ選択 UI は導入しない。Phase D で拡張可能なよう `EncodingResolver` 抽象だけは将来の差し込み口を残す
- **SRID 未検出時は 4326 黙認**: Step1 で警告バナーを出すが投入は止めない。fail-fast/手動指定 UI は導入しない (UI 改変を避ける方針と整合)
- **ローカル SRS は WKT ハードコード 3〜5 件のみ**: 和歌山測地系 (旧日本測地系系) + 関連する数件を `SridConverter.CreateCoordinateSystem` の switch に直書き。動的登録 API / proj 文字列対応は Phase D へ
- **`BulkInsertMaxCount` 据え置き**: Phase B 申し送り通り 5000 のまま。GDAL 実測後の引き上げ判断は Phase D へ
- **テストは fixture 1〜2 ファイルで契約検証**: `GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>` を 1 ファイル追加し、SHP/TAB 各 1 fixture (うち 1 件を和歌山系) で契約一貫性を確認。MIF は契約テストを通す最小サンプル 1 件のみ

「Phase B が完璧なら Phase C は 1 クラス足すだけで終わる」を立証する案。

## 1. アーキテクチャ全体像

```
ImportWizardForm.Step1
   └─ SourceFormat=ComboBox(geojson|csv|shapefile|mif|tab)
        └─ Factory: ImportSourceFactory.Create(format, path)
              ├─ geojson => GeoJsonLayerSource (既存)
              ├─ csv     => CsvLayerSource (既存)
              └─ shp/mif/tab => new GdalLayerSource(sourceFormat: format, path)
                    ├─ Open(): driverName を format から決定、ENCODING=CP932 で OGR Open
                    ├─ InferSchemaAsync(): GdalInferenceStrategy.Infer(layer.GetLayerDefn())
                    └─ ReadFeaturesAsync(targetSrid): layer.GetNextFeature ループ → Geometry.ExportToJson()
                          → GeoJsonFeature (4326 化は OGR CoordinateTransformation で実施)

Step2 SchemaGrid / Step3 Chunker+Bulk 投入は Phase B 経路をそのまま流用
```

Phase B との差分は `GdalLayerSource` + `GdalInferenceStrategy` + `ImportSourceFactory` の switch に 3 行 + `SridConverter` の switch に WKT 数件、計 4 点のみ。

## 2. 設計論点への回答 (Plan §設計論点 15 件)

| # | 論点 | 案 C の選択 | 理由 |
|---|---|---|---|
| 1 | zip 展開責務 | **(b) `/vsizip/` 仮想 FS 直接利用** | 専用ヘルパクラス化を回避。`GdalLayerSource` 内 `BuildVsiPath(path)` private メソッドで `.zip` 拡張子なら `/vsizip/{path}/{innerName}` を組み立てる |
| 2 | 文字コード判定 | **(a) CP932 固定** | UI 拡張なし。`Ogr.Open(path, new[] { "ENCODING=CP932" })` の固定オプション |
| 3 | SRID 検出失敗 | **(a) 4326 黙認** | Step1 で警告バナー表示のみ。UI フロー停止なし |
| 4 | FieldDefn マッピング | **(a) OFT 型に完全準拠** | 実値サンプリングは Phase B GeoJsonInferenceStrategy が型推論済の前提。GDAL は型情報が明示的なので二度推論不要 |
| 5 | 大規模メモリ管理 | **(a) `GetNextFeature` ループを `IAsyncEnumerable` で逐次 yield** | Phase B Chunker (1000 件束ね) にそのまま乗る。SQL ページング不要 |
| 6 | GDAL DLL 配布 | **(a) `WindowsRuntime.Minimal` 同梱** | ClickOnce / 別途インストーラは Phase D 課題。Minimal で MITAB 含むことを C1 で確認 |
| 7 | Step1 UI 解禁 | **(a) ラベル削除 + 全項目活性化** | Feature Flag / admin 設定は Phase D へ |
| 8 | ジオメトリ型混在 | **(c) feature 単位で MultiPolygon 固定** | `feature_current.geom geometry(Geometry, 3857)` 制約で DB 側は混在許容。Polygon → MultiPolygon 昇格を 1 関数で済ませる |
| 9 | `BulkInsertMaxCount` 上限解除 | **(a) Phase C スコープ外** | 5000 据え置き |
| 10 | ローカル SRS サポート | **(a) WKT ハードコード 3〜5 件** | `SridConverter.CreateCoordinateSystem` の switch に和歌山測地系 + 旧日本測地系 + 数件を直書き。動的登録 API 追加なし |
| 11 | SHP vs TAB 文字コード差 | **(a) 両形式同じ `ENCODING` オプション** | 形式別 EncodingResolver は導入しない。CP932 固定 |
| 12 | 64bit/32bit | **(c) WinForms 全体を x64 固定 (`<PlatformTarget>x64</PlatformTarget>`)** | `MaxRev.Gdal.WindowsRuntime.Minimal` が x64 のみ提供のため最も単純 |
| 13 | エラーハンドリング | **(a) skip + WARN ログ + Step3 完了ダイアログにスキップ件数表示** | fail-fast / skip 上限は Phase D へ。Phase C は「読めるものは読む」 |
| 14 | Geometry → GeoJSON 変換 | **(a) `Geometry.ExportToJson()` 文字列経由** | Phase B GeoJsonFeature が `JsonElement` 保持なので `JsonDocument.Parse` で 1 段挟むだけ。NTS 依存追加なし |
| 15 | `GdalBase.ConfigureAll()` 位置 | **(a) `Program.Main` 先頭** | テスト並列実行は xUnit `[Collection("Gdal")]` で直列化。`Lazy<>` 化のメンテ複雑性回避 |

15 論点全てで「**最小コスト / 既存契約不変 / Phase D へ持ち越し可**」の選択肢を採用。

## 3. 主要コンポーネント詳細

### 3.1 `GdalLayerSource : ILayerSource` (Phase B 踏襲ポイント)

Phase B `GeoJsonLayerSource` の形を**そのまま**写経する。

```csharp
public sealed class GdalLayerSource : ILayerSource, IAsyncDisposable
{
    private readonly string _path;
    private readonly string _sourceFormat;  // "shapefile" | "mif" | "tab"
    private DataSource? _ds;
    private Layer? _layer;
    private int? _sourceSrid;

    public GdalLayerSource(string sourceFormat, string path) { _sourceFormat = sourceFormat; _path = path; }

    public string SourceFormat => _sourceFormat;
    public int? SourceSrid => _sourceSrid;  // Open 後に確定

    public async Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct)
    {
        EnsureOpen();
        return GdalInferenceStrategy.Infer(_layer!.GetLayerDefn());
    }

    public async IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(
        int targetSrid, [EnumeratorCancellation] CancellationToken ct)
    {
        if (targetSrid != 4326) throw new NotSupportedException("only 4326 target supported");
        EnsureOpen();
        // (Open 時に SourceSrid を確定済。4326 でなければ OGR CoordinateTransformation を Layer に SetSpatialFilter+TransformTo 経路)
        Feature? f;
        while ((f = _layer!.GetNextFeature()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            using (f)
            {
                var geom = f.GetGeometryRef();
                if (geom is null) continue;
                if (_sourceSrid is int src && src != 4326) geom.TransformTo(Wgs84());
                var json = geom.ExportToJson(null);
                var jsonElem = JsonDocument.Parse(json).RootElement.Clone();
                var props = ExtractProperties(f);
                yield return new GeoJsonFeature(jsonElem, props);
            }
        }
    }

    private void EnsureOpen() { /* driverName 選択 + Ogr.Open(path, new[]{"ENCODING=CP932"}) + SRID 解決 */ }
}
```

Phase B `GeoJsonLayerSource` と比べて足したのは「OGR Open / driverName 切替 / TransformTo / ExportToJson 経由」のみ。**契約は完全に同一**。

### 3.2 `GdalInferenceStrategy` (Phase B `IInferenceStrategy` 流儀)

```csharp
public static class GdalInferenceStrategy
{
    public static IReadOnlyList<InferredField> Infer(FeatureDefn defn)
    {
        var list = new List<InferredField>();
        for (int i = 0; i < defn.GetFieldCount(); i++)
        {
            var fd = defn.GetFieldDefn(i);
            list.Add(new InferredField(
                Name: fd.GetName(),
                JsonType: MapOft(fd.GetFieldType())));  // OFTInteger=>integer, Real=>number, ...
        }
        return list;
    }
}
```

純粋関数 (引数 `FeatureDefn` のみ、副作用なし) で Phase B 既存 strategy と統一。

### 3.3 `SridConverter` 拡張 (差分 数行)

`CreateCoordinateSystem` switch に和歌山測地系等を**追加するだけ**。Public API は不変。

```csharp
// Phase C 追加分:
30169 => _csFactory.CreateFromWkt(/* JGD2000 平面直角 IX系 (近畿) WKT */),
30179 => _csFactory.CreateFromWkt(/* JGD2011 平面直角 IX系 WKT */),
// 和歌山旧測地系 (旧日本測地系) など EPSG 未登録のものは予約 SRID (例: 990001) を割り当て
990001 => _csFactory.CreateFromWkt(/* 旧日本測地系 ベース 平面直角 IX系 WKT */),
```

`IsSupported(int srid)` は既存実装そのままで判定可。

### 3.4 `ImportSourceFactory` (1 メソッドの新規ヘルパ)

```csharp
public static class ImportSourceFactory
{
    public static ILayerSource Create(string format, string path) => format switch
    {
        "geojson" => new GeoJsonLayerSource(path),
        "csv" => new CsvLayerSource(path, /* options */),
        "shapefile" or "mif" or "tab" => new GdalLayerSource(format, path),
        _ => throw new NotSupportedException($"unknown format: {format}"),
    };
}
```

`ImportWizardForm` Step1 は ComboBox SelectedValue を文字列で受け取り、このファクトリへ流すだけ。

### 3.5 `ImportWizardForm` Step1 改修 (最小)

- ComboBox 項目の表示文字列で「(Phase C 対応予定)」サフィックスを削除
- ファイル選択ダイアログのフィルタに `*.zip` (SHP), `*.mif;*.mid` (MIF), `*.zip;*.tab` (TAB) を追加
- SRID 未検出時の警告ラベル (`lblSridWarning.Visible=true`) を 1 行追加
- それ以外の Step1 〜 Step3 配線は無変更

### 3.6 `windos-app.csproj` 改修

```xml
<PropertyGroup>
  <PlatformTarget>x64</PlatformTarget>  <!-- 論点 12 -->
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MaxRev.Gdal.Core" Version="..." />
  <PackageReference Include="MaxRev.Gdal.WindowsRuntime.Minimal" Version="..." />
</ItemGroup>
```

`Program.Main` 先頭で `GdalBase.ConfigureAll();` を 1 行呼ぶ (論点 15)。

### 3.7 テスト (Phase B 契約テスト基盤に乗るだけ)

```csharp
public class GdalLayerSourceTests : ILayerSourceContractTests<GdalLayerSource>
{
    protected override GdalLayerSource CreateSource(string fixturePath)
        => new GdalLayerSource(InferFormatFromExt(fixturePath), fixturePath);
}
```

fixture:
- `windos-app.tests/Fixtures/import/shp_minimal.zip` (Polygon × 3, CP932 属性)
- `windos-app.tests/Fixtures/import/tab_wakayama.zip` (和歌山旧測地系 × 1 sample) — 論点 10 + 実機検証兼ねる
- `windos-app.tests/Fixtures/import/mif_minimal.mif/.mid` (Point × 2)

xUnit `[Collection("Gdal")]` で GDAL static 初期化を直列化。

## 4. WBS 対応 (Plan C1〜C11 への射影)

| Plan WBS | 案 C での扱い | コメント |
|---|---|---|
| C1 csproj + GdalBase.ConfigureAll | `<PlatformTarget>x64</PlatformTarget>` 追加 + Program.Main 先頭呼び出し | 論点 6, 12, 15 |
| C2 GdalLayerSource 骨格 | 上記 3.1 のまま実装 | 形式分岐は ctor 引数で吸収 |
| C3 zip 展開 | **`/vsizip/` 直接利用、専用クラス化なし** | `BuildVsiPath` private メソッド数行で吸収 (論点 1) |
| C4 GdalInferenceStrategy | 純粋関数 1 メソッド (上記 3.2) | 論点 4 |
| C5 Geometry → GeoJSON | `ExportToJson` + `JsonDocument.Parse` で 2 行 | 論点 14 |
| C6 SRID 検出 | 未検出時 4326 黙認 + 警告バナー | 論点 3 |
| C7 ImportWizardForm Step1 | ラベル削除 + ファイル拡張子フィルタ + 警告ラベル 1 行追加 | 論点 7 |
| C8 SridConverter 拡張 | switch に WKT 3〜5 件追加のみ | 論点 10 |
| C9 契約テスト | `GdalLayerSourceTests` 1 ファイル + fixture 3 件 | Phase B `ILayerSourceContractTests<T>` 自動適用 |
| C10 和歌山系実機検証 | C9 fixture `tab_wakayama.zip` で兼ねる | 別工程化しない |
| C11 ドキュメント | `docs/layer-import.md` に 1 セクション追加 | PHASE_C_INDEX は記入のみ |

Phase B 申し送り通り 5〜7 人日に収束する想定 (Plan 9〜12 人日見積もりより圧縮できるのは「専用ヘルパクラス化を避けた」「UI 拡張を最小に絞った」「動的 SRS 登録を採らない」3 点の合計効果)。

## 5. Phase B 踏襲・最小拡張のマッピング

| Phase B 資産 | Phase C 案 C での扱い | 拡張量 |
|---|---|---|
| `ILayerSource` 契約 | **完全踏襲** | 0 行変更 |
| `IInferenceStrategy` 純粋関数化 | `GdalInferenceStrategy` を同流儀で追加 | 新規 1 ファイル |
| `SridConverter` (4326/4612/6668/3857 キャッシュ) | switch に WKT 数件追加 | +5〜15 行 |
| `Chunker` (1000 件チャンク) | OGR 由来 `IAsyncEnumerable<GeoJsonFeature>` を素通し | 0 行変更 |
| `BulkInsert` 経路 (`POST /api/admin/layers/{id}/features/bulk`) | API 完全不変 | 0 行変更 |
| `GeoJsonLayerSource` / `CsvLayerSource` | 形を `GdalLayerSource` で写経 | 0 行変更 |
| `ImportWizardForm` Step1 | ComboBox ラベル文字列 + ファイルフィルタ + 警告ラベル | 数十行 |
| `ImportWizardViewModel` | options プロパティ追加なし | 0 行変更 |
| `ILayerSourceContractTests<T>` ジェネリック契約 | `GdalLayerSourceTests` を継承する 1 ファイル追加 | 新規 1 ファイル |
| DB スキーマ (`layers`/`audit_log`/`fn_feature_insert` 等) | 完全不変 | 0 行変更 |
| `appsettings.json` (`BulkInsert.MaxCountPerChunk=5000`) | 据え置き | 0 行変更 |

「Phase B が抽象化を正しく終わらせていれば、Phase C は **新規 3 ファイル + switch 加筆 + UI ラベル数行**」で終わる、を立証する案。

## 6. リスクと留意点

- **`/vsizip/` 経路の OGR 挙動安定性** (論点 1): zip 内に複数 SHP セットがある場合 OGR は最初に見つけたものを開く。Phase C 案 C では「zip 内 SHP は 1 セット」と Step1 でアサーション、複数セット zip は将来課題
- **CP932 固定の市場リスク** (論点 2): 海外データ取り込みは UTF-8 で動かないが、Phase C 顧客は日本市場前提なので許容。Phase D で `EncodingResolver` 抽象を切り出す余地は残す
- **SRID 4326 黙認の精度劣化** (論点 3): .prj 無しの SHP を世界座標として読むとずれるが、Step1 警告で気付ける。fail-fast にしない方が UI 単純化に効く
- **MITAB 和歌山旧測地系の WKT 入手** (論点 10): JGD2000/2011 平面直角 IX 系は EPSG 登録済 (30169/30179)。**旧**日本測地系の WKT は手動構築が必要 → C8 で 1 人日見込み。失敗したら旧測地系のみ Phase D 送り、案 C 本体は変更なしで成立
- **GDAL ネイティブ static 初期化の並列テスト汚染** (論点 15): xUnit `[Collection("Gdal")]` で直列化。`Program.Main` 先頭呼び出しは WinForms 起動時のみで、テストは `GdalBase.ConfigureAll()` を 1 度だけ collection fixture で呼ぶ

## 7. Phase D 申し送り

- `BulkInsertMaxCount` 上限解除 (GDAL 実測値ベースで 50000 まで引き上げ判定)
- 文字コード判定の拡張 (`.cpg` 解釈 + `CharSet` 句パース + Step1 ComboBox UI)
- 動的 SRS 登録 API (`SridConverter.RegisterWkt` / `RegisterProj`)
- zip 内複数 SHP セットの選択 UI
- SRID 未検出時の手動指定 UI (Step1 ComboBox)
- ClickOnce / MSIX による GDAL ネイティブ差分配布
- skip 上限 / fail-fast 切替の設定値追加

これらは全て案 C のコア (`GdalLayerSource` + `SridConverter` + Step1 UI) を**変更せず**追加できる差分。Phase D での拡張余地が線形に効くのが案 C の特長。

## 8. まとめ

案 C は「Phase B 抽象化の成果をそのまま延長線で享受する」リファレンス実装。新規コードは `GdalLayerSource` + `GdalInferenceStrategy` + `ImportSourceFactory` の 3 ファイル + `SridConverter` switch 加筆 + Step1 UI 数行のみ。Phase B 契約 (`ILayerSource` / `IInferenceStrategy` / `SridConverter` public API / `Chunker` / API) は **1 行も変更しない**。Phase D の拡張余地 (動的 SRS 登録 / 文字コード UI / 上限解除) は全て案 C の上に純粋な追加で乗る。Plan §設計論点 15 件全てで「最小コスト・既存契約不変・Phase D 持ち越し可」の選択肢を採ったため、レビュー段階で「攻めるべきは案 A・守るべきは案 C」の対立軸が形成できる位置付け。
