namespace AgriGis.Api.Dto;

// LG102 (Phase LG WLG1): レイヤグループのフラット一覧 DTO。
// ツリー構築はクライアント側 (parentGroupId を辿る)。JSON は既定の camelCase。
public sealed record LayerGroupDto(
    int GroupId,
    int? ParentGroupId,
    string GroupName,
    int SortOrder);

// LG103: POST /api/admin/layer-groups
public sealed record CreateLayerGroupRequestDto(
    string GroupName,
    int? ParentGroupId,
    int? SortOrder);

// LG104: PUT /api/admin/layers/{layerId}/group
// GroupId = null でルート直下へ配置。
public sealed record AssignLayerGroupRequestDto(
    int? GroupId,
    int SortOrder);

// LG104 レスポンス
public sealed record LayerGroupAssignmentDto(
    int LayerId,
    int? GroupId,
    int SortOrder);
