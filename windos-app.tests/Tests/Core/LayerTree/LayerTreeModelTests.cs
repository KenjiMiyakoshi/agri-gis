using AgriGis.Desktop.Core.LayerTree;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Core.LayerTree;

// LG203 (Phase LG WLG2): LayerTreeModel の構造操作 / DFS z-order / 3 値 CheckState /
// シリアライズ往復のテスト。マージ規則は LayerTreeMergeTests を参照。
public sealed class LayerTreeModelTests
{
    // テスト用ツリー:
    // root
    // ├─ db:1 "賦課"
    // │   ├─ layer 10
    // │   ├─ db:2 "詳細"
    // │   │   └─ layer 20
    // │   └─ layer 11
    // └─ layer 30
    private static LayerTreeModel BuildNestedTree()
        => LayerTreeModel.Build(
            new[]
            {
                new TreeGroupDefinition("db:1", "賦課", null, 0),
                new TreeGroupDefinition("db:2", "詳細", "db:1", 1),
            },
            new[]
            {
                new TreeLayerPlacement(10, "db:1", 0),
                new TreeLayerPlacement(20, "db:2", 0),
                new TreeLayerPlacement(11, "db:1", 2),
                new TreeLayerPlacement(30, null, 1),
            });

    // ---------------- DFS z-order ----------------

    [Fact]
    public void EnumerateVisibleLayerIdsDfs_NestedTwoLevels_SkipsInvisible()
    {
        var model = BuildNestedTree();
        model.SetLayerVisible(10, true);
        model.SetLayerVisible(20, true);
        model.SetLayerVisible(11, false); // 非表示混在
        model.SetLayerVisible(30, true);

        Assert.Equal(new[] { 10, 20, 30 }, model.EnumerateVisibleLayerIdsDfs());
    }

    [Fact]
    public void EnumerateVisibleLayerIdsDfs_AllVisible_FollowsTreeDfsOrder()
    {
        var model = BuildNestedTree();
        foreach (var id in model.EnumerateAllLayerIds()) model.SetLayerVisible(id, true);

        // db:1 → layer10 → db:2 → layer20 → layer11 → layer30 の DFS 順
        Assert.Equal(new[] { 10, 20, 11, 30 }, model.EnumerateVisibleLayerIdsDfs());
    }

