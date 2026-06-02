# db/migration/down/

各 migration の **ロールバック SQL**。`db/migration/NNN_xxx.sql` に対応する `NNN_xxx_down.sql` を配置。

## 適用方法

`db/migration/*.sql` の連番順とは **逆順** で流す:

```powershell
Get-ChildItem db/migration/down/*_down.sql | Sort-Object Name -Descending | ForEach-Object {
  Write-Host "Rolling back $($_.Name)..."
  Get-Content $_.FullName -Raw | docker exec -i agri_postgis psql -U agri_user -d agri_gis
}
```

## 制約

- Phase A 以前の migration (`001-009`, `0A0x`, `0B0x`) の down は本ディレクトリには **置いていない** (該当 Phase で実装されたデータ整合性破壊の懸念があるため、テスト目的のみで都度書く)
- Phase D (`0D0x`) からは「Wave 完了時の rollback 手順」として down script を必ず追加する
- 本ディレクトリは `PostgisContainerFixture` の自動 migration 経路から **除外** される (`db/migration/*.sql` glob は subdirectory 不参照)

## 関連

- `db/migration/README.md`: up migration の運用ルール
- `docs/issues/PHASE_D_DESIGN_P.md`: Phase D 採用案 (rollback 順序の根拠)
