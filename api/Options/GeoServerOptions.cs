namespace AgriGis.Api.Options;

// D101/D201 (WD1/WD2): appsettings.json: GeoServer { BaseUrl, AdminUser, AdminPasswordEnv, Workspace }
public sealed class GeoServerOptions
{
    public const string SectionName = "GeoServer";

    public string BaseUrl { get; set; } = "http://geoserver:8080/geoserver";
    public string AdminUser { get; set; } = "admin";
    public string AdminPasswordEnv { get; set; } = "AGRI_GIS_GEOSERVER_ADMIN_PASSWORD";
    public string Workspace { get; set; } = "agrigis";

    /// <summary>環境変数から admin password を解決する (or デフォルト)。</summary>
    public string ResolveAdminPassword() =>
        Environment.GetEnvironmentVariable(AdminPasswordEnv) ?? "geoserver_dev";
}
