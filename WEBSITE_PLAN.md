# AsciiStudio 介绍网页计划

日期：2026-10-03。状态：技术栈建议与首版范围，尚未开始网页开发。

## 1. 推荐技术栈

**推荐：HTML5 + CSS + 原生 JavaScript，使用 CSS 变量实现 Fluent 2 风格；需要复杂交互控件时，再按需引入 Fluent UI Web Components。**

本次目标是介绍现有 Windows 应用，让访客了解功能、查看真实转换效果并找到下载入口。首版适合单页静态网站，不需要后台、数据库或客户端框架。推荐方案优先满足文件少、首屏内容直接可读、维护简单和静态托管便利。

Fluent UI 主题可以通过颜色、排版、间距、圆角、层级和交互状态统一实现。使用官方组件库是另一个决策，并非实现 Fluent 风格的前提；首版的导航、下载链接、主题按钮和 FAQ 可以使用原生语义元素。

### 六种方案的取舍

| 方案 | 优点 | 缺点 | 本项目建议 |
| --- | --- | --- | --- |
| HTML + CSS + 原生 JavaScript | 3 个主要代码文件；无需构建；内容直接存在于 HTML；易静态托管 | 多页面内容复用与复杂状态需要自己组织 | **首版推荐** |
| HTML + CSS + JavaScript + Fluent UI Web Components | 可使用官方 Fluent 控件；无需 React；可通过 CDN 加载 | 增加依赖加载、组件注册、主题与版本管理；Shadow DOM 样式需适配 | 明确要求官方组件时的首选 |
| Vite + TypeScript + Fluent UI Web Components | 模块化、类型检查、依赖锁定与生产打包方便 | 增加 package.json、锁文件、配置和构建产物 | 交互变多后采用 |
| Astro 静态站点 + CSS | 多页内容、公共布局与静态输出容易维护 | 单页首版会增加框架和工程文件 | 后续扩展教程与文档中心时考虑 |
| React + Vite + Fluent UI React | 官方 React 控件丰富，适合状态较多的交互界面 | 增加框架、构建与运行时代价；单页介绍不需要这么多结构 | 在线编辑器或复杂演示阶段再评估 |
| Blazor + Fluent UI Blazor | 使用 C# / Razor；适合已有 .NET Web 逻辑与服务集成 | 增加 .NET Web 工程；交互式 Server 需要服务端，WASM 需要下载运行时 | 当前静态介绍页不优先采用 |

桌面应用使用 C# 并不要求介绍页也使用 C#。只有将来需要共享可在浏览器运行的逻辑，或增加 .NET 服务时，再评估 Blazor；当前 WinUI 与 Windows 专用代码不能直接变成浏览器功能。

## 2. 已核实的项目依据

