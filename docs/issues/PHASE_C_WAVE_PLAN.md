# agri-gis Phase C Wave 分割計画 (案 P)

`PHASE_C_ISSUES_INDEX.md` の 14 Issue を、Phase A の WA1〜WA5 / Phase B の WB0〜WB5 と同じ流儀で Wave 分割する。Phase B より小規模 (11.5 人日 / 14 Issue) であることと、クリティカルパスが「PoC → 配線 → 骨格 → 並列実装 → UI 統合 → 並列テスト → docs」の単線であることから、**5 Wave 構成 (WC0〜WC4)** を採用する。

## 0. 運用前提 (Phase A/B 踏襲)

- **branch-per-Wave + base=main 固定**: 各 Wave で `feature/phase-c-wc{N}-{slug}` ブランチを切る。Issue ごとの PR ではなく **Wave 単位の集約 PR を main へ `--no-ff` でマージ**。Phase B Wave 計画と同じ。
- **stacked PR pitfall 回避** (MEMORY.md): Wave PR は常に `base=main`。前 Wave がマージ済の main から fresh に切り出して開始。`base=feature/...` は禁止。
- **Wave 内 Issue 順**: Wave 内では Issue 番号順を推奨着手順とし、依存のない Issue は並列実装可能。Wave 内コミットは Issue ID prefix (`C102:`, `C103:` 等)。
- **Wave 完了 = main マージ + Wave 検証手順クリア**: 全テスト green + 受け入れ条件サマリを PR description に転記。
- **失敗時のロールバック**: Wave PR を revert すれば main に戻せる粒度を維持。Wave 内に巨大 Issue を入れない。
- **ラベル**: Wave 単位で `wave:WC0`〜`wave:WC4` を付与。Issue 既存ラベル (`phase:C` / `area:*` / `stretch` / `phase-c-prime-followup`) と併用。

## 1. Wave 一覧

| Wave | テーマ | 含む Issue | 工数 | 前提依存 | 並列実行可否 |
|---|---|---|---|---|---|
| **WC0** | 前提条件 PoC | C100 | 0.5d | なし | 単独 (Gate) |
| **WC1** | GDAL 配線 + コア骨格 | C101, C102 | 2.0d | WC0 完了 | Issue 間は直列 (C101→C102) |
| **WC2** | ドメイン部品の並列実装 | C103, C104, C105, C106, C301, C302 | 5.0d | WC1 完了 | **6 Issue 全て並列可** |
| **WC3** | UI 統合 (ImportWizardForm Step1) | C401 | 1.2d | WC2 完了 | 単独 |
| **WC4** | テスト群 + ドキュメント | C501, C502, C503, C504, C601 | 2.8d | WC3 完了 (C601 のみ C501-C503 待ち) | C501/C502/C504 並列 → C503 → C601 |
| | **合計** | **14 Issue** | **11.5d** | | |

クリティカルパス: WC0 (0.5d) → WC1 (2.0d) → WC2 ボトルネック (C103/C104 直列換算 1.5d) → WC3 (1.2d) → WC4 (C501→C503→C601 直列換算 2.3d) ≒ **7.5 営業日 + バッファ**。並列度を最大化すれば 8〜9 営業日で全 Wave マージ可能。

---

## 2. Wave 詳細

### WC0 — `Minimal` SKU 実機 PoC (Gate)

- **テーマ**: Phase C 全体の着手前提条件として `MaxRev.Gdal.WindowsRuntime.Minimal` SKU の `ESRI Shapefile` driver 含有を実機 PoC で確認する。`gdalinfo --formats` または最小 C# PoC で検証。
- **含む Issue**: C100
- **工数**: 0.5d
- **依存**: なし
- **並列**: 単独 (Gate Wave、後続全 Wave がブロックされる)
- **ブランチ**: `feature/phase-c-wc0-poc`
- **マージ順**: 1 番目
- **検証手順**:
  1. `tools/poc/GdalSkuCheck/` の PoC または `gdalinfo --formats` 出力が `ESRI Shapefile` を含むことを確認
  2. `docs/issues/PHASE_C_C100_POC_RESULT.md` に出力ログ + 配布サイズ (`Minimal` SKU 展開サイズ) を記録
  3. `go` / `no-go` 判断を PR description に明記。`no-go` の場合は Full SKU 切替 Issue を新規起票し WC1 を着手しない
  4. main にマージ後、後続 WC1 のキックオフ条件成立を MEMORY.md `orchestration_state.md` に追記

