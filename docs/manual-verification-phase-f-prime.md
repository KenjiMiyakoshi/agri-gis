# Phase F' 動作確認シナリオ (F'501)

Phase F' (`SSE multiplex + tile invalidation + z-order drag`) の手動 E2E 検証。Phase F の `docs/manual-verification-phase-f.md` で構築した 2 組織 × 3 ユーザ matrix (admin / salesA / prodA) を引き続き使用する。

## 前提

- Phase F' の全 PR (#246-#250 + 本 PR) マージ済
- 0F04_user_preference.sql 適用済
- API / WebGIS / WinForms 起動済 (Phase F の手順と同じ)

## シナリオ前準備

Phase F のシナリオで作成した組織と権限が残っていることを確認:

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"loginId":"admin","password":"<admin pw>"}' | jq -r .accessToken)

# 既定組織 / 営業部 / 生産部 (Phase F で作成)
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/admin/organizations | jq

# 営業部の現在権限 (layer 1, 2 が view+edit、layer 7 hidden 等)
curl -s -H "Authorization: Bearer $TOKEN" \
  http://localhost:5080/api/admin/organizations/<sales orgId>/layer-permissions | jq
```

## E2E シナリオ

### S1. SSE 統合確認 (WF'1 + WF'2)

1. salesA で WinForms 起動 → ログイン
2. CheckedListBox で layer 1, 2 を ON
3. WebGIS DevTools の Network タブを開く (WebView2 のため Inspect が必要、簡易には API ログで確認)
4. **期待**: `/api/events/stream-all?layerIds=1,2` への接続が **1 本**だけ存在 (Phase F 期は per-layer で 2 本)
5. layer 1 を OFF → 接続が `?layerIds=2` に張り替わる (1 本のまま)

### S2. 権限変更の即時 invalidate (WF'1 + WF'2 + WF'4)

1. **admin の WinForms** と **salesA の WinForms** を 2 台並行起動 (別ユーザの WebView2 環境分離)
   - 別 PC でも可、簡易には別 Windows ユーザでログイン
2. salesA で layer 1 を表示 (CheckedListBox で ON、地図に圃場が出る)
3. admin で 管理 → レイヤ管理 → 権限管理...
4. 営業部を選択 → layer 1 の `閲覧可` を OFF → 保存
5. **期待**: salesA の WebGIS で **数秒以内に** layer 1 が地図から消える
   - 詳細: `permission_invalidate` event → `fetchLayers` 再取得 → 不可視 layer の `removeLayer` + SSE 再購読
   - 再ログイン or 24h 待たずに即時反映 (Phase F の S5 シナリオの即時化)

### S3. Tile cache invalidation の検証 (WF'4 + WF'2)

S2 の続き:

1. salesA の WebGIS DevTools (or 検証 PC) で Network タブを確認
2. **期待**: layer 1 の tile (`/tiles/1/default/...`) への新規 fetch は admin の権限剥奪後は 403 で返る
3. WebGIS は剥奪を検知して `removeLayer` するため、tile 自体が再取得されない

### S4. z-order drag + 永続化 (WF'3)

1. salesA で layer 1, 2 を CheckedListBox で ON
2. **CheckedListBox 内で layer 2 を上にドラッグ** (左クリック保持 + 上方向)
3. **期待**: WebGIS で layer 2 が前面に、layer 1 が後面に並べ替わる
   - 詳細: WinForms drag → `layer_order_change` envelope → WebGIS `reorderLayers`
4. WinForms をいったん終了
5. salesA で再ログイン
6. **期待**: CheckedListBox の表示順が **保存された順序**で復元される (layer 2 が上)
   - 詳細: OnLoad で `GetUserPreferenceAsync('layer_order_v1')` → `ApplyPersistedLayerOrder`

### S5. 自己リソース原則の確認

1. salesA で order を保存
2. admin で同じキーで GET:
   ```bash
   curl -H "Authorization: Bearer $TOKEN_ADMIN" \
     http://localhost:5080/api/user/preferences/layer_order_v1
   ```
3. **期待**: 404 (admin の preference はまだ未設定、salesA の preference は admin から見えない)

### S6. 旧 SSE endpoint の deprecated 確認

```bash
curl -i -H "Authorization: Bearer $TOKEN" \
  http://localhost:5080/api/events/layers/1/stream
```

**期待**: 接続は維持されるが、レスポンスヘッダに以下:

```
Sunset: Sun, 31 Dec 2026 23:59:59 GMT
Deprecation: true
Link: </api/events/stream-all>; rel="successor-version"
```

## 確認チェックリスト

- [ ] S1: SSE が 1 connection に統合される
- [ ] S2: 権限剥奪が即時 (数秒) 反映、再ログイン不要
- [ ] S3: tile cache が即時 flush (再取得時 403)
- [ ] S4: drag で z-order 変更、再起動で復元
- [ ] S5: user_preference が自己リソース (他人から不可視)
- [ ] S6: 旧 SSE endpoint に Sunset / Deprecation ヘッダ

## 関連

- `docs/PHASE_F_PRIME_INDEX.md`
- `docs/sse-multiplex.md` (Design)
- `docs/tile-invalidation-on-perm.md` (Design)
- `docs/manual-verification-phase-f.md` (Phase F E2E、本シナリオの前提)
- 申し送り: SSE multiplex の Redis 化 (本番 1000+ user) は Phase H 候補
