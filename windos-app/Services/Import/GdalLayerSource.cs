using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.InferenceStrategies;
using AgriGis.Desktop.Services.Import.Packages;
using AgriGis.Desktop.Services.Import.Srid;
using OSGeo.OGR;
using OSGeo.OSR;

namespace AgriGis.Desktop.Services.Import;

// WC1 C102 / WC2 C302: ILayerSource (Phase B 確立) を実装する Shapefile/MIF/TAB 用 LayerSource。
//
// 設計判断 (PHASE_C_DESIGN_P §3 / §6):
//   - sourceFormat ctor 引数で driver 名を切替 (Phase C は "shapefile" のみ、
//     Phase C' で "MapInfo File" を渡せば MIF/TAB に再利用可能)
//   - 1 クラスに集約 (案 C 採用)
//   - 文字コードは IEncodingResolver で解決 → Ogr.OpenEx(path, ..., new[]{"ENCODING=..."}) で渡す
//     (環境変数 SHAPE_ENCODING 不使用、Design 決定 6)
//   - SRID 検出は ISridDetector に委譲、結果は SourceSrid / SridState に反映
//   - DisposeAsync で OGR DataSource → ShapefilePackage の連鎖解放
//   - WC2 C302: ReadFeaturesAsync は Geometry.ExportToJson() 経由で GeoJSON 化、
//     Polygon/LineString は Multi 正規化 (案 P §6.5)、Z/M 値は X/Y のみで skip + WARN、
//     IAsyncEnumerable で逐次 yield
public sealed class GdalLayerSource : ILayerSource
{
    private readonly ShapefilePackage _package;
    private readonly ISridDetector _sridDetector;
    private readonly IEncodingResolver _encodingResolver;
    private readonly string _sourceFormat;

    private DataSource? _dataSource;
    private int? _detectedSrid;
    private SridResolutionState? _sridState;
    private CoordinateTransformation? _sourceToTargetTransform;
    private SpatialReference? _sourceSrs;
    private SpatialReference? _targetSrs;

    private readonly List<string> _warnings = new();
    /// <summary>OGR 読み込み中の警告 (Z/M drop / feature skip 等)。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>読み込み中に skip された feature 数 (案 P §6.14 論点 13)。</summary>
    public int SkippedFeatureCount { get; private set; }

    public GdalLayerSource(
        ShapefilePackage package,
        ISridDetector sridDetector,
        IEncodingResolver encodingResolver,
        string sourceFormat = "shapefile")
    {
        _package = package;
        _sridDetector = sridDetector;
        _encodingResolver = encodingResolver;
        _sourceFormat = sourceFormat;
    }

    public string SourceFormat => _sourceFormat;

    public int? SourceSrid => _detectedSrid;

    public SridResolutionState? SridState => _sridState;

