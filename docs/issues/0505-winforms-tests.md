# 0505: `AgriGis.Desktop.Tests.csproj` + Core 単体 + ConventionTest

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 1d |
| Depends on | 0502 |
| Blocks | なし |

## 概要
WinForms Core ロジックの xUnit テストプロジェクトを立ち上げる。Core 単体と「Core から System.Windows.Forms 参照禁止」を確認する ConventionTest を含める。

## 背景・目的
案 B' は Core が純粋ロジックなので xUnit から自然にテストできる。Core が WinForms に依存し始めると CI が動かなくなるので、リフレクションでアーキ違反を機械的に検出する。

## スコープ
### 含む
- `AgriGis.Desktop.Tests/AgriGis.Desktop.Tests.csproj`
  - `TargetFramework=net8.0` (Windows 系 SDK 不要、Core だけテストするので)
  - もしくは `net8.0-windows` でも可（ConventionTest が Forms 型を「見ない」前提だけ崩さない）
  - PackageReference: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`
  - ProjectReference → `../windos-app/AgriGis.Desktop.csproj`
- `Tests/Core/AttributeValidatorTests.cs`
  - required 欠落で error
  - 型不一致で error
  - 合法属性は 0 件
- `Tests/Core/SchemaFormBuilderTests.cs`
  - FieldDescriptor が Kind を正しくマップ
- `Tests/Core/ProblemDetailsParserTests.cs`
  - `extensions.errors` 形 / `top-level errors` 形のどちらもパース可
  - status / title / requestId が取れる
- `Tests/Conventions/CoreLayerIsolationTest.cs`
  - リフレクションで `Assembly.Load("AgriGis.Desktop")` → `Types` をループ
  - `Namespace.StartsWith("AgriGis.Desktop.Core")` の型について
    - 参照しているアセンブリリスト (`Module.GetReferencedAssemblies` or `Type` を walk) に `System.Windows.Forms` を含まないことを検証
  - 含まれていたら test fail（型名を列挙）

### 含まない
- WinForms 統合テスト (UI 自動操作)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet test AgriGis.Desktop.Tests` が pass
- [ ] Core テスト 6 ケース以上
- [ ] ConventionTest が Core に Forms 参照を入れると fail することを手動で確認

## 影響ファイル
- `D:\proj\agri-gis\AgriGis.Desktop.Tests\AgriGis.Desktop.Tests.csproj` (新規)
- `D:\proj\agri-gis\AgriGis.Desktop.Tests\Tests\Core\AttributeValidatorTests.cs` (新規)
- `D:\proj\agri-gis\AgriGis.Desktop.Tests\Tests\Core\SchemaFormBuilderTests.cs` (新規)
- `D:\proj\agri-gis\AgriGis.Desktop.Tests\Tests\Core\ProblemDetailsParserTests.cs` (新規)
- `D:\proj\agri-gis\AgriGis.Desktop.Tests\Tests\Conventions\CoreLayerIsolationTest.cs` (新規)
- `D:\proj\agri-gis\AgriGis.sln` に追加

## 実装ノート
```csharp
// Conventions/CoreLayerIsolationTest.cs
public class CoreLayerIsolationTest
{
    [Fact]
    public void Core_does_not_reference_WindowsForms()
    {
        var asm = typeof(AgriGis.Desktop.Core.LayerSchema).Assembly;
        var coreTypes = asm.GetTypes().Where(t => t.Namespace?.StartsWith("AgriGis.Desktop.Core") == true);

        var bad = new List<string>();
        foreach (var t in coreTypes)
        {
            // フィールド / プロパティ / メソッドシグネチャに Forms 型が出ていないか走査
            var refs = new HashSet<Type>();
            foreach (var p in t.GetProperties()) refs.Add(p.PropertyType);
            foreach (var f in t.GetFields()) refs.Add(f.FieldType);
            foreach (var m in t.GetMethods())
            {
                refs.Add(m.ReturnType);
                foreach (var pr in m.GetParameters()) refs.Add(pr.ParameterType);
            }
            foreach (var r in refs)
                if (r.Assembly.GetName().Name == "System.Windows.Forms")
                    bad.Add($"{t.FullName} -> {r.FullName}");
        }
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }
}
```

注意点:
- ConventionTest はシグネチャしか見えないので、メソッド本体が `using System.Windows.Forms;` だけ書いた場合は素通りする。完璧ではないが要件「Core から Forms 参照禁止」の壁の 8 割は守れる
- より厳密にやるなら `AssemblyName.ReferencedAssemblies` を見るのも手だが、AgriGis.Desktop 全体としては Forms 参照しているので「Core 名前空間配下の型シグネチャ」で見る方式が現実解

## テスト観点
- Core ロジックの単体
- アーキ違反検知
