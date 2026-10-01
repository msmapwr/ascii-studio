# 自动编译与发布

工作流：`.github/workflows/build-release.yml`。

- main 推送、PR 与手动运行：Windows x64 Release 编译，执行核心检查，上传自包含 ZIP、SHA256SUMS.txt、发布说明，保留 14 天。
- 推送 `v*` 标签：上述检查全部通过后创建 GitHub Release。普通标签成为最新版；alpha/beta/rc 为预发布。
- 发布版本必须与 csproj、MSIX manifest、Changelog 一致。失败不创建公开 Release。
- 先创建 Draft，上传成功再公开；重跑会继续未完成 Draft，不覆盖已经公开的 Release。
- 不需要额外仓库 Secret，使用 GitHub 自带 GITHUB_TOKEN。只有 Release job 拥有 contents:write；PR job 只读。
- Action 使用已核对的官方版本并固定到完整 SHA。不会运行本机 BuildAndRun.ps1 或要求安装桌面技能。

## 发布一个版本

先完成代码、版本号与 Changelog，再合并 main。确认 main 的 Build and Release 成功后：

```powershell
git tag v0.6.0-alpha.2
git push origin v0.6.0-alpha.2
```

正式发布时使用正式版本标签，例如 `v0.6.0`。不要把未完成的功能标为正式版本。手动 Run workflow 只编译选中的 ref，不发布 Release。

## 使用下载文件

在 Releases 下载 `AsciiStudio-<版本>-win-x64.zip`，解压完整目录后运行 `AsciiStudio.exe`。Windows x64，目标 Windows 10 1809 及以后；包含 .NET 和 Windows App SDK 运行时。ZIP 未代码签名，当前不自动发布 MSIX。

SHA256SUMS.txt 可用于核对下载完整性：

```powershell
Get-FileHash ./AsciiStudio-<版本>-win-x64.zip -Algorithm SHA256
```

## 本地复现 CI

```powershell
./scripts/ci-build.ps1
# 标签校验也可在本机复现
./scripts/ci-build.ps1 -Tag v0.6.0-alpha.2
```

这仅执行构建、检查和 ZIP 生成，不启动程序，也不发布 GitHub Release。日常 WinUI 开发继续使用 BuildAndRun.ps1 注入分析器。

构建完成后运行 `./scripts/ci-checks.ps1`，用本地替身检查 Draft 发布顺序、重跑、上传失败和校验和错误，不调用真正的发布 API。
