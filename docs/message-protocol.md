# メッセージ規約 (WinForms ↔ WebGIS)

WebView2 ホスト (WinForms) と WebGIS (OpenLayers in WebView2) の双方向通信仕様。
コード散在を避けるため、本ファイルが**唯一の正本**。型定義は TypeScript / C# 双方を併記する。

実装：
- WebGIS 側: [`webgis/src/bridge/messages.ts`](../webgis/src/bridge/messages.ts), [`webgis/src/bridge/webviewBridge.ts`](../webgis/src/bridge/webviewBridge.ts)
- WinForms 側: [`windos-app/Services/Envelope.cs`](../windos-app/Services/Envelope.cs), [`windos-app/Services/BridgeMessenger.cs`](../windos-app/Services/BridgeMessenger.cs)

---

## 1. Envelope 構造

すべてのメッセージは以下の envelope に包んで送受信する。

```ts
interface Envelope<P = unknown> {
  type: string;                // タイプ識別子（後述）
  payload: P;                  // タイプごとに固有の構造
  requestId?: string;          // 任意。重複検知 / 応答ひも付けに使う
}
```

```csharp
public sealed record Envelope(
    string Type,
    JsonElement Payload,
    string? RequestId
);
```

### 1.1 物理伝送

- 文字列化した JSON を経由する。
- **Host → Web**：`CoreWebView2.PostWebMessageAsString(json)`
- **Web → Host**：`window.chrome.webview.postMessage(json)`
- ASCII 制御文字や BOM は付けない。`JsonSerializerOptions.PropertyNamingPolicy = CamelCase`。

### 1.2 メッセージ送出のタイミング

- Host → Web の初回メッセージは、Web 側からの `map_ready` を受領した後に送る。
  これにより WebGIS 側のリスナ未登録 race を避ける。
- WinForms 側は `EnsureCoreWebView2Async()` 完了 + `NavigationCompleted` の両方を待ってから `BridgeMessenger` を生成する。

---

## 2. requestId 規約

- **任意フィールド**。Web 側・Host 側のどちらが生成しても良い。
- 受信側は **5 分の TTL** で重複検知する。同一 `requestId` の 2 回目は **黙って破棄**。
- 値は実装系で衝突しなければ何でも可（推奨：UUID v4 や `hex(crypto.randomUUID())`）。
- WinForms 側の audit_log で扱う `request_id` (REST API) とは**別物**。混同しないこと。

参照実装：[`webgis/src/bridge/requestIdRegistry.ts`](../webgis/src/bridge/requestIdRegistry.ts)（TTL 5 分の Map + purge）。

---

## 3. 初期メッセージタイプ 5 種

### 3.1 Web → Host

#### 3.1.1 `feature_clicked`

地図上でフィーチャがクリックされたことを通知する。

```ts
interface FeatureClickedPayload {
  entityId: string;            // UUID
  layerId: number;
  featureId?: number;          // 任意（履歴対応のため非必須）
}
```

```csharp
public sealed record FeatureClickedPayload(
    string EntityId,
    int LayerId,
    long? FeatureId
);
```

#### 3.1.2 `map_ready`

WebGIS の初期化完了通知。Host はこれを受けてから書き込み系メッセージを送る。

```ts
interface MapReadyPayload {}
```

```csharp
public sealed record MapReadyPayload();
```

### 3.2 Host → Web

#### 3.2.1 `layer_select`

表示レイヤを切り替えるよう指示する。

```ts
interface LayerSelectPayload {
  layerId: number;
  asOf?: string;               // 'YYYY-MM-DD' 形式 (DATE 粒度)
}
```

```csharp
public sealed record LayerSelectPayload(int LayerId, string? AsOf);
```

#### 3.2.2 `features_reload`

現在表示中のレイヤを再フェッチさせる。属性保存後の即時反映などに使う。

```ts
interface FeaturesReloadPayload {
  layerId: number;
  asOf?: string;               // 'YYYY-MM-DD'
}
```

```csharp
public sealed record FeaturesReloadPayload(int LayerId, string? AsOf);
```

#### 3.2.3 `feature_highlight`

指定 entity を強調表示するよう指示する。

```ts
interface FeatureHighlightPayload {
  entityId: string;
}
```

```csharp
public sealed record FeatureHighlightPayload(string EntityId);
```

---

## 4. 将来追加予定（未実装、領域確保のみ）

| `type` | 方向 | 用途 |
|---|---|---|
| `feature_edit_geometry` | Host → Web | 図形編集モード開始（OpenLayers Draw/Modify 起動） |
| `feature_edit_commit` | Web → Host | 編集結果の図形を送信 |
| `view_set_rotation` | Host → Web | 回転角の同期 |
| `feature_save_result` | Host → Web | 保存成功/失敗の即時通知 |
| `cursor_position` | Web → Host | マウス座標の同期（ステータスバー表示） |

実装時は本ドキュメントの**現行 5 種の下に追記**する形でバージョン管理する。
**型を破壊的に変更しない**こと（既存フィールドの削除や型変更は新タイプを切る方が安全）。

---

## 5. エラー / 失敗時の扱い

| 状況 | 受信側の挙動 |
|---|---|
| JSON パース失敗 | console.warn / Log し破棄 |
| 不明 `type` | 同上、破棄 |
| `requestId` 重複 | 破棄（ログなし） |
| payload 形不一致（必須フィールド欠落） | 受信側で個別判定。落としてエラー扱いは推奨せず、最低限の防御で破棄 + ログ |

**例外を投げない / プロセス停止しない**。bridge は best-effort の通信路として扱う。
業務的な失敗は別途 REST API のエラー応答（ProblemDetails）で表現する。

---

## 6. 関連ドキュメント

- [採択設計 案 B'](./issues/README.md)
- [テスト方針](./testing-policy.md)
- API 側エラー応答仕様：`api/Middleware/ProblemDetailsMiddleware.cs`
