# v0.2 · Logo、滚轮与分辨率

日期：2026-09-30。

## 开发工具链

遵循 microsoft/win-dev-skills 的 winui-design、winui-dev-workflow、winui-code-review、winui-ui-testing 和 winui-packaging。使用 WinApp CLI 0.7.0；设计查询了 Gallery ScrollViewer / NumberBox / Slider 样例。`BuildAndRun.ps1` 转交给已安装技能的构建脚本并加载其分析器。构建单独运行时通过 Directory.Build.props 加载同一分析器。

现有项目为 unpackaged WinUI，自包含配置。WinApp 明确拒绝对该模式使用 `--no-launch`，因此 build-only 使用 dotnet build 加载技能分析器；运行始终通过项目模式 WinApp。未为了绕过启动问题更改打包类型。

开发者模式尚未启用，已询问 UAC 授权，未擅自触发管理员修改。当前 unpackaged 启动和未签名 MSIX 生成已能进行；开发包注册与安装未验收。

## 实现

- 三个独立 SVG 图标概念，默认使用 Layered Glyph；透明 PNG、横/竖组合字标、单色版、多尺寸 ICO、MSIX 多倍率资源。见 BRANDING.md。
- 参数面板滑块、数字框和关闭的下拉框使用滚轮路由行为，滚轮滚动父面板并保留参数值。原生控件继续用于输入、键盘与拖动。最初继承 NumberBox 导致 WinUI 样式 TargetType 运行时错误，已改为原生控件加附加事件行为。
- 图片分辨率 120 / 240 / 480 / 960 列及 1920 × 1080 字符预设；自定义列 / 行、自动比例、字符宽高比，最多 2000 × 2000、400 万字符。
- 自动比例过高时明确报错，避免旧版静默截断到 600 行造成拉伸。
- 新增 Rows 参数保留于项目；旧项目缺失该字段时继续自动计算。
- PNG / JPEG / 静态 GIF 输出倍率 1–4，显示实际像素尺寸；最大 4000 万像素 / 单边 32767。图片预算提示不会阻止 TXT 等导出。
- 页面导航错误日志、UI 未处理异常日志、项目大小检查；项目序列化移至后台线程。

## 验证记录

- WinApp 项目构建和 Release 构建完成，0 编译错误 / 0 警告（包括技能分析器）。
- `tests/AsciiStudio.Core.Checks`：8 项通过。覆盖固定尺寸、自动比例、1920 × 1080、超限、窄长图、旧默认、取消与透明像素。
- UI 脚本已生成并按技能限制执行。最新批次 4 PASS / 4 FAIL。通过 Logo、图片控件、面板顶部/底部和截图步骤。真实滚轮值保留、固定行输入、文字生成和倍率键盘步骤未完成有效验收：执行时出现窗口/页面切换及非前台焦点错误。不能据此宣称全部 UI 验证通过。
- `artifacts/ui-tests/results.json` 与截图保留实际结果。截图中应用的 Logo、主题与当前工具页面正常显示，但该截图未显示成功生成的文字结果。
- `artifacts/packages/AsciiStudio-0.2.0-x64.msix`：已生成未签名包。尚未签名、安装或进行干净机器验收。

## 代码审查

已检查本次改动的输入校验、UI 线程、释放资源、主题、可访问名称与自动化标识。修复了派生 NumberBox 的 WinUI 样式兼容问题、过大项目保存后不可重开的问题。图片编码和序列化在后台；SVG / 图标为独立本地资产。

尚存架构改进：旧页面仍以代码创建控件与事件，未整体迁移 MVVM / x:Bind；完整本地化、高对比度专项验证及所有旧控件的稳定 AutomationId 尚待完成。滚轮和键盘在无人操作的独占前台窗口中仍需重新验收，本次依照 UI 技能的最多两轮修复重跑限制停止继续重复。
