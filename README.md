# AsciiStudio

原生 WinUI 3 字符创作程序。图片、文字和导出全部在本机处理。

## 当前开发版

- 图片选择、拖放、剪贴板导入；PNG / JPEG / BMP / GIF 第一帧 / TIFF。
- 输出列数、预设与自定义字符集、亮度、对比度、Gamma、颜色、反转、饱和度、色相、灰度、棕褐色、锐化、边缘检测、二值化。
- Floyd–Steinberg、Jarvis–Judice–Ninke、Stucki、Atkinson 误差扩散。
- FIGlet 字体艺术字；系统字体栅格化用于中文；裁边、空格替换、三种边框。
- 基础边框、分隔线、矩形迷宫、星空、棋盘、斜纹、密度图案；随机种子。
- 基础 Unicode / ASCII 分析、裁边、控制字符清理、大小写、按字符簇反转和 Tab 转换。
- 可编辑结果、复制、图像预览；TXT / PNG / JPEG / 静态 GIF / HTML / SVG / ANSI / JSON / Markdown 导出。
- `.asciiproj` 项目、嵌入图片、文字生成参数、最近项目、结果恢复与主题设置。
- 独立 SVG Logo、窗口 / 任务栏 ICO、MSIX 图标资源；分辨率预设和自定义列 / 行，位图导出 1–4 倍。

这是首个可运行开发版，尚未覆盖网站的完整功能。详细边界见 [开发进度](docs/DEVELOPMENT_STATUS.md)。
当前版本 **0.5.0**：图片裁剪、方向调整、几何撤销与项目恢复见 [本轮记录](docs/ITERATION_06.md)。字体预览、设置和动画见 [v0.4](docs/ITERATION_05.md)，高 DPI 与响应式布局见 [v0.3](docs/ITERATION_04.md)。

## 开发环境

- Windows x64；最低系统目标为 Windows 10 1809，当前仅在此 Windows 11 开发机启动。
- .NET SDK **10.0.401**，由 `global.json` 固定。
- Visual Studio 2026，WinUI 工作负载与 Windows SDK 26100；组件清单在 `.vsconfig`。
- Windows App SDK **2.5.1**；依赖版本及锁文件随源码管理。
- WinApp CLI **0.7.0** 与已安装的 `microsoft/win-dev-skills`。开发约定见 `AGENTS.md`。

## 构建与运行

GitHub Actions 自动编译及版本标签发布说明见 [CI 与 Release](docs/CI_RELEASE.md)。

在仓库目录运行：

```powershell
.\BuildAndRun.ps1 .\src\AsciiStudio\AsciiStudio.csproj --detach
```

或者打开 `AsciiStudio.slnx`，选择 `AsciiStudio` 项目启动。

生成 Release 输出：

```powershell
.\scripts\build.ps1 -Configuration Release
```

程序按 x64、自包含 .NET 和 Windows App SDK 配置。复制发布时必须保留整个输出目录；安装包、文件关联、干净机器验证尚未完成。
使用 `.\scripts\package.ps1` 生成未签名 MSIX；传入 `-CertificatePath` 可使用已有证书签名。本次已生成未签名包，未安装证书或安装包。签名、信任和干净机器验证仍待完成。

## 数据与限制

设置、最近项目、恢复文件和错误日志保存在 `%LOCALAPPDATA%\AsciiStudio`。最近项目记录是本机文件路径。恢复文件保存最近一次结果，编辑后延迟约 800ms 写入；频繁更新时等待输入停止。图库与多文档工作区尚未实现。

图片文件最大 40MB，源图最大 8000 万像素，处理时最长边缩至 2400；图片转换最多 2000 列 / 2000 行及 400 万字符，自动比例超限时提示而不截断。1920 × 1080 预设是字符网格，固定行数可能拉伸原图。位图导出最大 4000 万像素、单边 32767 像素；高分辨率网格可导出 TXT 等格式，位图可能需要更小的网格和字号。FIGlet 输入限制 ASCII；中文使用系统字体模式。Unicode 网格仍以 UTF-16 单元作为宽度，复杂宽字符的网格排版尚待完善。GIF 当前为静态图片。

## 完整计划

[网站功能计划](docs/WEBSITE_FEATURE_PLAN.md)与[覆盖表](docs/FEATURE_COVERAGE.csv)是完整范围。游戏、历史档案、会员排除。摄像头、3D、ANSI 查看器、绘画工作室、动画、素材库、全面文本工具和高级导出均继续保留在计划中。

未复制网站作品素材。FIGlet 使用 Figgle / Figgle.Fonts；依赖来源见 [第三方说明](docs/THIRD_PARTY.md)。
