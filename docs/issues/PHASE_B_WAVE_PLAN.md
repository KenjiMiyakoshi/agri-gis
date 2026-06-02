# Phase B Wave 分割計画 (案 P / 25 Issue / 19.5d)

`PHASE_B_ISSUES_INDEX.md` の 25 Issue を 6 Wave に分割。Phase A `WA1〜WA5` の運用 (branch-per-Wave + `base=main` 固定 + `merge --no-ff`) を踏襲。`stacked_pr_pitfall` (MEMORY.md) 遵守として、Issue 単位 PR は **全て `base=main`**、Wave 統合 PR も `base=main`。

## サマリ

| Wave | テーマ | Issue 数 | 工数 | 前依存 | 並列度 |
|---|---|---|---|---|---|
| WB0 | 性能スパイク (リスク先出し) | 1 | 0.5d | — | 単独 |
| WB1 | DB 土台 (layers/関数/job) | 4 | 2.5d | WB0 | B101 後 B102/B103/B104 並列 |
| WB2 | API 土台 (JsonOpts+CRUD+雛形) | 3 | 2.5d | WB1 | 直列 (B201→B202→B205) |
| WB3 | API バルク + WinForms 共通基盤 | 4 | 3.5d | WB2 | B203/B204/B401/B502 並列可 |
| WB4 | WinForms Source/Inference/UI (本体) | 8 | 8.0d | WB3 | 3 サブ並列 |
| WB5 | Test + Docs (品質固め) | 5 | 2.5d | WB4 | B501/B503/B504/B505 並列 → B506 → Docs |

合計 25 Issue / 19.5d。クリティカルパス ≈ WB0(0.5) + WB1(1.5) + WB2(2.5) + WB3(1.5) + WB4(4.0) + WB5(2.0) = **12.0d** (B101→B102→B201→B202→B203→B407→B408→B501→B601→B602)。

---

## WB0: 性能スパイク (0.5d / 1 Issue)

**含む**: B506 (5000 件投入スパイク) `stretch`

**狙い**: Plan で「最初の Issue は B506 推奨」とした実装リスク 4 (chunk サイズ既定値の根拠) を先に潰す。B203/B204 の `ChunkDefaultSize=1000` を実測根拠付きで確定し、後工程の手戻りをゼロにする。

**前 Wave 依存**: なし (DB/API 完成前に WinForms 側で純粋関数 + ローカル PostgreSQL or DB fixture のみで計測。本実装着手前のスパイクとして独立)。

**並列**: 単独 (本人 1 名 0.5d)。

**マージ順**: `feature/WB0` ブランチで B506 を実装 → `base=main` で PR → main へ `merge --no-ff`。

**検証**: `dotnet test --filter Category=Performance` で 5000 件投入時間 + GC ログ出力、結果値を `docs/layer-import.md` の下書きに転記 (WB5 で本反映)。所要 30s 超なら WB3 で `ChunkDefaultSize=500` を採用。

---

## WB1: DB 土台 (2.5d / 4 Issue)

**含む**: B101 / B102 / B103 / B104

**狙い**: `layers` 拡張 + `fn_layer_create` / `fn_layer_delete` + `layer_import_job` の DDL/関数のみ。API/WinForms に影響しない閉じた変更。

**前 Wave 依存**: WB0 (スパイク結果は DB 設計には影響しない。論理的には独立だが、Wave 順序として WB0 後)。

**並列**: B101 を先行マージ → B102/B103/B104 を並列 PR 化可 (3 並列、各 0.5d)。`db/migration/0B0x_*.sql` のファイルが分離されていて衝突なし。

**マージ順**:
1. B101 (`feature/B101-layers-extend`) → main
2. B102 / B103 / B104 を並列 PR → 上から順に main へ `merge --no-ff`
3. Wave 統合タグ `phase-b/WB1`

**検証**:
- `psql -f db/migration/0B0[1-4]_*.sql` を新規 DB に 2 回連続適用してエラーなし (冪等性)
- `SELECT fn_layer_create(...)` で `layers` + `layer_schema_version` + `audit_log` の 3 行が入る
- `SELECT fn_layer_delete(...)` で `layers.deleted_at` が立ち `feature_current` が不変
- 既存 A506 PUT /schema が green (回帰なし)

---

## WB2: API 土台 (2.5d / 3 Issue)

**含む**: B201 `negotiable-debt:H2` / B202 / B205

**狙い**: `JsonOpts` (H2 解消) + DTO + `MapGroup` 雛形 → CRUD 5 endpoint → `FeatureEndpoints` の `deleted_at IS NULL` 修正 (案 C 致命 3 対応)。

**前 Wave 依存**: WB1 (B102/B103 関数を呼ぶ)。

