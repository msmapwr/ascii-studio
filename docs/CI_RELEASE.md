# 自动编译与发布

工作流：`.github/workflows/build-release.yml`。

- main、feat／fix／refactor 分支推送、PR 与手动运行：x64 / ARM64 Release 编译，执行 xUnit 检查，上传自包含 ZIP、SHA256SUMS.txt、发布说明，保留 14 天。
- 推送 `v*` 标签：上述检查全部通过后创建 GitHub Release。普通标签成为最新版；alpha/beta/rc 为预发布。
- 发布版本必须与 csproj、MSIX manifest、Changelog 一致。失败不创建公开 Release。
- 先创建 Draft，上传成功再公开；重跑会继续未完成 Draft，不覆盖已经公开的 Release。
- 不需要额外仓库 Secret，使用 GitHub 自带 GITHUB_TOKEN。只有 Release job 拥有 contents:write；PR job 只读。
- Action 使用已核对的官方版本并固定到完整 SHA。不会运行本机 BuildAndRun.ps1 或要求安装桌面技能。

## 发布一个版本

先完成代码、版本号与 Changelog，再合并 main。确认 main 的 Build and Release 成功后：

```powershell
$releaseVersion = ([xml](Get-Content ./src/Charloom/Charloom.csproj -Raw)).Project.PropertyGroup.Version
git tag "v$releaseVersion"
git push origin "v$releaseVersion"
```

正式发布时使用正式版本标签，例如 `v0.6.0`。不要把未完成的功能标为正式版本。手动 Run workflow 只编译选中的 ref，不发布 Release。

## 使用下载文件

在 Releases 下载对应架构的 `Charloom-<版本>-win-x64.zip` 或 `Charloom-<版本>-win-arm64.zip`，解压完整目录后运行 `Charloom.exe`。目标 Windows 10 1809 及以后；包含 .NET 和 Windows App SDK 运行时。ZIP 未代码签名，当前不自动发布 MSIX。ARM64 和干净机器实机验收独立记录，不以交叉编译代替。

SHA256SUMS.txt 可用于核对下载完整性：

```powershell
Get-FileHash ./Charloom-<版本>-win-x64.zip -Algorithm SHA256
```

## 本地复现 CI

```powershell
./scripts/ci-build.ps1
./scripts/ci-build.ps1 -Architecture arm64
# 标签校验也可在本机复现
$releaseVersion = ([xml](Get-Content ./src/Charloom/Charloom.csproj -Raw)).Project.PropertyGroup.Version
./scripts/ci-build.ps1 -Tag "v$releaseVersion"
```

这仅执行构建、检查和 ZIP 生成，不启动程序，也不发布 GitHub Release。日常 WinUI 开发继续使用 BuildAndRun.ps1 注入分析器。

核心检查现在使用 `dotnet test tests/Charloom.Core.Tests/Charloom.Core.Tests.csproj -c Release --logger trx`。
GitHub Actions 无论构建成功或失败都尝试上传 `artifacts/test-results/*.trx`。
Windows 字体、项目与导出专项使用 `Charloom.Creation.Tests` 的 xUnit；PowerShell UI 脚本在真实桌面执行，测试职责见[架构](ARCHITECTURE.md)。

构建完成后运行 `./scripts/ci-checks.ps1`，用本地替身检查 Draft 发布顺序、重跑、上传失败和校验和错误，不调用真正的发布 API。
# 1.0 预发布同包交付

1.0 预发布的每种架构 ZIP 同时包含桌面端与 CLI，两者版本一致。Charloom 品牌包使用 `Charloom.exe` 和 `charloom-cli.exe`；首个公开新品牌预发布额外提供 `asciistudio-cli.exe` 兼容入口。alpha.1–alpha.3 的历史包仍使用 `AsciiStudio.exe` 和 `asciistudio-cli.exe`。发布检查包含共享应用服务、CLI 运行时配置和完整 GUI PRI／XBF；x64 CI 运行真实 CLI 进程验证帮助、UTF-8 stdin 和转换。桌面主窗口在本地解压包上另行验收；ARM64 实机验收独立记录。

正式 0.9.0 保留稳定下载入口。`v1.0.0-alpha.1` 等预发布标签通过同一流水线发布为 prerelease，不能改标为正式 1.0.0；最终正式版须用户明确许可。