### WC1 — GDAL 配線 + `GdalLayerSource` 骨格

- **テーマ**: `windos-app.csproj` への GDAL NuGet 追加 / x64 固定 / `Program.Main` 先頭の `GdalBase.ConfigureAll()` を入れ、その上に `GdalLayerSource : ILayerSource` の骨格 (ctor 注入 + Dispose 連鎖) を据える。Phase C の「土台」Wave。
- **含む Issue**: C101 (0.5d), C102 (1.5d)
- **工数**: 2.0d
- **依存**: WC0 (C100 `go` 判定)
- **並列**: Wave 内 Issue 間は **直列** (C101 完了後に C102)。WC0 完了時点で main に C100 PoC 結果がマージ済のため `feature/phase-c-wc1-skeleton` を main から切る
- **ブランチ**: `feature/phase-c-wc1-skeleton`
- **マージ順**: 2 番目
- **検証手順**:
  1. `dotnet build -c Release` 成功、配布 zip サイズが C100 PoC ±10% 以内
  2. `Program.Main` 起動時に GDAL バナーログ 1 行出力 (`Gdal:ConfigureOnStartup=true` 時のみ)
  3. `windos-app.tests` が x64 ターゲットでビルド成功、`InternalsVisibleTo` が csproj に存在
  4. `GdalLayerSource` 骨格がコンパイル可能で `ILayerSource` 契約面を満たす (実体未実装でも OK)
  5. `dotnet test` 既存 Phase B テスト全 green を維持

### WC2 — ドメイン部品の並列実装

- **テーマ**: `GdalLayerSource` 内で呼ばれるドメイン部品 (Package / SRID / Encoding / Inference / Geometry) を一気に並列で実装する Wave。Phase B WB2 のドメイン拡張パターンと同パラダイム。
- **含む Issue**: C103 (1d), C104 (1d), C105 (0.5d), C106 (0.5d), C301 (1d), C302 (1d)
- **工数**: 5.0d
- **依存**: WC1 (C102 骨格マージ済)
- **並列**: **6 Issue 全て並列可**。Issue 一覧の依存表より:
  - C103/C104/C301/C302 → C102 のみに依存 (WC1 で解決済)
  - C105/C106 → 依存なし (純粋関数 + API 追加のみ)
- **ブランチ戦略**: Wave 内に**サブブランチを切らず 1 本の `feature/phase-c-wc2-domain-parts` で並走**。担当者が分かれる場合は Issue 単位で `git switch -c` してから `feature/phase-c-wc2-domain-parts` にマージし戻す内部運用とする (main に直接 PR は出さない)
- **マージ順**: 3 番目
- **検証手順**:
  1. `dotnet test` で `windos-app.tests` 既存 + Phase C 新規 unit すべて green (WC2 時点では `GdalLayerSourceTests` は WC4 待ちなので除外)
  2. `ShapefilePackage.OpenAsync` が `points_4326_cp932.zip` 等の最小手作り SHP zip を temp 展開できる (手動スモーク)
  3. `ISridDetector` Strategy が `OgrSridDetector` / `ManualSridDetector` の 2 実装で `SridDetectionResult` 4 値遷移を返す (手動スモーク)
  4. `GdalInferenceStrategy` が `OFTInteger/OFTReal/OFTString/OFTDate` の最小 SHP に対し `InferredField` を返す
  5. `SridConverter.RegisterWkt(99999, "<WKT>")` 後に `IsSupported(99999) == true` (REPL 手動確認可)
  6. `CpgFileParser.Parse("CP932")` 等の `[Theory]` ケースが green (C502 を待たずこの Wave で簡易ユニットを足してもよい)

