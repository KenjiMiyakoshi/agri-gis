# Phase H5 完了サマリ

Phase H5 (MainForm god class リファクタ) 完了時点の高位サマリ。Review② で識別された唯一の残債 (H5) を解消、全 Phase 完了マーカー。

## マージ済 PR (全 2 件)

| Wave | PR | 内容 |
|------|----|------|
| WH5-1 | [#237](https://github.com/KenjiMiyakoshi/agri-gis/pull/237) | `MainFormController` 抽出 + `HandleUnauthorizedAsync` reload 二重実装統合 + `MainFormControllerTests` 5 件 |
| WH5-2 | 本 PR | `BridgeRouter` + `EditPolicy` 抽出 + asOf 解除時 guest 復元バグ予防 + `BridgeRouterTests` 4 件 + `EditPolicyTests` 4 ケース |

## 受入条件

1. ✅ MainForm.cs から layers 管理 / Unauthorized 復旧 / bridge dispatch / 編集可否判定 を切り出し
2. ✅ `HandleUnauthorizedAsync` の reload 二重実装解消
3. ✅ asOf 解除時の guest 復元バグ予防 (`EditPolicy` 経由統一)
4. ✅ `MainForm.cs` 行数: 342 → 約 320 行 (一部ヘルパ追加で純減少は小、責務切り出し効果が主)
5. ✅ 新規ファイル: `MainFormController.cs`, `BridgeRouter.cs`, `EditPolicy.cs` + 各テスト
6. ✅ 既存テスト全 pass + 新規テスト ~13 件追加
7. ✅ 動作: WinForms ログイン → レイヤ選択 → クリック編集 → asOf 切替 → ログアウト再ログイン (手動 smoke)

## 主要な実装メモ

- **`MainFormController`** (WH5-1): UI 非依存。`ReloadAsync(prevSelectedLayerId)` で純関数 `ComputeRestoreIndex` を返却、UI 側 (MainForm) が ComboBox 更新と SelectedIndex 設定を行う。`TryRecoverUnauthorizedAsync` は `Func<bool>` デリゲートで LoginForm 表示を委譲し、復旧後の reload を Controller 内で完結
- **`BridgeRouter`** (WH5-2): `Register(type, handler)` テーブル方式、未登録 type は無視、`OnError` で例外を通知 (UnauthorizedApiException は handler 内で識別して `HandleUnauthorizedAsync` 呼び出し)
- **`EditPolicy.ShouldBeReadOnly(isGuest, isAsOfActive)`** (WH5-2): 純関数 (4 truth table)。`ApplyEditPolicy()` で `AsOfState.IsReadOnly` と `Session.IsGuest` を同時評価、asOf トグル時/feature ロード時の双方で呼び出すことで、旧版の「asOf 解除時に guest が編集可能に復元される」バグを予防
- **`SendThemeChange`**: dead code に近いが将来 theme ComboBox UI で使用予定として残置 (PR レビュアー確認推奨)

## 残債なし

Review② で識別された C1-C3, UI-1/2, H1-H4 はすべて過去フェーズで解消、H5 が唯一の残債だった。本 PR で全消化。

## H'' 申し送り (新規 enhancement、リファクタ範囲外)

- **`LayerEventListener` 配線**: Phase E' WE'4 で DI 登録のみ、MainForm からの `Subscribe` 未配線。Phase E'' 候補
- **theme ComboBox UI**: `SendThemeChange` を活用する UI 未実装、Phase D'' 候補
- **WebView2 イベント `OnLoad` 内部**: 一括処理だが state machine 化の余地あり (重要度低、現状で動作良好)
- **`MainForm.cs` の更なる削減**: WebView2 lifecycle + asOf イベントブリッジは form 残置が妥当、これ以上の分割は overengineering

## 関連

- `docs/PHASE_H5_COMPLETE.md` (本ドキュメント)
- メモリ `review2_findings.md` (H5 残債の出典、解消完了で更新)
- メモリ `orchestration_state.md` (全 Phase 完了マーカー)
