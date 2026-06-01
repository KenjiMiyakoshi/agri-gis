using AgriGis.Desktop.Core;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Conventions;

public sealed class CoreLayerIsolationTest
{
    // Core 名前空間配下の型シグネチャ (フィールド/プロパティ/メソッド引数・戻り値) に
    // System.Windows.Forms 由来の型が現れないことを検査する。
    //
    // 本テストはシグネチャしか見ないので、メソッド本体だけで Forms を呼ぶ違反は検知できない。
    // それでも「Core から Forms 参照禁止」要件の主要 8 割は守れる。
    [Fact]
    public void Core_does_not_reference_WindowsForms_in_signatures()
    {
        var coreAsm = typeof(LayerSchema).Assembly;
        var coreTypes = coreAsm.GetTypes()
            .Where(t => t.Namespace?.StartsWith("AgriGis.Desktop.Core") == true)
            .ToList();

        var violations = new List<string>();

        foreach (var t in coreTypes)
        {
            var refs = new HashSet<Type>();
            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                refs.Add(p.PropertyType);
            }
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                refs.Add(f.FieldType);
            }
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                refs.Add(m.ReturnType);
                foreach (var pr in m.GetParameters())
                {
                    refs.Add(pr.ParameterType);
                }
            }

            foreach (var r in refs)
            {
                var asmName = r.Assembly.GetName().Name;
                if (asmName == "System.Windows.Forms")
                {
                    violations.Add($"{t.FullName} -> {r.FullName}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Core types reference System.Windows.Forms in signatures:" + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
    }
}