### WC3 — `ImportWizardForm` Step1 拡張 (UI 統合)

- **テーマ**: WC2 で揃ったドメイン部品を `ImportWizardForm` Step1 に inline 配線。Shapefile ZIP 項目活性化 + `SridResolutionState` 4 値による Next 制御 + 検出文字コード / SRID の inline 表示。モーダル不使用。
- **含む Issue**: C401 (1.2d)
- **工数**: 1.2d
- **依存**: WC2 (C102/C103/C104/C301/C302/C105/C106 すべてマージ済)
- **並列**: 単独 (UI 統合は 1 Issue 集中)
- **ブランチ**: `feature/phase-c-wc3-importwizard`
- **マージ順**: 4 番目
- **検証手順**:
  1. WinForms 起動 → ImportWizard Step1 で Shapefile ZIP が活性、MIF/TAB は「Phase C' 対応予定」非活性表示
  2. zip ファイル選択 → 自動検出 → `DetectedEncoding` / `DetectedSrid` / `FieldCount` / `FeatureCount` が画面に inline 表示
  3. `.prj` 不在 zip で `SridResolutionState=FallbackToPrompt` 表示 → 手動 SRID 入力で `Next` 活性化
  4. `appsettings.json: Import:SridFallbackPolicy=Reject` で起動した場合は同 zip で `Next` 非活性
  5. `Import:SridFallbackPolicy=AssumeWgs84` で起動し投入実行 → `audit_log.meta_jsonb` に `{"srid_inferred":true}` が記録される (Phase B DB に対するスモーク)
  6. 文字コード ComboBox で `UTF-8` 上書き → OGR Open し直されて dbf 属性表示が変化
  7. Step2 / Step3 が Phase B 既存挙動のまま動作 (リグレッション確認)

### WC4 — テスト群 + ドキュメント

- **テーマ**: Phase C 実装の品質保証 Wave。OGR 依存テスト (C501) + 純粋関数テスト (C502) + ViewModel ヘッドレス (C504) を並列で実装、その後 E2E 10 万件 (C503) を回し、最後にドキュメント (C601) で締める。
- **含む Issue**: C501 (1d), C502 (0.5d), C503 (0.8d/`stretch`), C504 (0.5d), C601 (0.5d)
- **工数**: 2.8d (C503 stretch 含む)
- **依存**: WC3 (C401 マージ済) + WC2 の全部品
- **並列**:
  - **C501 / C502 / C504**: 完全並列 (依存衝突なし)
  - **C503**: C401 に依存、C501 のフィクスチャ確立後に着手することで合成 SHP 生成スクリプトが流用可
  - **C601**: C501-C503 結果が docs 入力 (性能特性) のため最後
- **ブランチ**: `feature/phase-c-wc4-tests-docs`
- **マージ順**: 5 番目 (最終)
- **検証手順**:
  1. `dotnet test --filter "Category!=Performance"` で全テスト green (C501/C502/C504 を含む)
  2. `dotnet test --filter "Category=Performance"` を **ローカル / 夜間ジョブ**で実行し C503 が完走、`PeakWorkingSet64 <= 2GB`
  3. C501 の `ICollectionFixture<GdalFixture>` 並列実行が破綻していない (テスト実行時間が `[Collection("Gdal")]` 単純化想定より顕著に長くないこと)
  4. C504 の `SridResolutionState` 4 値遷移 + `meta_json` 組み立てが Mock で assertion 通過
  5. `docs/layer-import.md` Phase C セクション + `docs/PHASE_C_INDEX.md` + `README.md` 機能行が最新で、C503 結果 (投入時間 + 最大 working set) が転記されている
  6. C503 が stretch 未消化の場合は docs に「性能特性は Phase C' で計測」と明記して Wave クローズ可

