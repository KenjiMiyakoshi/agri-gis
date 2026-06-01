# 0601: メッセージ規約ドキュメント (envelope, 5 タイプ, 将来追加)

| 項目 | 値 |
|---|---|
| Phase | Docs |
| Estimate | 0.5d |
| Depends on | 0403 |
| Blocks | なし |

## 概要
WebView2 ホスト ↔ WebGIS のメッセージ規約を `docs/message-protocol.md` に明文化する。

## 背景・目的
案 B' で「メッセージ envelope の構造、初期 5 タイプ、将来追加余地を文書化」が必須。コードコメントで散在させると齟齬が出るので一箇所に集める。

## スコープ
### 含む
- `docs/message-protocol.md`
- 章立て:
  1. Envelope 構造
     - `{ type: string, payload: object, requestId?: string }`
     - JSON 文字列で `PostWebMessageAsString` / `chrome.webview.postMessage`
  2. requestId 規約
     - 任意。Host → Web で生成、Web → Host でも独自生成可
     - 受信側で 5 分の TTL で重複検知
  3. 初期メッセージタイプ 5 種（payload 仕様つき）
     - **Web → Host**
       - `feature_clicked`: `{ entityId: string; layerId: number; }`
       - `map_ready`: `{}`
     - **Host → Web**
       - `layer_select`: `{ layerId: number; }`
       - `features_reload`: `{ layerId: number; asOf?: 'YYYY-MM-DD'; }`
       - `feature_highlight`: `{ entityId: string; }`
  4. 将来追加予定（コメント領域）
     - `feature_edit_geometry`: 図形編集セッション開始
     - `view_set_rotation`: ホストからの回転制御
     - `feature_save_result`: 保存結果通知
  5. エラー / 失敗時の扱い
     - 不明 type は受信側で破棄（warn）
     - JSON パース失敗も破棄
  6. 参照コード
     - `webgis/src/bridge/messages.ts` (0403)
     - `windos-app/Services/BridgeMessenger.cs` (0503)

### 含まない
- 実装（コードは別イシュー）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `docs/message-protocol.md` が存在
- [ ] 上記 6 章が揃う
- [ ] 各 type の payload に TypeScript 型 / C# record の双方を併記

## 影響ファイル
- `D:\proj\agri-gis\docs\message-protocol.md` (新規)

## 実装ノート
- TypeScript 型と C# record を並べて書く形:
  ```ts
  interface FeatureClickedPayload { entityId: string; layerId: number; }
  ```
  ```csharp
  public sealed record FeatureClickedPayload(string EntityId, int LayerId);
  ```
- 将来追加予定は「コメント」とし、未実装が明確になるようにする

## テスト観点
- ドキュメント
