# Admin Style Editor (Phase D' D'2 + D'3)

WebGIS の admin 画面で SLD を編集 + ライブプレビュー + カラーランプ自動生成する。

## 背景

Phase D D202 で `PUT /api/admin/layers/{id}/style` を実装したが、管理者向け UI が無い。SLD XML を直接書く必要があり、カラーランプ (数値属性ベースの段階配色) も手作業で `<ogc:PropertyIsLessThan>` を並べる必要。

Phase D' で以下を整える:

- D'2: Monaco エディタ + ライブプレビュー
- D'3: カラーランプ UI (Quantile / EqualInterval / Manual breaks)

## 採用案: 単一 HTML エントリ + 分離バンドル

`webgis/` 内に新エントリ `admin-style.html` を作成。Vite の `build.rollupOptions.input` で多エントリ化:

```typescript
// webgis/vite.config.ts
export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        main: 'index.html',
        adminStyle: 'admin-style.html'
      }
    }
  }
});
```

Monaco editor は CDN 経由で動的 import (一般 WebGIS bundle に混入させない):

```typescript
// webgis/src/admin/styleEditor.ts
import('@monaco-editor/loader').then((monaco) => {
  monaco.default.config({ paths: { vs: 'https://cdn.jsdelivr.net/npm/monaco-editor@0.50.0/min/vs' } });
  monaco.default.init().then((monacoInstance) => {
    // editor 初期化
  });
});
```

## 画面構成

```
┌─────────────────────────────────────────────────┐
│ Layer: [圃場 (1) ▼]  Theme: [default ▼]  [保存] │
├──────────────────────────┬──────────────────────┤
│ Monaco SLD Editor        │ Preview Map          │
│                          │                      │
│ <Rule>                   │ (OpenLayers)         │
│   <PolygonSymbolizer>    │                      │
│     <Fill>               │  ┌──────┐            │
│       <CssParameter ...  │  │      │            │
│   </PolygonSymbolizer>   │  └──────┘            │
│ </Rule>                  │                      │
│                          │                      │
├──────────────────────────┴──────────────────────┤
│ ColorRamp Generator                             │
│  Field: [harvest_qty ▼]  Bins: [5]              │
│  Method: [Quantile ▼]  Palette: [Viridis ▼]     │
│  [ヒストグラム svg]   [SLD に挿入]                │
└─────────────────────────────────────────────────┘
```

## ライブプレビュー (D'203)

エディタ入力 → debounce 500ms → 一連の処理:

1. `PUT /api/admin/layers/{layerId}/style` (SLD 全文を body に)
2. レスポンスの `styleVersion` を保持
3. 右ペイン preview map の TileLayer URL を `?sv={styleVersion}` で更新 → 再描画

エラー処理:
- GeoServer 側 SLD コンパイル失敗 (REST API が 4xx 返す) → エディタ上部に赤帯エラー表示
- 保存ボタンは無効化、自動 PUT も停止

## カラーランプ UI (D'204 + D'205)

### サーバ側: GET /api/admin/layers/{id}/attributes/{field}/stats (D'105)

```http
GET /api/admin/layers/1/attributes/harvest_qty/stats?bins=5&method=quantile
→ 200 OK
{
  "field": "harvest_qty",
  "method": "quantile",
  "bins": 5,
  "breaks": [120.5, 280.0, 450.0, 720.0, 1200.0],
  "min": 50.0,
  "max": 1500.0,
  "count": 12450,
  "histogram": [{ "min": 50, "max": 130, "count": 2510 }, ...]   // 20 bin で固定
}
```

SQL (Quantile):

```sql
WITH samples AS (
    SELECT (attributes->>'harvest_qty')::numeric AS v
      FROM feature_current
     WHERE layer_id = 1
       AND attributes->>'harvest_qty' IS NOT NULL
     LIMIT 50000
)
SELECT percentile_cont(s) WITHIN GROUP (ORDER BY v) AS pct
  FROM samples,
       LATERAL (SELECT unnest(ARRAY[0.2, 0.4, 0.6, 0.8, 1.0]) AS s) AS p
```

EqualInterval は単純に `min + (max-min)*i/N`、Manual はユーザー指定の breaks をそのまま受領。

### クライアント側: カラーランプ生成

`webgis/src/admin/colorRamp.ts`:

```typescript
export interface ColorRamp {
  field: string;
  breaks: number[];        // [120.5, 280.0, 450.0, 720.0, 1200.0]
  colors: string[];        // ['#fdb380', '#fc8d59', '#e34a33', '#b30000', '#680000']
}

const PALETTES: Record<string, (n: number) => string[]> = {
  Viridis: (n) => [...generateViridis(n)],
  RdYlGn: (n) => [...generateRdYlGn(n)],
  ...
};

export async function generateColorRamp(
  layerId: number,
  field: string,
  bins: number,
  method: 'quantile' | 'equal' | 'manual',
  palette: string
): Promise<ColorRamp> {
  const stats = await fetch(`/api/admin/layers/${layerId}/attributes/${field}/stats?bins=${bins}&method=${method}`);
  const colors = PALETTES[palette](bins);
  return { field, breaks: stats.breaks, colors };
}
```

### サーバ側: SldXmlBuilder 拡張 (D'205)

PUT で受領した style_json に `colorRamp` プロパティが含まれる場合、`SldXmlBuilder.cs` が N 段 Rule を生成:

```csharp
public string Build(JsonElement styleJson)
{
    // 既存処理
    if (styleJson.TryGetProperty("colorRamp", out var cr))
    {
        return BuildColorRampSld(cr);
    }
    return BuildBaseSld(styleJson);
}

private string BuildColorRampSld(JsonElement cr)
{
    var field = cr.GetProperty("field").GetString();
    var breaks = cr.GetProperty("breaks").EnumerateArray().Select(e => e.GetDouble()).ToArray();
    var colors = cr.GetProperty("colors").EnumerateArray().Select(e => e.GetString()).ToArray();
    var sb = new StringBuilder();
    sb.Append(SldHeader);
    for (int i = 0; i < colors.Length; i++)
    {
        double? lower = i == 0 ? null : breaks[i - 1];
        double? upper = i == colors.Length - 1 ? null : breaks[i];
        sb.Append($@"
<Rule>
  <Title>{field} bin {i}</Title>
  <ogc:Filter>
    {RangeFilter(field, lower, upper)}
  </ogc:Filter>
  <PolygonSymbolizer>
    <Fill><CssParameter name=""fill"">{colors[i]}</CssParameter></Fill>
  </PolygonSymbolizer>
</Rule>");
    }
    sb.Append(SldFooter);
    return sb.ToString();
}
```

## 認可

- `/admin-style.html` は public route (HTML ファイル自体)
- 内部の API 呼び出しは Bearer JWT 必須、admin role 必須 (既存 `[Authorize(Roles="admin")]` 配線)
- 非 admin がアクセスした場合は 403 → エディタ画面に「権限がありません」表示

## WinForms からの導線 (D'206)

`LayerAdminForm` に「テーマ編集を WebGIS で開く」ボタン追加:

```csharp
var url = $"http://localhost:5173/admin-style.html?layerId={layer.LayerId}";
Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
```

別ブラウザで開く形 (既存 WebView2 とは別 instance、編集中に MainForm の地図を見ながら作業できる)。

## 受入条件

1. `admin-style.html` が分離バンドルとして `npm run build` で生成 (一般 bundle に Monaco が混入しない)
2. layer/theme 選択 → SLD が Monaco に表示 (`GET /api/admin/layers/{id}/style`)
3. SLD 編集 → 500ms 後に preview map が新色で再描画
4. SLD コンパイル失敗時にエラー帯表示 + 自動 PUT 停止
5. カラーランプで `harvest_qty` 5 階級 Quantile Viridis → SLD 自動生成 → Monaco に挿入 → preview 反映
6. 保存 → `style_version+1` → MainForm WebView2 で**手動 reload なしで**反映 (SSE 連動、D'5)
7. WinForms `LayerAdminForm` の「テーマ編集」ボタンで `/admin-style.html` 起動

## テスト

- `styleEditor.spec.ts` (`webgis vitest`): Monaco モック、PUT 呼び出し、styleVersion +1 後の URL 更新
- `colorRamp.spec.ts` (`webgis vitest`): Quantile/EqualInterval/Manual の breaks 計算、Viridis 色配列生成
- `SldXmlBuilderColorRampTests` (`api.tests`): `colorRamp` 受領で N 段 Rule 生成、`RangeFilter` の上限/下限境界

## 関連

- `docs/PHASE_D_PRIME_INDEX.md`
- `docs/sld-cache-busting.md` (D'1: style_version 伝搬)
- `docs/feature-events-sse.md` (D'5: SSE で自動反映)
- `docs/rendering.md` (Phase D SLD theme 経路)
- メモリ `selection_visualization_and_multi_select.md`