**並列**: 直列。B201 (雛形 + JsonOpts 集約) が B202 の前提、B205 は B103 後で独立だが `FeatureEndpoints.cs` の編集が B202 と同ファイル近辺なので衝突回避のため WB2 内で順次。

**マージ順**:
1. B201 → main (`api/Json/JsonOpts.cs` 新規 + 既存 grep 0 件まで置換 + `LayerAdminDto` 系)
2. B202 → main (CRUD 5 endpoint、A506 既存テスト green 確認)
3. B205 → main (`FeatureEndpoints` SQL に `l.deleted_at IS NULL` 追記)
4. Wave 統合タグ `phase-b/WB2`

**検証**:
- `dotnet test api.tests --filter FullyQualifiedName~AdminLayers` 基本 GET/POST/DELETE が 200/201/204
- `Grep "JsonSerializerOptions\(\)" api/` で 0 件 (H2 解消の grep 確認)
- 既存 A505/A506 全 green (回帰なし)
- B205: 論理削除 layer に紐づく feature が GET /api/features で見えない

---

## WB3: API バルク + WinForms 共通基盤 (3.5d / 4 Issue)

**含む**: B203 / B204 / B401 / B502

**狙い**: バルク投入 + import-job 状態管理 + 409/413 + WinForms NuGet 解決 + 論理削除回帰テスト。WB4 の WinForms 本体着手に必要な前提を全て整える。

**前 Wave 依存**: WB2 (B201/B202/B205 完了済)。

**並列**: 4 並列可。
- B203 (`api/Endpoints/AdminLayersEndpoints.cs` の bulk endpoint)
- B204 (同ファイルの import-jobs 3 endpoint)
- B401 (`windos-app.csproj` + `ApiClient.cs` 追記)
- B502 (`api.tests/FeatureEndpointsDeletedAtRegressionTests.cs` 新規)

B203 と B204 は同じ `AdminLayersEndpointsEndpoints.cs` を触るためファイル衝突懸念あるが、`MapPost("/.../features:bulk", ...)` と `MapPost("/.../import-jobs", ...)` の追加位置が異なれば手動マージで衝突回避可。慎重を期すなら B203→B204 直列、B401/B502 はその裏で並列。

**マージ順**:
1. B203 → main
2. B204 → main (B203 とのマージ衝突をこのタイミングで解消)
3. B401 / B502 を並列 PR → main (B203/B204 後にレビュー)
4. Wave 統合タグ `phase-b/WB3`

**検証**:
- `Count>5000` で 413 ProblemDetails (境界テスト)
- `status='running'` 中の start 二重呼びで 409
- `dotnet restore windos-app/windos-app.csproj` 成功 (NuGet 解決)
- `IApiClient` の新メソッド群がコンパイル通過
- B502 single test green

---

## WB4: WinForms Source/Inference/UI (8.0d / 8 Issue)

**含む**: B402 / B403 / B404 / B405 `negotiable-debt:H4` / B406 / B407 / B408

**狙い**: Phase B の本体。`ILayerSource` 系 + `IInferenceStrategy` + `SchemaGrid` + `LayerAdminForm` + `ImportWizardViewModel` + `ImportWizardForm`。

**前 Wave 依存**: WB3 (B401 NuGet + API endpoint 完成)。

**並列**: 3 サブ並列で組む。

```
サブ S1 (Source 系):    B402 → B404
サブ S2 (Schema 系):    B403 → B405
サブ S3 (UI 系):        B406
   ↓ S1/S2/S3 合流
ViewModel:              B407 (B402+B403 後)
   ↓
Form:                   B408 (B404+B405+B406+B407 後)
```

ファイル衝突観点:
- S1: `Services/ILayerSource.cs`, `GeoJsonLayerSource.cs`, `CsvLayerSource.cs`, `SridConverter.cs`, `Chunker.cs`
- S2: `Services/IInferenceStrategy.cs`, `GeoJsonInferenceStrategy.cs`, `CsvInferenceStrategy.cs`, `Controls/SchemaGrid.cs`, `Controls/AttributeEditorControl.cs`
- S3: `Forms/LayerAdminForm.cs`, `Forms/MainForm.cs` (1 行追加)

各サブのファイル集合は重ならず、3 サブを最大 3 名で並列実行可。1 名で進める場合は `B402 → B403 → B406 (並列性のためここで切替) → B404 → B405 → B407 → B408` の直列順。

**マージ順** (Wave 内):
1. B402 / B403 / B406 (並列 PR、各 1.0d)
2. B404 / B405 (並列 PR、各 0.5-1.0d。B402/B403 後)
3. B407 (1.0d、B402+B403 後)
4. B408 (2.0d、B404-B407 完了後)
5. Wave 統合タグ `phase-b/WB4`

