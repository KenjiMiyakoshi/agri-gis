using System.Text.Json;

namespace AgriGis.Api.Dto;

// D203 (WD2): admin theme CRUD 用 DTO
// 構造 (Phase D MVP):
// {
//   "themes": {
//     "default": { "fillColor": "#4CAF50", "fillOpacity": 0.5, "strokeColor": "#1B5E20", "strokeWidth": 1 },
//     "byOwner": { "categoryField": "owner_kind", "categories": { "A": {...}, "B": {...} } }
//   }
// }
// PUT で受領 → DB UPDATE → 各 theme について GeoServer に SLD POST
public sealed record LayerStyleDto(JsonElement StyleJson);
