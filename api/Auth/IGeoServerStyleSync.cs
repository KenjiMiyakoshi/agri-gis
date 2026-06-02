namespace AgriGis.Api.Auth;

// D203 (WD2): admin が PUT した style_json を GeoServer の data_dir/styles に反映する。
// Phase D 採用案 §2.4: GeoServer REST API (/geoserver/rest/styles/...) に SLD XML を POST。
// SLD XML 変換は Phase D MVP では「fillColor / strokeColor のみ」を扱う最小スコープ。
// 失敗時は rollback (DB transaction) 用に bool を返す。
public interface IGeoServerStyleSync
{
    /// <summary>
    /// layer の theme スタイルを GeoServer に同期する。
    /// </summary>
    /// <returns>true=成功 / false=失敗 (caller が rollback)</returns>
    Task<bool> PushStyleAsync(int layerId, string themeName, string sldXml, CancellationToken ct);
}
