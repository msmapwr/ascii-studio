# Charloom｜字织

<p align="center">
  <img src="assets/branding/charloom-horizontal.svg" alt="Charloom｜字织" width="420" />
</p>

**把字符织成画面。**

Windows 原生、离线、高质量 ASCII / ANSI 字符艺术创作工具。

图片或文字 → 调整转换质量 → 编辑与比较 → 保存项目 → 导出作品。转换与导出在本机完成。

### [⬇ 下载 0.9.0](https://github.com/msmapwr/charloom/releases/tag/v0.9.0)　[官网 · 教程与作品展示](https://msmapwr.github.io/charloom/)　[更新说明](CHANGELOG.md)

Windows x64 / ARM64：完整解压对应架构的 ZIP，运行桌面端或 CLI。便携版包含运行时，无需开发 SDK。新品牌的入口为 `Charloom.exe` 和 `charloom-cli.exe`；旧版本仍使用原文件名。
Charloom 原名 AsciiStudio。正式版本仍为 **0.9.0**（旧名发行）；当前源码为 **1.0.0-alpha.6**（最新公开预发布为 [alpha.5](https://github.com/msmapwr/charloom/releases/tag/v1.0.0-alpha.5)），正式 1.0.0 尚未发布。新品牌预发布包使用 `Charloom.exe` 和 `charloom-cli.exe`；另保留 `asciistudio-cli.exe` 兼容入口一个公开预发布周期。预发布下载以 [GitHub Releases](https://github.com/msmapwr/charloom/releases) 为准。

源码版 CLI 提供 64 个命令及逐级中英文 `--help`，支持持久编辑历史、Unicode 选区、独立工作区、候选结果管理和显式剪贴板命令（alpha.6 新增），详见 [命令行详细教程](docs/CLI_TUTORIAL.md) 与 [功能覆盖表](docs/CLI.md)。尚未覆盖全部桌面功能；双语桌面、扩展动效与个性化设置继续开发。

官网提供产品介绍。完整静态网站位于 [`website/dist`](website/dist)，包含[入门教程](website/dist/guide.html)、[作品展示](website/dist/gallery.html)、[下载与运行](website/dist/downloads.html)和[版本记录](website/dist/changelog.html)，可用于 GitHub Pages。

![图片到彩色 ANSI：山景原图与半块字符结果](assets/showcase/landscape-before-after.png)
![文字到 ASCII：系统字体原图与真实转换结果](assets/showcase/text-before-after.png)

以上为应用同一转换／导出代码生成的样例，原图由仓库工具绘制；可[离线复现](assets/showcase/README.md)。

<p>
  <img src="assets/showcase/app.png" width="49%" alt="Charloom 原生 Windows 创作界面" />
  <img src="assets/showcase/text-workflow.gif" width="49%" alt="文字转换操作：输入、生成与切换内容" />
</p>

实机窗口截图与操作截图序列，展示改名前的 AsciiStudio 界面。

## 功能

- 图片转字符画：打开或粘贴图片，调整字符集、尺寸、比例、亮度、对比度、颜色和抖动方式。支持 PNG、JPEG、BMP、GIF 第一帧和 TIFF。
- 图片质量：字体密度实测、结构线条、Braille、半块双色、自适应、透明裁剪和调色板；阶段缓存、临时预览及可取消转换。详见 [图片质量说明](docs/IMAGE_QUALITY.md)。
- 文字转字符画：FIGlet／系统字形，字行距、换行、对齐、规则重叠、字体网格／收藏／导入、中文描边与缺字提示、混排预设、标题边框。详见 [文字排版说明](docs/TEXT_LAYOUT.md)。
- ANSI 查看器：查看 ANSI 文件或粘贴转义序列，读取常见颜色、编码和 SAUCE 信息。
- 编辑与整理：编辑字符画、调整预览缩放、撤销和重做。手工修改后可选择更新结果或保留独立版本。
- 大结果与对比：可见区域预览、适应窗口/宽度、分页编辑与选区定位，支持原图切换、并排和可拖动分界线。使用方法及资源边界见 [视图说明](docs/VIEWPORT.md)。
- 项目与导出：保存 `.asciiproj` 项目并恢复创作来源。可导出 TXT、PNG、JPEG、静态 GIF、HTML、SVG、ANSI、JSON 和 Markdown。

结果历史最多保存 100 步，并受 64 MB 预算限制。0.9 聚焦转换、ANSI、编辑、保存和导出；动画、摄像头、3D 已冻结，见[路线图](docs/ROADMAP.md)。

已有辅助工具继续可用：生成边框与图案、整理文本、代码注释、本地加密与编码。它们不扩大本阶段的开发范围。

## 获取与运行

控制台用户可从 [命令行详细教程](docs/CLI_TUTORIAL.md) 开始，包含 PowerShell 示例、图片／中文转换、持久编辑、工作区恢复、批处理与加密。

### 下载使用

在 [0.9.0 Release](https://github.com/msmapwr/charloom/releases/tag/v0.9.0) 下载 `AsciiStudio-0.9.0-win-x64.zip` 或 `AsciiStudio-0.9.0-win-arm64.zip`，完整解压后双击 `AsciiStudio.exe`。不要只复制 EXE，程序需要同目录下的 DLL、PRI、XBF 和资源文件。

ZIP 未做代码签名；`SHA256SUMS.txt` 用于检查下载完整性。ARM64 构建验证与实机运行验收分开记录。

### 命令行快速开始

在新品牌 ZIP 的解压目录打开 PowerShell：

```powershell
.\charloom-cli.exe --help --language zh-CN
.\charloom-cli.exe text --text "Charloom" --output hello.txt
.\charloom-cli.exe image --help --language en-US
```

旧版本 CLI 名称为 `asciistudio-cli.exe`；请先运行 `--version`，按对应版本的帮助使用。

### 开发环境

- Windows x64 开发机，最低运行目标为 Windows 10 1809；发布包提供 x64 / ARM64
- .NET SDK `10.0.401`（由 `global.json` 固定）
- Visual Studio 2026、WinUI 工作负载和 Windows SDK 26100
- Windows App SDK `2.5.1`
- WinApp CLI `0.7.0` 和项目要求的 `microsoft/win-dev-skills`

克隆新仓库后，在项目目录使用已安装的 WinUI 工作流技能启动：

```powershell
$workflow = Join-Path $env:USERPROFILE '.codex\skills\winui-dev-workflow\BuildAndRun.ps1'
& $workflow .\src\Charloom\Charloom.csproj --detach
```

也可以打开 `Charloom.slnx`，将 `Charloom` 设为启动项目。生成 Release 构建：

```powershell
.\scripts\build.ps1 -Configuration Release
```

开发环境和 CI 发布流程见 [CI 与 Release](docs/CI_RELEASE.md)。

## 文件与限制

为兼容旧版，设置、最近项目、窗口位置、结果恢复和日志继续使用 `%LOCALAPPDATA%\AsciiStudio`；改名不会清空这些数据。恢复数据保存在本机；最近项目列表记录的是文件路径。

图片文件上限为 40 MB，源图上限为 8000 万像素，处理时最长边缩至 2400 像素。图片结果最多 2000 列、2000 行和 400 万采样点；Braille每字符8点，半块每字符2点。位图导出上限为 4000 万像素，单边不超过 32767 像素；HTML / SVG 标记上限为 1600 万字符。较大的作品可改用文本格式导出。

GIF 目前只读取第一帧，导出的 GIF 也是静态图。字符画网格按Unicode字符簇和显示列计算，中文与emoji使用双列；字体或终端的实际字形可能不同，详见[Unicode说明](docs/UNICODE.md)。FIGlet 模式接受 ASCII 输入；中文文字请使用系统字体模式。

## 相关文档

- [0.9 路线图](docs/ROADMAP.md)
- [架构与测试](docs/ARCHITECTURE.md)
- [历史归档](docs/archive/README.md)
- [第三方依赖说明](docs/THIRD_PARTY.md)
