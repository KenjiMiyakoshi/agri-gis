namespace AgriGis.Desktop.Dto;

// LG303 (Phase LG WLG3): api/Dto/LayerGroupDto.cs のミラー。
// GET /api/layer-groups のフラット一覧 (ツリー構築はクライアント側で parentGroupId を辿る)。
public sealed record LayerGroupDto(
    int GroupId,
    int? ParentGroupId,
    string GroupName,
    int SortOrder);

// POST /api/admin/layer-groups
public sealed record CreateLayerGroupRequestDto(
    string GroupName,
    int? ParentGroupId,
    int? SortOrder);

// PATCH /api/admin/layer-groups/{id}
// null のプロパティは JSON から省略され「変更なし」になる (JsonOpts の WhenWritingNull)。
// 注意: 「parentGroupId を明示 null (ルートへ移動)」は本 DTO では表現できない。
// 現状のクライアント用途は rename のみなので未対応 (必要になったら JsonElement 直送に切替)。
public sealed record UpdateLayerGroupRequestDto(
    string? GroupName,
    int? ParentGroupId,
    int? SortOrder);

// PUT /api/admin/layers/{layerId}/group (GroupId = null でルート直下)
public sealed record AssignLayerGroupRequestDto(
    int? GroupId,
    int SortOrder);

public sealed record LayerGroupAssignmentDto(
    int LayerId,
    int? GroupId,
    int SortOrder);
