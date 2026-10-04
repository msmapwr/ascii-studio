# 开发约定

后续 WinUI 开发使用已安装的 microsoft/win-dev-skills。

- 界面设计/修改先读取 `winui-design`。
- 构建和启动使用 `winui-dev-workflow` 的 `BuildAndRun.ps1` 与 WinApp CLI。
- 提交前执行 `winui-code-review`。
- UI 验证使用 `winui-ui-testing`；打包使用 `winui-packaging`。
- 工具链缺项按技能要求先说明，使用 `winui-setup` 配置。
- Logo 源文件为 SVG；衍生 PNG / ICO 由脚本生成。

## 开发完成后的主分支同步

- 每轮开发完成并通过适用检查后，将工作分支合并到 `main`，直接推送 `origin/main`，不创建 PR。
- 完成前 fetch 并验证本地 `main` 与 `origin/main` 指向相同提交；保留远程已有修改，不能强推主分支。
- 功能开发仍在独立工作分支进行，不直接修改 `main`。

## 版本发布限制

- 在用户明确说可以发布 1.0.0 之前，不得发布正式版 1.0.0。
- 0.9.0 开发继续使用 alpha/beta/rc 阶段版本；版本号不得自动推进为 1.0.0。
