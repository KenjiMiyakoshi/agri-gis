# 0501: `AgriGis.Desktop.csproj` 新規作成と依存導入

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 0.5d |
| Depends on | なし |
| Blocks | 0502, 0503, 0504, 0505 |

## 概要
WinForms クライアントのスケルトンプロジェクトを `windos-app/` 配下に立ち上げる。

## 背景・目的
案 B' の WinForms 単一プロジェクト構成 (`net8.0-windows`, `UseWindowsForms=true`) を確立し、後続イシューでフォルダ規約に従って中身を作れるようにする。

## スコープ
### 含む
- `windos-app/AgriGis.Desktop.csproj`
  - `TargetFramework=net8.0-windows`
  - `UseWindowsForms=true`
  - `Nullable=enable`, `ImplicitUsings=enable`
  - `RootNamespace=AgriGis.Desktop`
  - PackageReference:
    - `Microsoft.Web.WebView2` (最新安定)
    - `Microsoft.Extensions.DependencyInjection` (8.x)
    - `Microsoft.Extensions.Http` (8.x)
- `Program.cs` で `ApplicationConfiguration.Initialize()` + DI コンテナ + `MainForm` 起動の最小骨格
- `MainForm.cs` は空の Form（タイトル "AgriGis"）
- フォルダ作成: `Core/`, `Services/`, `Forms/`
- `ApplicationConfiguration.Initialize()` のために必要な `ApplicationConfiguration.cs` 自動生成設定確認
- `AgriGis.sln` に追加（0301 で作成想定）

### 含まない
- WebView2 のロード (0504)
- API クライアント (0503)
- Core ロジック (0502)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet build windos-app/AgriGis.Desktop.csproj` が通る
- [ ] `dotnet run --project windos-app` で空のフォームが立ち上がる
- [ ] `Core/`, `Services/`, `Forms/` ディレクトリが存在

## 影響ファイル
- `D:\proj\agri-gis\windos-app\AgriGis.Desktop.csproj` (新規)
- `D:\proj\agri-gis\windos-app\Program.cs` (新規)
- `D:\proj\agri-gis\windos-app\Forms\MainForm.cs` (新規, 空)
- `D:\proj\agri-gis\windos-app\Forms\MainForm.Designer.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\` (空ディレクトリ; .gitkeep)
- `D:\proj\agri-gis\windos-app\Services\` (空ディレクトリ; .gitkeep)

## 実装ノート
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>AgriGis.Desktop</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2592.51" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
  </ItemGroup>
</Project>
```

```csharp
// Program.cs
using Microsoft.Extensions.DependencyInjection;
using AgriGis.Desktop.Forms;

ApplicationConfiguration.Initialize();
var services = new ServiceCollection();
// services.AddHttpClient<IApiClient, ApiClient>(...) は 0503 で
var sp = services.BuildServiceProvider();
Application.Run(new MainForm());
```

注意点:
- ディレクトリ名は仕様文どおり `windos-app/`（typo 修正しない）
- WebView2 のランタイムは Edge 同梱なので個別インストール不要

## テスト観点
- 0505 で別プロジェクトで Core 単体 + ConventionTest
