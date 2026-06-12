using AgriGis.Desktop.Core.LayerTree;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core.LayerTree;

// LG203 (Phase LG WLG2): Merge (デフォルトツリー × user preference) と
// MigrateFromFlatOrder (旧 layer_order_v1 移行) の規則テスト。
public sealed class LayerTreeMergeTests
{
    // デフォルトツリー (DB 由来想定):
    // root
    // ├─ db:1 "賦課"
    // │   ├─ layer 10
    // │   └─ layer 11
    // └─ layer 30
    private static LayerTreeModel BuildDefaultTree()
        => LayerTreeModel.Build(
            new[] { new TreeGroupDefinition("db:1", "賦課", null, 0) },
            new[]
            {
                new TreeLayerPlacement(10, "db:1", 0),
                new TreeLayerPlacement(11, "db:1", 1),
                new TreeLayerPlacement(30, null, 1),
            });

    // ---------------- Merge: preference 無し ----------------

    [Fact]
    public void Merge_NoPreference_ReturnsDefaultTree()
    {
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), null, new[] { 10, 11, 30 });

        Assert.Equal(new[] { 10, 11, 30 }, merged.EnumerateAllLayerIds());
        Assert.Equal("賦課", merged.FindGroup("db:1")!.Name);
        Assert.Equal("db:1", merged.FindLayer(10)!.ParentKey);
        Assert.Null(merged.FindLayer(30)!.ParentKey);
    }

    [Fact]
    public void Merge_InvalidPreferenceJson_FallsBackToDefaultTree()
    {
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), "{ broken", new[] { 10, 11, 30 });

        Assert.Equal(new[] { 10, 11, 30 }, merged.EnumerateAllLayerIds());
        Assert.NotNull(merged.FindGroup("db:1"));
    }

    [Fact]
    public void Merge_NoPreference_LayerMissingFromDefaultTree_AppendedToRootTail()
    {
        // defaultTree に無い available レイヤ (99) はルート末尾へ
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), null, new[] { 10, 11, 30, 99 });

        Assert.Equal(new[] { 10, 11, 30, 99 }, merged.EnumerateAllLayerIds());
        var l = merged.FindLayer(99)!;
        Assert.Null(l.ParentKey);
        Assert.Equal(merged.RootNodes.Count - 1, l.Order);
    }

    // ---------------- Merge: preference 有り ----------------

    private const string PreferenceJson = """
        {
          "groups": [
            { "key": "db:1", "name": "古い名前", "parent": null, "order": 1, "expanded": false },
            { "key": "usr:a1b2c3d4", "name": "自分用", "parent": null, "order": 0, "expanded": true }
          ],
          "layers": [
            { "layerId": 30, "parent": "usr:a1b2c3d4", "order": 0 },
            { "layerId": 11, "parent": "db:1", "order": 0 },
            { "layerId": 10, "parent": "db:1", "order": 1 }
          ]
        }
        """;

    [Fact]
    public void Merge_WithPreference_AdoptsPreferenceStructure()
    {
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), PreferenceJson, new[] { 10, 11, 30 });

        // preference の構造: usr グループが先、db:1 内は 11 → 10、layer30 は usr 配下
        Assert.Equal(new[] { 30, 11, 10 }, merged.EnumerateAllLayerIds());
        Assert.Equal("usr:a1b2c3d4", merged.FindLayer(30)!.ParentKey);
        Assert.False(merged.FindGroup("db:1")!.Expanded); // expanded は preference 採用
    }

    [Fact]
    public void Merge_DbGroupName_FollowsDefaultTreeRename()
    {
        // preference には "古い名前" が入っているが、DB 側 (defaultTree) の rename に追従する
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), PreferenceJson, new[] { 10, 11, 30 });

        Assert.Equal("賦課", merged.FindGroup("db:1")!.Name);
    }

    [Fact]
    public void Merge_UserGroup_IsPreservedAsIs()
    {
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), PreferenceJson, new[] { 10, 11, 30 });

        var usr = merged.FindGroup("usr:a1b2c3d4")!;
        Assert.Equal("自分用", usr.Name); // usr: は preference の名前のまま
        Assert.Null(usr.ParentKey);
        Assert.Equal(0, usr.Order); // 並び順も preference のまま
    }

    [Fact]
    public void Merge_NewLayerNotInPreference_PlacedAtDefaultTreePosition()
    {
        // preference 保存後に layer 12 が db:1 へ追加されたケース
        var defaultTree = LayerTreeModel.Build(
            new[] { new TreeGroupDefinition("db:1", "賦課", null, 0) },
            new[]
            {
                new TreeLayerPlacement(10, "db:1", 0),
                new TreeLayerPlacement(11, "db:1", 1),
                new TreeLayerPlacement(12, "db:1", 2),
                new TreeLayerPlacement(30, null, 1),
            });

        var merged = LayerTreeModel.Merge(defaultTree, PreferenceJson, new[] { 10, 11, 12, 30 });

        Assert.Equal("db:1", merged.FindLayer(12)!.ParentKey); // defaultTree の位置へ
        Assert.Equal(new[] { 30, 11, 10, 12 }, merged.EnumerateAllLayerIds());
    }

    [Fact]
    public void Merge_NewLayerInNewDbGroup_GroupChainIsRecreated()
    {
        // preference 保存後に admin が新グループ db:9 + layer 90 を追加したケース。
        // defaultTree の位置を再現するため db:9 ごとマージ結果に復元される
        var defaultTree = LayerTreeModel.Build(
            new[]
            {
                new TreeGroupDefinition("db:1", "賦課", null, 0),
                new TreeGroupDefinition("db:9", "新設", null, 1),
            },
            new[]
            {
                new TreeLayerPlacement(10, "db:1", 0),
                new TreeLayerPlacement(11, "db:1", 1),
                new TreeLayerPlacement(90, "db:9", 0),
                new TreeLayerPlacement(30, null, 2),
            });

        var merged = LayerTreeModel.Merge(defaultTree, PreferenceJson, new[] { 10, 11, 30, 90 });

        var g9 = merged.FindGroup("db:9");
        Assert.NotNull(g9);
        Assert.Equal("新設", g9!.Name);
        Assert.Equal("db:9", merged.FindLayer(90)!.ParentKey);
    }

    [Fact]
    public void Merge_NewLayerNotInDefaultTree_AppendedToRootTail()
    {
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), PreferenceJson, new[] { 10, 11, 30, 99 });

        var l = merged.FindLayer(99)!;
        Assert.Null(l.ParentKey);
        Assert.Equal(merged.RootNodes.Count - 1, l.Order);
    }

    [Fact]
    public void Merge_RemovedLayerInPreference_IsIgnored()
    {
        // layer 30 が削除済 (availableLayerIds に無い) → preference にあっても無視
        var merged = LayerTreeModel.Merge(BuildDefaultTree(), PreferenceJson, new[] { 10, 11 });

        Assert.Null(merged.FindLayer(30));
        Assert.Equal(new[] { 11, 10 }, merged.EnumerateAllLayerIds());
        // usr グループ自体は残る (空でも保持)
        Assert.NotNull(merged.FindGroup("usr:a1b2c3d4"));
    }

    [Fact]
    public void Merge_RemovedDbGroup_ContentsEvacuateToRoot()
    {
        // preference には db:1 があるが、DB 側で削除済 (defaultTree に無い)
        var defaultTree = LayerTreeModel.Build(
            Array.Empty<TreeGroupDefinition>(),
            new[]
            {
                new TreeLayerPlacement(10, null, 0),
                new TreeLayerPlacement(11, null, 1),
                new TreeLayerPlacement(30, null, 2),
            });

        var merged = LayerTreeModel.Merge(defaultTree, PreferenceJson, new[] { 10, 11, 30 });

        Assert.Null(merged.FindGroup("db:1")); // 消滅グループは復活しない
        Assert.Null(merged.FindLayer(11)!.ParentKey); // 中身はルートへ
        Assert.Null(merged.FindLayer(10)!.ParentKey);
        Assert.NotNull(merged.FindGroup("usr:a1b2c3d4")); // usr: は無関係に保持
        Assert.Equal("usr:a1b2c3d4", merged.FindLayer(30)!.ParentKey);
    }

    [Fact]
    public void Merge_RemovedDbGroup_NestedUserGroupSurvivesAtRoot()
    {
        // 消滅 db: グループの中の usr: 子グループは中身ごとルートへ退避
        const string pref = """
            {
              "groups": [
                { "key": "db:99", "name": "消滅", "parent": null, "order": 0, "expanded": true },
                { "key": "usr:deadbeef", "name": "生存", "parent": "db:99", "order": 0, "expanded": true }
              ],
              "layers": [
                { "layerId": 10, "parent": "usr:deadbeef", "order": 0 }
              ]
            }
            """;
        var defaultTree = LayerTreeModel.Build(
            Array.Empty<TreeGroupDefinition>(),
            new[] { new TreeLayerPlacement(10, null, 0) });

        var merged = LayerTreeModel.Merge(defaultTree, pref, new[] { 10 });

        Assert.Null(merged.FindGroup("db:99"));
        var usr = merged.FindGroup("usr:deadbeef");
        Assert.NotNull(usr);
        Assert.Null(usr!.ParentKey); // ルートへ
        Assert.Equal("usr:deadbeef", merged.FindLayer(10)!.ParentKey); // サブツリーは保持
    }

    // ---------------- MigrateFromFlatOrder ----------------

    [Fact]
    public void MigrateFromFlatOrder_SetsVisibleAndIntraGroupOrder()
    {
        // 旧 layer_order_v1 = [11, 30, 10] (11 が最前面)。10/11 は db:1 内なので
        // グループ内の相対順序のみ反映され 11 → 10 になる。30 はルートで位置不変。
        var migrated = LayerTreeModel.MigrateFromFlatOrder(BuildDefaultTree(), new[] { 11, 30, 10 });

        Assert.True(migrated.FindLayer(10)!.Visible);
        Assert.True(migrated.FindLayer(11)!.Visible);
        Assert.True(migrated.FindLayer(30)!.Visible);
        // ツリー構造は defaultTree のまま (db:1 が先、ルート layer30 が後)
        Assert.Equal(new[] { 11, 10, 30 }, migrated.EnumerateVisibleLayerIdsDfs());
        Assert.Equal("db:1", migrated.FindLayer(11)!.ParentKey);
    }

    [Fact]
    public void MigrateFromFlatOrder_LayersNotInFlatOrder_BecomeInvisibleAndKeepPosition()
    {
        var migrated = LayerTreeModel.MigrateFromFlatOrder(BuildDefaultTree(), new[] { 30 });

        Assert.False(migrated.FindLayer(10)!.Visible);
        Assert.False(migrated.FindLayer(11)!.Visible);
        Assert.True(migrated.FindLayer(30)!.Visible);
        Assert.Equal(new[] { 30 }, migrated.EnumerateVisibleLayerIdsDfs());
        // 構造と順序は不変
        Assert.Equal(new[] { 10, 11, 30 }, migrated.EnumerateAllLayerIds());
    }

    [Fact]
    public void MigrateFromFlatOrder_UnknownLayerIds_AreIgnored()
    {
        var migrated = LayerTreeModel.MigrateFromFlatOrder(BuildDefaultTree(), new[] { 999, 10 });

        Assert.Null(migrated.FindLayer(999));
        Assert.Equal(new[] { 10 }, migrated.EnumerateVisibleLayerIdsDfs());
    }

    [Fact]
    public void MigrateFromFlatOrder_EmptyFlatOrder_AllInvisible()
    {
        var migrated = LayerTreeModel.MigrateFromFlatOrder(BuildDefaultTree(), Array.Empty<int>());

        Assert.Empty(migrated.EnumerateVisibleLayerIdsDfs());
        Assert.Equal(new[] { 10, 11, 30 }, migrated.EnumerateAllLayerIds());
    }
}
