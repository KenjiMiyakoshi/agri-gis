# 0401: `webgis/src/` モジュール分割

| 項目 | 値 |
|---|---|
| Phase | WebGIS |
| Estimate | 1d |
| Depends on | なし |
| Blocks | 0402, 0403 |

## 概要
単一ファイル `webgis/src/main.ts` を `map/`, `api/`, `bridge/`, `controllers/` の 4 サブモジュールに分割する。過剰分割しない。

## 背景・目的
案 B' で WebView2 ホストや属性編集 UI が絡んでくると main.ts が肥大化するので、I/O 境界と純粋ロジックを早めに分けておく。

## スコープ
### 含む
- `webgis/src/map/mapInit.ts` (Map / View 生成)
- `webgis/src/map/vectorLayer.ts` (vectorSource / vectorLayer エクスポート)
- `webgis/src/map/styles.ts` (ポリゴン/ポイントスタイル)
- `webgis/src/api/types.ts` (DTO の型: LayerDto, FeatureCollectionDto 等。0402 で本格化)
- `webgis/src/api/client.ts` (`fetchLayers`, `fetchFeatures` の薄ラッパ。0402 で本格化)
- `webgis/src/controllers/layer.ts` (`wireLayerSelect`, `loadFeatures` 移植)
- `webgis/src/controllers/selection.ts` (将来用、空でも OK)
- `webgis/src/bridge/` は 0403 で作成（本イシューではディレクトリだけ用意）
- `webgis/src/main.ts` を 30 行以下にリファクタ（各モジュールを束ねるだけに）
- `tsconfig.json` の `paths` 整備（任意）

### 含まない
- bridge / メッセージ envelope (0403)
- API クライアントの型強化 (0402)
- Vitest (0404)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `npm run dev` が動き、ブラウザで地図とレイヤ切替が従来通り動く
- [ ] `webgis/src/main.ts` が 30 行以下
- [ ] 上記サブディレクトリが存在し、各モジュールに 1 つの責務だけが入っている
- [ ] `npm run build` (tsc) がエラーなし

## 影響ファイル
- `D:\proj\agri-gis\webgis\src\main.ts` (大幅縮小)
- `D:\proj\agri-gis\webgis\src\map\*.ts` (新規)
- `D:\proj\agri-gis\webgis\src\api\*.ts` (新規, 雛形のみ)
- `D:\proj\agri-gis\webgis\src\controllers\*.ts` (新規)

## 実装ノート
```ts
// main.ts (Before: 116 行 → After: 30 行未満)
import { createMap } from './map/mapInit';
import { wireLayerSelect } from './controllers/layer';
import { wireRotation } from './controllers/rotation';

const ctx = createMap('map');
wireLayerSelect(ctx);
wireRotation(ctx);
```

```ts
// map/mapInit.ts
export interface MapContext { map: Map; view: View; vectorSource: VectorSource; vectorLayer: VectorLayer<...>; }
export function createMap(targetId: string): MapContext { /* ... */ }
```

注意点:
- スタイルは 1 ファイルにまとめる（type === 'Point'/'MultiPoint' 判別含む）
- 既存の `wireRotation` は `controllers/rotation.ts` に分離してもいい（半日内で収まるなら）

## テスト観点
- 手動 smoke のみ。Vitest は 0404
