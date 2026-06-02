# Phase C C100 / WC0 — Minimal SKU 実機 PoC 結果

**判定: 🟢 GO** — WC1 (C101/C102) 着手可。

Phase C 着手前提条件である `MaxRev.Gdal.WindowsRuntime.Minimal` SKU の Shapefile driver 含有を実機 PoC で検証した結果、必須項目はすべて満たし、副次的に Phase C' / Phase D でも Full SKU 切替が不要であることが確定した。

## PoC 構成

- 場所: `tools/poc/GdalSkuCheck/`
- ターゲット: .NET 8 / x64 (`<PlatformTarget>x64</PlatformTarget>`)
- NuGet: `MaxRev.Gdal.Core 3.10.1.319` + `MaxRev.Gdal.WindowsRuntime.Minimal 3.10.1.319`
- 検証内容:
  1. `GdalBase.ConfigureAll()` がエラーなしで完了するか
  2. OGR driver 列挙で `ESRI Shapefile` (必須) と `MapInfo File` / `LIBKML` / `KML` / `GeoJSON` / `CSV` (情報目的) の存在
  3. ネイティブ DLL 配布サイズの目安

## 実行結果 (2026-06-02 ローカル、Windows 11 10.0.26100, X64)

```
=== Phase C WC0 / C100: Minimal SKU PoC ===
Run at:           2026-06-02T07:59:22.9940688+00:00
Process arch:     X64
OS:               Microsoft Windows 10.0.26100

[1/4] GdalBase.ConfigureAll() ...
      OK

[2/4] OGR driver enumeration ...
      Total drivers: 81

      Must-have drivers:
        [✓] ESRI Shapefile

      Nice-to-have drivers (Phase C' / Phase D 判断材料):
        [✓] MapInfo File
        [✓] LIBKML
        [✓] KML
        [✓] GeoJSON
        [✓] CSV

[3/4] Distribution size estimates ...
      Native DLLs under output dir: 18 files, 30.8 MB
      Total build output:           94.4 MB

[4/4] Go / No-Go judgement ...
      [GO] all must-have drivers are present in Minimal SKU
      → Action: WC1 (C101 / C102) 着手可

=== End of PoC ===
```

## 判定詳細

### 必須項目 (Phase C 本体)

| 項目 | 結果 | 備考 |
|------|:----:|------|
| `GdalBase.ConfigureAll()` がエラーなし | ✓ | x64 ターゲットで正常初期化 |
| `ESRI Shapefile` driver 含有 | ✓ | Phase C 本体 (C102 / C103) 着手可 |

### 副次的な発見 (Phase C' / Phase D に効く)

| Driver | 結果 | 含意 |
|--------|:----:|------|
| `MapInfo File` | ✓ | **Phase C' で Full SKU 切替不要**。MIF/MID と TAB は同一 driver で扱う |
| `LIBKML` | ✓ | **Phase D KML 対応で Full SKU 切替不要**。Phase C 設計案 P §6.13 で「LIBKML が `Minimal` SKU で利用可能か再確認」としていた論点が解消 |
| `KML` | ✓ | LIBKML とは別の軽量 KML driver。LIBKML の代替として保険 |
| `GeoJSON` | ✓ | Phase B WinForms の自前実装 (NetTopologySuite ベース) と並走可能 |
| `CSV` | ✓ | 同上 |

OGR 総 driver 数 81 件。Phase C 範囲では十分な被覆。

### 配布サイズ

- ネイティブ DLL: **約 30.8 MB** (18 ファイル、`gdal*` / `proj*` / `geos*` / `sqlite*` / `expat*` / `tiff*` / `png*` / `jpeg*` / `zstd*` 等)
- ビルド出力全体: **約 94.4 MB** (managed DLL + ネイティブ + サテライト リソース込み)
- `PHASE_C_DESIGN_P.md` §5 の見立て (60-80 MB) より小さい。**ClickOnce / MSIX 差分配布最適化は Phase D まで遅延可能**

## WC1 着手の前提条件としての確認

| 受け入れ条件 (Issue #146 C100) | 充足 |
|-------------------------------|:----:|
| PoC を `tools/poc/GdalSkuCheck/` に作成 | ✓ |
| `ESRI Shapefile` driver 含有を確認 | ✓ |
| 配布サイズを記録 | ✓ (30.8 MB native / 94.4 MB total) |
| `go` / `no-go` 判断を PR description に明記 | ✓ (本ドキュメント) |

## Phase C 計画への反映 (今後の Issue 化推奨事項)

本 PoC で明らかになった情報は **計画文書側の表現を最新化する余地** を生んだ:

1. `PHASE_C_DESIGN_P.md` §6.13 Phase D 申し送り 1.「KML / KMZ 対応: LIBKML が `Minimal` SKU で利用可能か再確認」→ **本 PoC で `Minimal` SKU 含有を確認済** に書き換え可能 (Phase D 着手時の不確実性が 1 つ減った)
2. `PHASE_C_DESIGN_P.md` §6.12 Phase C' 申し送り 1.2.「MIF/MID 対応 / TAB 対応」→ **本 PoC で `Minimal` SKU 含有を確認済** に書き換え可能 (Phase C' での GDAL 再評価が不要)

ただし上記は本 PR (WC0) のスコープ外、別 Issue (`phase-c-prime-followup`) で起票する。

## 次のアクション

- 本 PR をマージ → WC1 着手
- WC1 = C101 (GDAL NuGet + x64 固定 + `GdalBase.ConfigureAll()` 配線) → C102 (`GdalLayerSource` 骨格)
- WC1 のブランチは `feature/phase-c-wc1-skeleton`、`base=main` 固定