**検証**:
- `dotnet build windos-app` green
- `LayerAdminForm.Show()` 起動、ToolStrip [新規/編集/削除] 表示
- ImportWizard 3 ステップ通過 (GeoJSON サンプル投入)、CSV は `ProjNet` 4326 化結果一致
- `AttributeEditorControl` が `IFeatureSaveCoordinator` 経由で動作 (H4 解消、ParentForm キャスト grep 0 件)
- admin 以外で MainForm メニュー Visible=false
- 1 万件オーダーのキャンセル時 `failed` finalize + DELETE 確認ダイアログ

---

## WB5: Test + Docs (2.5d / 5 Issue)

**含む**: B501 / B503 / B504 / B505 / B601 / B602

(B502 と B506 は前倒し済なので Wave 数の整合上 5 Issue + Docs 2)

**狙い**: 認可マトリクス + 契約テスト + 純粋関数 [Theory] + ViewModel ヘッドレス + Docs 仕上げ。

**前 Wave 依存**: WB4 (B408 まで完了)。

**並列**: B501/B503/B504/B505 を並列 PR (各 0.5-1.0d、ファイル集合が `api.tests/AdminLayers*` と `windos-app.tests/Services/*` `ViewModels/*` で分離) → 全 green 確認後 B601 → B602。

**マージ順**:
1. B501 / B503 / B504 / B505 を並列 PR → main
2. B601 (`docs/layer-import.md` 新規、WB0/B506 の実測値反映) → main
3. B602 (`docs/auth.md` 追記 + `docs/PHASE_B_INDEX.md`) → main
4. Wave 統合タグ `phase-b/WB5` = `phase-b/complete`

**検証**:
- `dotnet test` 全体 green (api.tests + windos-app.tests)
- `dotnet test --filter Category!=Performance` CI green、Performance 単体実行も再現
- 認可マトリクス 15 ケース全カバー、audit_log に geom 非含 (C2 回帰)
- B503 で `GeoJsonLayerSourceTests` / `CsvLayerSourceTests` が同じ抽象を継承し全契約 green
- B505 で `Application.Run` 不要、`Mock<IApiClient>` で N 回呼出検証
- `docs/PHASE_B_INDEX.md` から全 Issue/Wave へリンク到達

---

## 統合 PR と Wave マージ運用

Phase A `WA1-5 merge --no-ff` 踏襲:

- **Issue 単位 PR**: `base=main`、`feature/B<NNN>-<slug>` ブランチ。マージは Squash (Phase A と同じ)。
- **Wave 統合タグ**: 各 Wave 完了時に `git tag phase-b/WB<n>` を main に打つ (Phase A の `phase-a/WA<n>` 同型)。
- **`base=main` 固定**: Issue PR を Wave ブランチ base にすると `stacked_pr_pitfall` 再発するので絶対回避。Wave 内の依存は **作業順** で表現し、PR 上では同じ main を base にする。
- **Wave 完了の判定条件**: (1) Wave 内全 Issue PR が main にマージ済、(2) main で `dotnet test` 全 green、(3) Wave テーマの受け入れ条件 (上記「検証」) を満たす。

## Wave 完了時の検証チェックリスト (全 Wave 共通)

各 Wave 完了時に main で以下を実行:

1. `git checkout main && git pull`
2. `dotnet build` (warning も観察)
3. `dotnet test` (api.tests + windos-app.tests、Performance は Trait 除外)
4. `psql -f db/migration/000_reset.sql -f db/migration/*.sql` を新規 DB に適用してエラーなし
5. Wave 固有検証 (上記各 Wave 「検証」項目)
6. `git tag phase-b/WB<n>` + `git push --tags`
7. `docs/PHASE_B_INDEX.md` (WB5 で作成後) に Wave 完了マーク

## 受け入れ条件 (Phase B 全体)

WB5 完了時に以下が満たされること:

- 25 Issue 全て main にマージ済
- クリティカルパス 12.0d 以内で完了 (Plan 14-18d の中央値)
- Review② 負債 H2 (B201) / H4 (B405) 解消 (grep 0 件確認)
- `stretch` B506 の実測値が `docs/layer-import.md` に反映
- `phase-b/WB0` 〜 `phase-b/WB5` の 6 タグが main 上に存在
- H5 (MainForm god class) は Phase C 申し送り (`PHASE_B_DESIGN_P.md` §9) として明記済

---

推奨着手順 (1 名直列の場合): **WB0 → WB1 (B101→B102/103/104) → WB2 (B201→B202→B205) → WB3 (B203→B204→B401/B502) → WB4 (B402→B403→B406→B404→B405→B407→B408) → WB5 (B501-505→B601→B602)**。

複数名並列の場合は WB1 で 3 並列、WB3 で 4 並列、WB4 で 3 サブ並列、WB5 で 4 並列がそれぞれ可能。