- 仓库：[msmapwr/ascii-studio](https://github.com/msmapwr/ascii-studio)，origin 使用该仓库的 SSH 地址。
- 桌面技术栈：WinUI 3、C#、.NET 10、Windows App SDK；源项目版本为 `0.9.0-alpha.8`。
- [README](README.md) 介绍当前已实现功能；[路线图](docs/ROADMAP.md) 约束后续范围。
- [品牌规范](docs/BRANDING.md) 提供 SVG Logo 与紫色、蓝色、青色品牌色。
- [展示素材说明](assets/showcase/README.md) 提供真实应用截图、操作 GIF 与可复现的转换效果。
- `docs/archive/WEBSITE_FEATURE_PLAN.md` 是历史桌面产品功能计划，不作为新介绍页的已实现能力清单。

网页介绍现有产品。动画、摄像头和 3D 已冻结，不作为当前可用功能宣传。当前仍为 0.9 预发布阶段；未经用户明确许可，不宣传或发布正式版 1.0.0。

## 3. 最少文件结构

建议将网页放在独立的 `website/` 目录，与桌面源代码分开：

```text
WEBSITE_PLAN.md
website/
  index.html       # 页面内容、语义结构、标题与描述
  styles.css       # Fluent 风格变量、响应式布局、深浅主题
  main.js          # 主题偏好、必要的渐进增强
  assets/          # 发布所需的 Logo、截图与样例
```

首版仅 3 个主要代码文件，图片与 Logo 另计。FAQ 使用原生 details / summary，下载使用 a 链接；内容和下载入口在 JavaScript 不可用时仍可使用。

不为了减少文件而把所有图片转成 Base64 塞进 HTML。把选用素材复制到网站发布目录，保留来源；部署包必须自包含，不能引用发布目录之外的 `../assets/`。

采用当前推荐方案时无需 package.json、TypeScript 配置或打包器。未来引入 npm 依赖时增加清单与锁文件，接受必要的工程文件，不以减少文件数牺牲可维护性。

## 4. Fluent 视觉方向

网页延续 AsciiStudio 的品牌，重点展示真实字符画作品与 Windows 创作界面。

- 配色起点：品牌紫 `#7741D8`、辅助蓝 `#32AEFF`、辅助青 `#85F0FF`、浅色背景 `#F5F5F5`、表面白 `#FFFFFF`、正文 `#242424`。正文、按钮和焦点实际配色以对比度验证为准。
- 排版：优先使用本机 `Segoe UI Variable` / `Segoe UI`，中文回退到 `Microsoft YaHei`，最后使用系统无衬线字体；字符画使用等宽字体。
- 间距：采用 4px 基础节奏；控件、作品容器和浮层使用不同层级的圆角与阴影。
- 主题：默认跟随系统，支持浅色与深色切换；所有表面、正文、链接和状态颜色统一通过 CSS 变量管理。
- 布局：内容左对齐；首屏呈现产品名称、明确用途、下载入口与真实应用截图。作品区域用较大幅面的原图／结果对照，避免把所有内容拆成同尺寸卡片。
- 材质：适度使用半透明导航与柔和表面层级；浏览器效果作为网页视觉处理，不声称实现原生 WinUI Mica。
- 无障碍：可见键盘焦点、语义标题、图片替代文本、高对比度适配、减少动态效果偏好；交互信息不只靠颜色表达。

Fluent 2 官方使用设计 token 统一颜色、排版、间距与层级，可作为网站 CSS 变量体系的依据：[Design tokens](https://fluent2.microsoft.design/design-tokens)。

## 5. 首版页面内容

默认假设：中文优先、单页、公开产品介绍；目标动作是了解产品后访问 GitHub Releases。以下为后续网页开发建议，不表示已实施。

1. **导航与首屏**：Logo、用途简介、下载预发布版、查看 GitHub；说明 Windows 原生与本机离线处理。
2. **真实作品展示**：图片转彩色 ANSI、文字转 ASCII 的原图／结果对照。
3. **主要功能**：图片转换、文字排版、ANSI 查看、编辑与对比、项目保存及导出。内容以当前 README 为准。
4. **创作流程**：导入或输入 → 调整与比较 → 保存项目 → 导出作品；结合真实截图或操作 GIF。
5. **下载与常见问题**：Windows x64、ZIP 解压运行、预发布状态、处理是否上传、静态 GIF 限制。
6. **页尾**：GitHub、Release 更新说明、路线图与素材来源。

下载入口固定指向 [GitHub Releases](https://github.com/msmapwr/ascii-studio/releases)。首版不写死某次构建的文件 URL，不依赖浏览器请求 GitHub API 才能显示下载按钮。发布前再次核实最新可下载版本与系统要求。

首版复用以下素材：

- [横版 SVG Logo](assets/branding/asciistudio-horizontal.svg)。
- [应用截图](assets/showcase/app.png)。
- [图片转换对照](assets/showcase/landscape-before-after.png)。
- [文字转换对照](assets/showcase/text-before-after.png)。
- [文字操作 GIF](assets/showcase/text-workflow.gif)：作为次要内容；减少动态效果模式使用静态截图。

Logo 保留 SVG 源文件，不重新生成品牌。需要新增 PNG / ICO 时沿用现有脚本。图片声明固有尺寸，首屏外素材延迟加载；必要时生成适合网页的压缩副本，保留仓库原文件。

## 6. 官方组件与版本策略

若后续明确要求使用官方 Fluent UI 控件，选择 Fluent UI Web Components，先验证主题切换与所需控件，再集成。官方 README 支持 CDN 模块加载，并建议部署时固定到已经开发与测试过的版本：[Fluent UI Web Components](https://github.com/microsoft/fluentui/blob/master/packages/web-components/README.md)。

不直接复制技能文件中的版本示例：本次核实的官方文档使用 `setTheme` 与 `webLightTheme` / `webDarkTheme`，技能中的部分示例使用 `provideFluentDesignSystem`。实现时以选定发布版本的文档和实际导出为准，不能混用不同代际 API，也不使用浮动 latest 地址。

如采用 CDN，确认 ESM 入口及其依赖能够在浏览器正确解析；下载入口与正文保持原生 HTML，组件加载失败时仍能访问。若需要可靠的按需打包与依赖管理，转为 Vite + TypeScript 方案。

Fluent UI Blazor 是 ASP.NET Core Blazor 的组件库；它属于独立的 Web 技术路线：[官方仓库](https://github.com/microsoft/fluentui-blazor)。当前不安装该库。

## 7. 后续开发与 Sites 托管

本次仅写计划，不创建 Site、不安装依赖、不开发或发布网页。

实际单页介绍站预计为 **Medium**。开始实现前按照项目规则一次性澄清 6–12 个有效问题；关键决策包括目标访客、语言、页面内容、交互范围、是否必须使用官方组件、下载信息与发布方式。已从项目确认的品牌、技术栈和功能不重复询问。

建议拆成以下 Small：

1. HTML 内容与真实素材整理。
2. Fluent 主题与响应式布局。
3. 主题切换、键盘操作与渐进增强。
4. 浏览器验证、版本／Changelog 整理与发布准备。

后续使用独立的 `feat/website-introduction` 分支，并以最新 main 为基础。提交遵循 Conventional Commits；适用检查通过后按项目流程 Commit / Push。

Sites 可按静态资源路线承载该介绍页，无需为托管强制引入 React、Blazor 或服务端。实际发布时再使用 Sites 的注册、打包和发布流程；按要求准备公开静态输出目录及 `.openai/hosting.json`，配置只指向网站公开输出，不把整个桌面仓库打包。网页源文件数量与托管配置文件数量分别计算。

本次文档任务为 Small，不准备 Push，因此不调整应用版本。后续 Medium 完成并准备 Push 时，应明确网站与应用的版本管理关系、更新对应版本和 Changelog；遵守 0.9 预发布约束，不自动推进到 1.0.0。

## 8. 验收标准

- 访客首屏能够理解产品用途并找到下载入口。
- 内容、版本、平台要求与 README／实际 Release 一致，没有把规划功能当成已实现功能。
- 手机、平板和桌面宽度下无意外横向溢出；截图清晰，文本可读。
- 深浅主题、键盘操作、高对比度和减少动态效果模式可用。
- 禁用 JavaScript 后，介绍内容、作品图片、FAQ 和下载链接仍可访问。
- Logo、图片和内部链接在实际发布路径下正常加载；无浏览器控制台阻断错误。
- HTML / CSS 检查与 JavaScript 静态检查通过；使用 TypeScript 时执行类型检查；框架路线执行适用构建。
- 完成真实浏览器截图核验；网页使用浏览器验证，WinUI UI 测试仅在桌面应用代码发生变化时适用。
- Sites 发布后验证实际 URL、静态资源与下载链接。