    [Fact]
    public void EnumerateAllLayerIds_ReturnsDfsOrderRegardlessOfVisibility()
    {
        var model = BuildNestedTree();
        Assert.Equal(new[] { 10, 20, 11, 30 }, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void EnumerateVisibleLayerIdsDfs_EmptyTree_ReturnsEmpty()
    {
        var model = new LayerTreeModel();
        Assert.Empty(model.EnumerateVisibleLayerIdsDfs());
        Assert.Empty(model.RootNodes);
    }

    // ---------------- GetGroupCheckState 3 値 ----------------

    [Fact]
    public void GetGroupCheckState_AllDescendantsVisible_Checked()
    {
        var model = BuildNestedTree();
        model.SetLayerVisible(10, true);
        model.SetLayerVisible(20, true);
        model.SetLayerVisible(11, true);

        Assert.Equal(GroupCheckState.Checked, model.GetGroupCheckState("db:1"));
    }

    [Fact]
    public void GetGroupCheckState_NoneVisible_Unchecked()
    {
        var model = BuildNestedTree();
        Assert.Equal(GroupCheckState.Unchecked, model.GetGroupCheckState("db:1"));
    }

    [Fact]
    public void GetGroupCheckState_PartiallyVisible_Mixed()
    {
        var model = BuildNestedTree();
        model.SetLayerVisible(20, true); // 孫レイヤのみ ON

        Assert.Equal(GroupCheckState.Mixed, model.GetGroupCheckState("db:1"));
        Assert.Equal(GroupCheckState.Checked, model.GetGroupCheckState("db:2"));
    }

    [Fact]
    public void GetGroupCheckState_NoDescendantLayers_Unchecked()
    {
        var model = new LayerTreeModel();
        var key = model.CreateUserGroup("空グループ", null);

        Assert.Equal(GroupCheckState.Unchecked, model.GetGroupCheckState(key));
    }

    // ---------------- SetGroupVisible ----------------

    [Fact]
    public void SetGroupVisible_TogglesAllDescendantLayers()
    {
        var model = BuildNestedTree();
        model.SetGroupVisible("db:1", true);

        Assert.True(model.FindLayer(10)!.Visible);
        Assert.True(model.FindLayer(20)!.Visible); // 孫も一括
        Assert.True(model.FindLayer(11)!.Visible);
        Assert.False(model.FindLayer(30)!.Visible); // グループ外は不変

        model.SetGroupVisible("db:1", false);
        Assert.Equal(GroupCheckState.Unchecked, model.GetGroupCheckState("db:1"));
    }

    // ---------------- MoveGroup / MoveLayer ----------------

    [Fact]
    public void MoveGroup_IntoItself_Throws()
    {
        var model = BuildNestedTree();
        Assert.Throws<InvalidOperationException>(() => model.MoveGroup("db:1", "db:1", 0));
    }

    [Fact]
    public void MoveGroup_IntoOwnDescendant_Throws()
    {
        var model = BuildNestedTree();
        Assert.Throws<InvalidOperationException>(() => model.MoveGroup("db:1", "db:2", 0));
    }

    [Fact]
    public void MoveGroup_ToRoot_Reparents()
    {
        var model = BuildNestedTree();
        model.MoveGroup("db:2", null, 0);

        var g = model.FindGroup("db:2")!;
        Assert.Null(g.ParentKey);
        Assert.Equal(0, g.Order);
        // DFS 順が変わる: db:2 (layer20) が先頭へ
        Assert.Equal(new[] { 20, 10, 11, 30 }, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void MoveLayer_ToAnotherGroupAtOrder_UpdatesParentAndOrder()
    {
        var model = BuildNestedTree();
        model.MoveLayer(30, "db:2", 0);

        var l = model.FindLayer(30)!;
        Assert.Equal("db:2", l.ParentKey);
        Assert.Equal(0, l.Order);
        Assert.Equal(new[] { 10, 30, 20, 11 }, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void MoveLayer_UnknownLayer_Throws()
    {
        var model = BuildNestedTree();
        Assert.Throws<KeyNotFoundException>(() => model.MoveLayer(999, null, 0));
    }

    // ---------------- LGP201: MoveLayers (複数アトミック移動) ----------------

    [Fact]
    public void MoveLayers_PreservesInputOrder_NotTreeOrder()
    {
        var model = BuildNestedTree();
        // 入力順 = 11, 10 (ツリー DFS では 10 が先)。入力順が保持されるべき
        model.MoveLayers(new[] { 11, 10 }, null, 0);

        Assert.Null(model.FindLayer(11)!.ParentKey);
        Assert.Null(model.FindLayer(10)!.ParentKey);
        Assert.Equal(0, model.FindLayer(11)!.Order);
        Assert.Equal(1, model.FindLayer(10)!.Order);
        // ルート先頭に 11, 10 が入力順で並ぶ
        Assert.Equal(new[] { 11, 10, 20, 30 }, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void MoveLayers_AcrossGroups_GathersIntoTargetGroupFromStartOrder()
    {
        var model = BuildNestedTree();
        // 30 (ルート) と 20 (db:2) を db:1 の index 1 から連番で集約
        model.MoveLayers(new[] { 30, 20 }, "db:1", 1);

        Assert.Equal("db:1", model.FindLayer(30)!.ParentKey);
        Assert.Equal("db:1", model.FindLayer(20)!.ParentKey);
        // db:1 の子: [10, 30, 20, db:2, 11]? db:2 は空に。挿入は除去後の子に対して index 1 起点
        var db1 = model.FindGroup("db:1")!;
        var childLayerIds = db1.Children.OfType<TreeLayerNode>().Select(l => l.LayerId).ToList();
        Assert.Equal(new[] { 30, 20 }, childLayerIds.Where(id => id is 30 or 20).ToList());
        Assert.Equal(1, model.FindLayer(30)!.Order);
        Assert.Equal(2, model.FindLayer(20)!.Order);
        Assert.Empty(model.FindGroup("db:2")!.Children); // 20 が抜けて空
    }

    [Fact]
    public void MoveLayers_SkipsUnknownIds_ProcessesRest()
    {
        var model = BuildNestedTree();
        model.MoveLayers(new[] { 10, 999, 30 }, "db:2", 0);

        Assert.Equal("db:2", model.FindLayer(10)!.ParentKey);
        Assert.Equal("db:2", model.FindLayer(30)!.ParentKey);
        // 入力順 10, (999 無視), 30 が index 0 起点で挿入され、既存 20 は後ろへ押し出される
        Assert.Equal(0, model.FindLayer(10)!.Order);
        Assert.Equal(1, model.FindLayer(30)!.Order);
        Assert.Equal(2, model.FindLayer(20)!.Order);
    }

    [Fact]
    public void MoveLayers_AllUnknownIds_NoOp()
    {
        var model = BuildNestedTree();
        var before = model.EnumerateAllLayerIds();

        model.MoveLayers(new[] { 998, 999 }, null, 0);

        Assert.Equal(before, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void MoveLayers_EmptyInput_NoOp()
    {
        var model = BuildNestedTree();
        var before = model.EnumerateAllLayerIds();

        model.MoveLayers(Array.Empty<int>(), "db:1", 0);

        Assert.Equal(before, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void MoveLayers_DuplicateIds_FirstWins()
    {
        var model = BuildNestedTree();
        model.MoveLayers(new[] { 30, 30 }, "db:1", 0);

        var db1 = model.FindGroup("db:1")!;
        // 30 は 1 度だけ挿入される
        Assert.Single(db1.Children.OfType<TreeLayerNode>().Where(l => l.LayerId == 30));
        Assert.Equal(0, model.FindLayer(30)!.Order);
    }

    [Fact]
    public void MoveLayers_StartOrderBeyondEnd_ClampsToEnd()
    {
        var model = BuildNestedTree();
        // ルートには db:1, layer30 の 2 ノード。除去後 startOrder=99 は末尾へ clamp
        model.MoveLayers(new[] { 10, 11 }, null, 99);

        Assert.Equal(new[] { 20, 30, 10, 11 }, model.EnumerateAllLayerIds());
        Assert.Equal(2, model.FindLayer(10)!.Order);
        Assert.Equal(3, model.FindLayer(11)!.Order);
    }

    [Fact]
    public void MoveLayers_NegativeStartOrder_ClampsToZero()
    {
        var model = BuildNestedTree();
        model.MoveLayers(new[] { 30 }, "db:1", -5);

        Assert.Equal(0, model.FindLayer(30)!.Order);
        Assert.Equal("db:1", model.FindLayer(30)!.ParentKey);
    }

    [Fact]
    public void MoveLayers_UnknownParentKey_Throws()
    {
        var model = BuildNestedTree();
        Assert.Throws<KeyNotFoundException>(() => model.MoveLayers(new[] { 10 }, "db:999", 0));
    }

    [Fact]
    public void MoveLayer_SingleDelegation_MatchesMoveLayersBehavior()
    {
        var a = BuildNestedTree();
        var b = BuildNestedTree();

        a.MoveLayer(30, "db:2", 0);
        b.MoveLayers(new[] { 30 }, "db:2", 0);

        Assert.Equal(a.EnumerateAllLayerIds(), b.EnumerateAllLayerIds());
        Assert.Equal(a.FindLayer(30)!.ParentKey, b.FindLayer(30)!.ParentKey);
        Assert.Equal(a.FindLayer(30)!.Order, b.FindLayer(30)!.Order);
    }

    [Fact]
    public void MoveLayers_SameParentReorder_ContiguousFromStartOrder()
    {
        var model = BuildNestedTree();
        // db:1 内: [10, db:2, 11]。10 と 11 を index 0 起点で先頭へ寄せる
        model.MoveLayers(new[] { 11, 10 }, "db:1", 0);

        var db1 = model.FindGroup("db:1")!;
        var ids = db1.Children.Select(c => c is TreeLayerNode l ? l.LayerId : -1).ToList();
        // 11, 10 が先頭、db:2 (-1) が後ろ
        Assert.Equal(new[] { 11, 10, -1 }, ids);
        Assert.Equal(0, model.FindLayer(11)!.Order);
        Assert.Equal(1, model.FindLayer(10)!.Order);
    }

    // ---------------- RemoveGroup ----------------

    [Fact]
    public void RemoveGroup_EvacuatesLayersAndChildGroupsToParent()
    {
        var model = BuildNestedTree();
        model.RemoveGroup("db:1");

        Assert.Null(model.FindGroup("db:1"));
        // 中身 (layer10 / db:2 / layer11) は元の位置 (ルート先頭) へ順序維持で退避
        Assert.Equal(new[] { 10, 20, 11, 30 }, model.EnumerateAllLayerIds());
        Assert.Null(model.FindLayer(10)!.ParentKey);
        Assert.Null(model.FindGroup("db:2")!.ParentKey);
        Assert.Equal("db:2", model.FindLayer(20)!.ParentKey); // 子グループの中身は保持
    }

    [Fact]
    public void RemoveGroup_NestedGroup_EvacuatesToItsParentGroup()
    {
        var model = BuildNestedTree();
        model.RemoveGroup("db:2");

        Assert.Null(model.FindGroup("db:2"));
        Assert.Equal("db:1", model.FindLayer(20)!.ParentKey);
        // db:2 のあった位置 (db:1 内 index 1) に layer20 が入る
        Assert.Equal(new[] { 10, 20, 11, 30 }, model.EnumerateAllLayerIds());
    }

    // ---------------- CreateUserGroup / フラグ ----------------

    [Fact]
    public void CreateUserGroup_ReturnsUsrPrefixedRandomHexKey()
    {
        var model = new LayerTreeModel();
        var key = model.CreateUserGroup("自分用", null);

        Assert.Matches("^usr:[0-9a-f]{8}$", key);
        var g = model.FindGroup(key)!;
        Assert.Equal("自分用", g.Name);
        Assert.True(g.Expanded);

        var key2 = model.CreateUserGroup("もう一つ", key);
        Assert.NotEqual(key, key2);
        Assert.Equal(key, model.FindGroup(key2)!.ParentKey);
    }

    [Fact]
    public void SetLayerFlags_PartialUpdate_LeavesOtherFlagUntouched()
    {
        var model = BuildNestedTree();
        model.SetLayerFlags(10, edit: true);
        Assert.True(model.FindLayer(10)!.EditEnabled);
        Assert.False(model.FindLayer(10)!.SnapEnabled);

        model.SetLayerFlags(10, snap: true);
        Assert.True(model.FindLayer(10)!.EditEnabled);
        Assert.True(model.FindLayer(10)!.SnapEnabled);

        // 未知 id は寛容に無視 (落ちない)
        model.SetLayerFlags(999, edit: true);
        model.SetLayerVisible(999, true);
    }

    // ---------------- シリアライズ往復 ----------------

    [Fact]
    public void ToPreferenceJson_RoundTrip_PreservesStructure()
    {
        var model = BuildNestedTree();
        model.SetGroupExpanded("db:2", false);

        var restored = LayerTreeModel.FromPreferenceJson(model.ToPreferenceJson());

        Assert.Equal(model.EnumerateAllLayerIds(), restored.EnumerateAllLayerIds());
        var g1 = restored.FindGroup("db:1")!;
        var g2 = restored.FindGroup("db:2")!;
        Assert.Equal("賦課", g1.Name);
        Assert.Null(g1.ParentKey);
        Assert.True(g1.Expanded);
        Assert.Equal("db:1", g2.ParentKey);
        Assert.False(g2.Expanded);
        Assert.Equal("db:1", restored.FindLayer(10)!.ParentKey);
        Assert.Equal("db:2", restored.FindLayer(20)!.ParentKey);
        Assert.Null(restored.FindLayer(30)!.ParentKey);
        Assert.Equal(model.FindLayer(11)!.Order, restored.FindLayer(11)!.Order);
    }

    [Fact]
    public void ToPreferenceJson_DoesNotContainVisibleOrFlags()
    {
        var model = BuildNestedTree();
        model.SetLayerVisible(10, true);
        model.SetLayerFlags(10, edit: true, snap: true);

        var json = model.ToPreferenceJson();

        // visible はセッション状態、edit/snap は layer_flags_v1 に分離するためツリー JSON に含めない
        Assert.DoesNotContain("visible", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("edit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("snap", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromPreferenceJson_InvalidJson_ReturnsEmptyModel()
    {
        Assert.Empty(LayerTreeModel.FromPreferenceJson("{ not valid json").EnumerateAllLayerIds());
        Assert.Empty(LayerTreeModel.FromPreferenceJson(null).EnumerateAllLayerIds());
        Assert.Empty(LayerTreeModel.FromPreferenceJson("").EnumerateAllLayerIds());
        Assert.Empty(LayerTreeModel.FromPreferenceJson("[1,2,3]").RootNodes);
    }

    [Fact]
    public void FromPreferenceJson_MissingFields_IsTolerant()
    {
        // key 欠損グループ / layerId 欠損レイヤは無視、parent/order/expanded 欠損はデフォルト値
        const string json = """
            {
              "groups": [
                { "name": "key無し" },
                { "key": "usr:aaaa1111", "name": "正常" },
                { "key": "usr:bbbb2222", "parent": "usr:存在しない" }
              ],
              "layers": [
                { "parent": "usr:aaaa1111" },
                { "layerId": 5, "parent": "usr:aaaa1111" },
                { "layerId": 6 }
              ]
            }
            """;

        var model = LayerTreeModel.FromPreferenceJson(json);

        Assert.Equal(2, model.EnumerateGroups().Count);
        var g = model.FindGroup("usr:aaaa1111")!;
        Assert.True(g.Expanded); // 欠損 → true
        Assert.Null(model.FindGroup("usr:bbbb2222")!.ParentKey); // 未知 parent → ルート
        Assert.Equal("usr:aaaa1111", model.FindLayer(5)!.ParentKey);
        Assert.Null(model.FindLayer(6)!.ParentKey);
        Assert.Equal(new[] { 5, 6 }, model.EnumerateAllLayerIds());
    }

    [Fact]
    public void FromPreferenceJson_CyclicParents_FallBackToRoot()
    {
        const string json = """
            {
              "groups": [
                { "key": "usr:aaaa0001", "name": "A", "parent": "usr:bbbb0002" },
                { "key": "usr:bbbb0002", "name": "B", "parent": "usr:aaaa0001" }
              ],
              "layers": []
            }
            """;

        var model = LayerTreeModel.FromPreferenceJson(json);

        // 循環は両者ともルートへ退避 (落ちない)
        Assert.Equal(2, model.EnumerateGroups().Count);
        Assert.All(model.EnumerateGroups(), g => Assert.Null(g.ParentKey));
    }
}
