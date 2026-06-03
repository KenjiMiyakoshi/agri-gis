using AgriGis.Desktop.Services.Import.Encoding;
using AgriGis.Desktop.Services.Import.Packages;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import.Encoding;

// C'304 (WC'3): UcsDetectResolver の動作検証。
// ShapefilePackage は internal ctor で外部 new 不可のため、本テストでは
// IImportPackage 経由の fallback 経路と DummyPkg ベースで最低限の挙動を保証する。
// ShapefilePackage を実 zip で生成しての DBF 検出テストは Phase C''
// (テストインフラ整備 + 実 zip サンプル含め) に送り。
public sealed class UcsDetectResolverTests
{
    public UcsDetectResolverTests()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Resolve_NonShapefile_FallsBackToInner()
    {
        var fallback = new StubResolver("FALLBACK_VALUE");
        var resolver = new UcsDetectResolver(fallback);
        var pkg = new DummyPkg();
        Assert.Equal("FALLBACK_VALUE", resolver.Resolve(pkg));
    }

    [Fact]
    public void ConfidenceThreshold_IsConfigured()
    {
        // 採用しきい値 0.7 が定数として宣言されていること (将来 admin 設定化したときの起点)
        Assert.Equal(0.7f, UcsDetectResolver.ConfidenceThreshold);
    }

    private sealed class DummyPkg : IImportPackage
    {
        public string PrimaryPath => "x";
        public IReadOnlyList<string> MissingOptional => Array.Empty<string>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubResolver : IEncodingResolver
    {
        private readonly string _result;
        public StubResolver(string result) { _result = result; }
        public string Resolve(IImportPackage package) => _result;
    }
}
