# Phase F 動作確認シナリオ (F501)

複数レイヤ同時表示 + 組織×レイヤ権限の手動 E2E 検証。docker compose で全サービス起動した状態を前提とする。

## 前提

- `docker compose up -d` で agri_postgis + GeoServer 起動済
- `dotnet run --project api -c Release` で API 起動
- `npm run dev` (webgis ディレクトリ) で WebGIS dev server 起動
- `dotnet run --project windos-app -c Release` で WinForms 起動
- 環境変数: `AGRI_GIS_JWT_SECRET` + `AGRI_GIS_INITIAL_ADMIN_PW` 設定済

## シナリオ前準備 (admin 操作)

### 1. 組織と一般ユーザを作成

admin で WinForms にログイン → 「管理 → ユーザ管理...」(将来) or API 直接:

```bash
TOKEN=$(curl -s -X POST http://localhost:5188/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"loginId":"admin","password":"<admin pw>"}' | jq -r .accessToken)

# OrgA (営業部) を作成
curl -X POST http://localhost:5188/api/admin/organizations \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"営業部","code":"sales"}'
# 戻り値の id を ORG_A=<id> として控える

# OrgB (生産部) を作成
curl -X POST http://localhost:5188/api/admin/organizations \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"生産部","code":"production"}'
# 戻り値の id を ORG_B=<id>

# ユーザ作成 (OrgA に generalA / guestA、OrgB に generalB)
curl -X POST http://localhost:5188/api/admin/users \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"loginId\":\"generalA\",\"displayName\":\"General A\",\"orgId\":$ORG_A,\"roles\":[\"general\"],\"initialPassword\":\"PassA1!\"}"

curl -X POST http://localhost:5188/api/admin/users \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"loginId\":\"guestA\",\"displayName\":\"Guest A\",\"orgId\":$ORG_A,\"roles\":[\"guest\"],\"initialPassword\":\"PassA2!\"}"

curl -X POST http://localhost:5188/api/admin/users \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"loginId\":\"generalB\",\"displayName\":\"General B\",\"orgId\":$ORG_B,\"roles\":[\"general\"],\"initialPassword\":\"PassB1!\"}"
```

### 2. 既存 layer を確認 (3〜5 件想定)

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5188/api/admin/layers | jq '.[].layerName'
```

例: `layer_id=1:圃場`, `layer_id=2:観測点`, `layer_id=7:作業エリア` が存在するとする。

### 3. 権限マトリクス設定

admin で WinForms 起動 → 管理 → レイヤ管理 → 「権限管理...」ボタン → ダイアログで:

| 組織 | layer 1 (圃場) | layer 2 (観測点) | layer 7 (作業エリア) |
|---|---|---|---|
| 既定 (default) | view + edit | view + edit | view + edit |
| 営業部 (sales) | view + edit | view のみ | (未設定 = 不可視) |
| 生産部 (production) | view のみ | view + edit | view + edit |

各組織を ComboBox で選択 → CheckBox トグル → 「保存」 → 各組織の権限が確定。

## E2E シナリオ

### S1. generalA (営業部) でログイン → layer フィルタ確認

1. WinForms 再起動 → loginId=`generalA`, password=`PassA1!` でログイン
2. **期待**: 右パネル「表示レイヤ:」CheckedListBox に **2 件** (layer 1 + 2) のみ表示
3. **期待**: layer 7 (作業エリア) は **完全に消えている**
4. CheckedListBox で layer 1 を ON → 地図に圃場が表示される
5. CheckedListBox で layer 2 も ON → 地図に観測点も重なって表示される
6. layer 1 OFF → 圃場が消える、layer 2 だけ残る
7. layer 1, 2 両方 ON の状態で地図上の圃場をクリック → 右パネル下部に属性表示 → 編集可能 (saveButton 押下可)
8. layer 2 (観測点) を地図上でクリック → 属性表示 → **編集不可** (saveButton 無効化、canEdit=false 反映)

### S2. guestA (営業部 guest) でログイン → 全 layer 閲覧専用

1. ログアウト → loginId=`guestA`, password=`PassA2!`
2. **期待**: 表示レイヤ には layer 1 + 2 (S1 と同じ)
3. layer 1 ON → 圃場表示 → クリック → 属性表示 → **編集不可** (guest role → ApplyGuestRestriction)

### S3. generalB (生産部) でログイン → 別組織の権限確認

1. ログアウト → loginId=`generalB`, password=`PassB1!`
2. **期待**: 表示レイヤ に layer 1, 2, 7 の 3 件
3. layer 1 ON → 圃場表示 → クリック → 属性表示 → **編集不可** (生産部は layer 1 view のみ)
4. layer 2 ON → 観測点表示 → クリック → 属性表示 → **編集可** (view + edit)
5. layer 7 ON → 作業エリア表示 → クリック → 属性表示 → **編集可**

### S4. admin (既定組織) でログイン → 全 layer 全権限

1. ログアウト → loginId=`admin`, password=`<admin pw>`
2. **期待**: 表示レイヤ に全 layer (1, 2, 7)
3. 全レイヤ で編集可

### S5. 権限変更の即時反映 (再ログイン)

1. admin で「権限管理...」→ 営業部 → layer 7 を `can_view=true` に変更 → 保存
2. ログアウト → `generalA` で再ログイン
3. **期待**: 表示レイヤに layer 7 が **追加** されている
4. layer 7 ON → 地図表示 → クリック → 編集不可 (can_view のみ)

### S6. URL 直叩き対策 (深層防御)

DevTools (WebGIS) を開いて Network タブで tile URL を確認、別 layer の tile URL を `generalA` JWT で curl:

```bash
TOKEN_A=$(curl -s -X POST http://localhost:5188/api/auth/login -d '{"loginId":"generalA","password":"PassA1!"}' -H 'Content-Type: application/json' | jq -r .accessToken)

# 営業部に未設定の layer 7 を直接叩く
curl -i -H "Authorization: Bearer $TOKEN_A" \
  http://localhost:5188/tiles/7/default/15/29408/12051.png
```

**期待**: `HTTP 403 Forbidden` + ProblemDetails (F205 の can_view 検査が効いている)

### S7. POST /api/features 403 (write 防御)

```bash
# generalA が layer 2 (view のみ) に feature 追加を試みる
curl -i -X POST http://localhost:5188/api/features \
  -H "Authorization: Bearer $TOKEN_A" \
  -H 'Content-Type: application/json' \
  -d '{"layerId":2,"geometry":{"type":"Point","coordinates":[143.2,42.9]},"attributes":{"name":"test"}}'
```

**期待**: `HTTP 403 Forbidden` + ProblemDetails (F204 の can_edit 検査が効いている)

## 確認チェックリスト

- [ ] S1: 組織 × layer のフィルタが効く
- [ ] S1: 複数 layer 重ね表示 (CheckedListBox 複数 ON)
- [ ] S1: canEdit に応じて AttributeEditor の read-only 制御
- [ ] S2: guest 全 layer 閲覧専用 (既存挙動の維持)
- [ ] S3: 別組織で権限マトリクスが反映される
- [ ] S4: admin は全 layer + 全権限 bypass
- [ ] S5: 権限変更 → 再ログインで反映
- [ ] S6: tile URL 直叩き 403 (深層防御)
- [ ] S7: feature POST 403 (write 防御)

## 関連

- `docs/PHASE_F_INDEX.md`, `docs/org-layer-permission.md`, `docs/multi-layer-display.md`
- 申し送り: tile cache TTL 24h で `S5` の即時反映は不可 (再ログイン必要)。SSE での即時 invalidate は F'/G で検討