---

## 3. マージ順 (確定)

```
main
 ├─ WC0 (C100)                       — Gate / PoC 結果と go 判定
 ├─ WC1 (C101 → C102)                — GDAL 配線 + 骨格
 ├─ WC2 (C103 / C104 / C105 / C106 / C301 / C302 並列) — ドメイン部品
 ├─ WC3 (C401)                       — UI 統合
 └─ WC4 (C501 / C502 / C504 → C503 → C601) — 品質保証 + Docs
```

各 Wave 完了後に main へ `--no-ff` マージし、`docs/issues/PHASE_C_WAVE_STATE.md` (Phase B の Wave State 踏襲、運用中に作成) で「完了 Wave」「次 Wave キックオフ条件」を 1 行ずつ追記する。Phase B `review2_findings` のような途中レビューは Phase C では計画にないが、WC3 完了時点 (UI 統合が main にある状態) で実装リスクの残物を見直す軽量チェックポイントを置くことを推奨。

## 4. Wave 間の依存とリスク

- **WC0 → WC1 ゲート**: PoC で `ESRI Shapefile` が `Minimal` SKU に含まれない場合、WC1 以降は全停止して Full SKU 切替 Issue 起票。これにより 11 人日相当の手戻りを起動前に検知。
- **WC2 内並列の競合**: 6 Issue が同一ブランチで走るため、`Services/Import/` 配下の `csproj` `<ItemGroup>` 編集や `Program.cs` DI 登録での conflict が起こりやすい。Issue ごとに「自分が作るファイル一覧」を PR 前に共有し DI 登録は最後に集約する運用とする。
- **WC3 単独 Issue の重さ**: C401 は 1.2d だが UI + ViewModel 同時編集のため実工数ブレが大きい。Step1 inline 表示のスケッチを WC2 並走中に下書きしておく前倒しを許可。
- **WC4 stretch (C503) の扱い**: 10 万件 E2E は失敗しても Phase C 完了をブロックしない。C601 docs で「Phase C' で計測」と明記して Wave クローズしてよい (Issue 一覧の `stretch` ラベルと整合)。
- **C601 docs の Phase C'/D 申し送り**: Issue 一覧 `PHASE_C_ISSUES_INDEX.md` 末尾の 5+5 件を `phase-c-prime-followup` ラベルで Wave 完了時に起票する作業は **Wave 外の運用タスク**として扱い、WC4 マージと同日に実施する。

## 5. Phase A/B との対応

| Phase | Wave 数 | 工数 | Gate Wave |
|---|---|---|---|
| Phase A | WA1〜WA5 (5) | — | WA1 |
| Phase B | WB0〜WB5 (6) | — | WB0 |
| **Phase C** | **WC0〜WC4 (5)** | **11.5d** | **WC0 (PoC)** |

Phase C は Phase B より 1 Wave 少ない。理由は (a) API/DB 拡張ゼロで「DB マイグレ Wave」が不要、(b) WC2 でドメイン部品 6 個を 1 Wave に集約できるため。Phase A/B の WaN-1 (中間レビュー Wave) に相当するものは置かないが、WC3 完了時点で軽量チェックポイントを推奨 (上記 §3 参照)。

## 6. 完了条件 (Phase C 全 Wave)

1. WC0〜WC4 すべて main マージ済
2. `dotnet test --filter "Category!=Performance"` 全 green
3. `docs/layer-import.md` Phase C セクション公開、`docs/PHASE_C_INDEX.md` 存在
4. Phase C' 申し送り Issue 5 件 + Phase D 申し送り Issue 5 件が `phase-c-prime-followup` / `phase:D` ラベルで起票済
5. MEMORY.md `orchestration_state.md` に「Phase C 完了 / Phase C' 次着手候補」を追記

以上で Phase C は閉じ、Phase C' (MIF/TAB + 和歌山系 WKT 本体収録 + `IImportPackage` 抽象化) へ移行する。