    public async Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        var layer = _dataSource!.GetLayerByIndex(0);
        return GdalInferenceStrategy.Infer(layer, _warnings);
    }

    public async IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(
        int targetSrid,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (targetSrid != 4326)
        {
            throw new NotSupportedException(
                $"GdalLayerSource: only 4326 target is supported, got {targetSrid}.");
        }

        await EnsureOpenAsync(ct);
        var layer = _dataSource!.GetLayerByIndex(0);
        var defn = layer.GetLayerDefn();
        var fieldCount = defn.GetFieldCount();
        var fieldNames = new string[fieldCount];
        var fieldTypes = new FieldType[fieldCount];
        var fieldSubTypes = new FieldSubType[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            var fd = defn.GetFieldDefn(i);
            fieldNames[i] = fd.GetName();
            fieldTypes[i] = fd.GetFieldType();
            fieldSubTypes[i] = fd.GetSubType();
        }

        layer.ResetReading();
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Feature? feat;
            try
            {
                feat = layer.GetNextFeature();
            }
            catch (Exception ex)
            {
                _warnings.Add($"GetNextFeature failed; skipping: {ex.Message}");
                SkippedFeatureCount++;
                continue;
            }
            if (feat is null) break;

            GeoJsonFeature? converted;
            try
            {
                converted = ConvertFeature(feat, fieldNames, fieldTypes, fieldSubTypes);
            }
            catch (Exception ex)
            {
                _warnings.Add($"Feature {feat.GetFID()} skipped: {ex.Message}");
                SkippedFeatureCount++;
                feat.Dispose();
                continue;
            }
            feat.Dispose();

            if (converted is not null)
            {
                yield return converted;
            }
        }
    }

    private GeoJsonFeature? ConvertFeature(Feature feat, string[] fieldNames,
        FieldType[] fieldTypes, FieldSubType[] fieldSubTypes)
    {
        using var geom = feat.GetGeometryRef();
        if (geom is null) return null;

        // Z/M 値 drop (Design P §6.5 論点 13)
        if (geom.GetCoordinateDimension() > 2)
        {
            _warnings.Add($"Z/M values dropped at feature {feat.GetFID()}");
            geom.FlattenTo2D();
        }

        // ソース EPSG → 4326 への座標変換 (smoke test 5件目バグ回避)。
        // AssumedWgs84 / source==4326 の場合は _sourceToTargetTransform=null で skip。
        if (_sourceToTargetTransform is not null)
        {
            var ret = geom.Transform(_sourceToTargetTransform);
            if (ret != 0)
            {
                throw new InvalidOperationException(
                    $"OGR Geometry.Transform failed for feature {feat.GetFID()} " +
                    $"(source EPSG={_detectedSrid}, target=4326, code={ret})");
            }
        }

        // MultiPolygon / MultiLineString 正規化 (案 C 採用、§6.5 論点 8)
        var promoted = PromoteToMulti(geom);
        var disposePromoted = !ReferenceEquals(promoted, geom);

        string geomJson;
        try
        {
            geomJson = promoted.ExportToJson(Array.Empty<string>());
        }
        finally
        {
            if (disposePromoted) promoted.Dispose();
        }

        using var geomDoc = JsonDocument.Parse(geomJson);
        var geometry = geomDoc.RootElement.Clone();

        var props = new Dictionary<string, JsonElement>(fieldNames.Length);
        for (int i = 0; i < fieldNames.Length; i++)
        {
            var name = fieldNames[i];
            var idx = feat.GetFieldIndex(name);
            if (idx < 0) continue;
            if (!feat.IsFieldSet(idx) || feat.IsFieldNull(idx))
            {
                using var nullDoc = JsonDocument.Parse("null");
                props[name] = nullDoc.RootElement.Clone();
                continue;
            }
            // OGR FieldType に応じた JSON 値で組み立てる (API スキーマバリデーションと整合)
            var jsonLiteral = BuildFieldJsonLiteral(feat, idx, fieldTypes[i], fieldSubTypes[i]);
            using var pdoc = JsonDocument.Parse(jsonLiteral);
            props[name] = pdoc.RootElement.Clone();
        }

        return new GeoJsonFeature(geometry, props);
    }

    /// <summary>
    /// OGR Feature の特定フィールドを、`InferredField.Type` (integer/number/string/date/boolean)
    /// と整合する JSON リテラルに変換する。
    /// </summary>
    private static string BuildFieldJsonLiteral(Feature feat, int idx, FieldType type, FieldSubType subType)
    {
        switch (type)
        {
            case FieldType.OFTInteger:
            case FieldType.OFTInteger64:
                if (subType == FieldSubType.OFSTBoolean)
                {
                    return feat.GetFieldAsInteger(idx) != 0 ? "true" : "false";
                }
                return feat.GetFieldAsInteger64(idx).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case FieldType.OFTReal:
                return feat.GetFieldAsDouble(idx).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            case FieldType.OFTDate:
            case FieldType.OFTDateTime:
                // GetFieldAsDateTime は (year, month, day, hour, minute, second, tzFlag) を out で返す
                int year = 0, month = 0, day = 0, hour = 0, minute = 0, tzFlag = 0;
                float seconds = 0f;
                feat.GetFieldAsDateTime(idx, out year, out month, out day, out hour, out minute, out seconds, out tzFlag);
                return JsonSerializer.Serialize($"{year:D4}-{month:D2}-{day:D2}");

            default:
                // OFTString / OFTBinary / *List 等は文字列扱い
                return JsonSerializer.Serialize(feat.GetFieldAsString(idx));
        }
    }

    /// <summary>
    /// Polygon → MultiPolygon (1 リング)、LineString → MultiLineString (1 ライン) に昇格。
    /// Point/MultiPoint/既に Multi のものはそのまま返す。
    /// </summary>
    internal static Geometry PromoteToMulti(Geometry input)
    {
        var t = input.GetGeometryType();
        switch (t)
        {
            case wkbGeometryType.wkbPolygon:
            case wkbGeometryType.wkbPolygon25D:
            {
                var mp = new Geometry(wkbGeometryType.wkbMultiPolygon);
                mp.AddGeometry(input);
                return mp;
            }
            case wkbGeometryType.wkbLineString:
            case wkbGeometryType.wkbLineString25D:
            {
                var ml = new Geometry(wkbGeometryType.wkbMultiLineString);
                ml.AddGeometry(input);
                return ml;
            }
            default:
                return input;
        }
    }

    private ValueTask EnsureOpenAsync(CancellationToken ct)
    {
        if (_dataSource is not null) return ValueTask.CompletedTask;
        return EnsureOpenInternalAsync(ct);
    }

    private async ValueTask EnsureOpenInternalAsync(CancellationToken ct)
    {
        // 1. SRID 検出
        var sridResult = await _sridDetector.DetectAsync(_package, ct);
        _detectedSrid = sridResult.Srid;
        _sridState = sridResult.State;

        if (sridResult.State == SridResolutionState.Rejected)
        {
            throw new InvalidOperationException(
                ".prj 不在または SRID 検出失敗 (SridFallbackPolicy=Reject)");
        }

        // 2. 文字コード解決 → .cpg fallback 書き込み (OGR が .cpg を自動検出するため、
        //    .cpg が無いケースでは temp dir に .cpg を生成する。これで
        //    プロセス環境変数 SHAPE_ENCODING を使わずにエンコーディングを指定できる
        //    Design 決定 6 と整合)
        var encoding = _encodingResolver.Resolve(_package);
        if (string.IsNullOrEmpty(_package.CpgPath))
        {
            var generated = Path.ChangeExtension(_package.ShpPath, ".cpg");
            await File.WriteAllTextAsync(generated, encoding, ct);
        }

        // 3. OGR Open (driver 名で限定して開く)
        var driverName = _sourceFormat switch
        {
            "shapefile" => "ESRI Shapefile",
            "mif" or "tab" => "MapInfo File",
            _ => throw new NotSupportedException($"Unsupported source format: {_sourceFormat}")
        };
        var driver = Ogr.GetDriverByName(driverName);
        if (driver is null)
            throw new InvalidOperationException($"OGR driver not found: {driverName}");

        _dataSource = driver.Open(_package.ShpPath, 0);  // 0 = read-only

        if (_dataSource is null)
        {
            throw new InvalidOperationException(
                $"Failed to open shapefile: {_package.ShpPath} (encoding={encoding})");
        }

        if (_package.MissingOptionalExtensions.Count > 0)
        {
            Trace.WriteLine(
                $"[GdalLayerSource] missing optional sidecars: {string.Join(',', _package.MissingOptionalExtensions)}");
        }

        // 4. source EPSG → 4326 への CoordinateTransformation を準備する。
        //    AssumedWgs84 と source==4326 は変換不要。それ以外 (Detected / FallbackToPrompt
        //    で手動 SRID が指定された) では OGR の OSR で transform を組み立てる。
        if (_sridState != SridResolutionState.FallbackToWgs84
            && _detectedSrid.HasValue
            && _detectedSrid.Value != 4326)
        {
            _sourceSrs = new SpatialReference("");
            var importResult = _sourceSrs.ImportFromEPSG(_detectedSrid.Value);
            if (importResult != 0)
            {
                throw new InvalidOperationException(
                    $"OSR ImportFromEPSG({_detectedSrid.Value}) failed (code={importResult}). " +
                    $"This SRID is not known to GDAL's proj database.");
            }
            _targetSrs = new SpatialReference("");
            _targetSrs.ImportFromEPSG(4326);
            // PostGIS 等と整合させるため Authority 順 (lat,lon for 4326) ではなく
            // GeoJSON 仕様の lon,lat 順を強制する。
            _sourceSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            _targetSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            _sourceToTargetTransform = new CoordinateTransformation(_sourceSrs, _targetSrs);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sourceToTargetTransform?.Dispose();
        _sourceToTargetTransform = null;
        _sourceSrs?.Dispose();
        _sourceSrs = null;
        _targetSrs?.Dispose();
        _targetSrs = null;
        _dataSource?.Dispose();
        _dataSource = null;
        await _package.DisposeAsync();
    }

    // ----- internal static 純粋関数群 (C501 のテスト対象) -----

    /// <summary>
    /// OGR Open option 形式の文字列配列を組み立てる (`ENCODING=CP932` 等)。
    /// 現在は .cpg fallback 経路を採用しているため Ogr.Open 呼び出しでは未使用だが、
    /// 将来 Gdal.OpenEx を採用する経路への切替候補として残置 (テストは継続)。
    /// </summary>
    internal static string[] BuildOpenOptions(string encoding)
    {
        if (string.IsNullOrEmpty(encoding))
        {
            return Array.Empty<string>();
        }
        return new[] { $"ENCODING={encoding}" };
    }
}
