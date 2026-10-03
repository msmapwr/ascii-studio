# AsciiStudio 介绍网页计划

日期：2026-10-03。状态：首版已实现并完成本地验证，使用 GitHub Pages 发布。

## 1. 确定技术栈

采用 HTML + CSS + JavaScript + Fluent UI Web Components。

本次目标是介绍现有 Windows 应用，让访客了解功能、查看真实转换效果并找到下载入口。首版适合单页静态网站，不需要后台、数据库或客户端框架。推荐方案优先满足文件少、首屏内容直接可读、维护简单和静态托管便利。

官方组件用于主题选择等交互控件，CSS 变量统一页面的颜色、排版、间距与层级。正文、导航与下载链接使用语义 HTML；FAQ 使用原生 details / summary。组件加载失败时，访客仍能阅读内容和访问下载入口。

### 六种方案的取舍

| 方案 | 优点 | 缺点 | 本项目建议 |
| --- | --- | --- | --- |
| HTML + CSS + 原生 JavaScript | 3 个主要代码文件；无需构建；内容直接存在于 HTML；易静态托管 | 需要自行实现控件样式与交互 | 初始备选 |
| HTML + CSS + JavaScript + Fluent UI Web Components | 可使用官方 Fluent 控件；无需 React；可通过 CDN 加载 | 增加依赖加载、组件注册、主题与版本管理；Shadow DOM 样式需适配 | 用户已选定 |
| Vite + TypeScript + Fluent UI Web Components | 模块化、类型检查、依赖锁定与生产打包方便 | 增加 package.json、锁文件、配置和构建产物 | 交互变多后采用 |
| Astro 静态站点 + CSS | 多页内容、公共布局与静态输出容易维护 | 单页首版会增加框架和工程文件 | 后续扩展教程与文档中心时考虑 |
| React + Vite + Fluent UI React | 官方 React 控件丰富，适合状态较多的交互界面 | 增加框架、构建与运行时代价；单页介绍不需要这么多结构 | 在线编辑器或复杂演示阶段再评估 |
| Blazor + Fluent UI Blazor | 使用 C# / Razor；适合已有 .NET Web 逻辑与服务集成 | 增加 .NET Web 工程；交互式 Server 需要服务端，WASM 需要下载运行时 | 当前静态介绍页不优先采用 |

桌面应用使用 C# 并不要求介绍页也使用 C#。只有将来需要共享可在浏览器运行的逻辑，或增加 .NET 服务时，再评估 Blazor；当前 WinUI 与 Windows 专用代码不能直接变成浏览器功能。

## 2. 已核实的项目依据

- 仓库：[msmapwr/ascii-studio](https://github.com/msmapwr/ascii-studio)，origin 使用该仓库的 SSH 地址。
- 桌面技术栈：WinUI 3、C#、.NET 10、Windows App SDK；源项目版本为 `0.9.0-alpha.8`。
- [README](../README.md) 介绍当前已实现功能；[路线图](ROADMAP.md) 约束后续范围。
- [品牌规范](BRANDING.md) 提供 SVG Logo 与紫色、蓝色、青色品牌色。
- [展示素材说明](../assets/showcase/README.md) 提供真实应用截图、操作 GIF 与可复现的转换效果。
- `docs/archive/WEBSITE_FEATURE_PLAN.md` 是历史桌面产品功能计划，不作为新介绍页的已实现能力清单。

网页介绍现有产品。动画、摄像头和 3D 已冻结，不作为当前可用功能宣传。当前仍为 0.9 预发布阶段；未经用户明确许可，不宣传或发布正式版 1.0.0。

## 3. 最少文件结构

建议将网页放在独立的 `website/` 目录，与桌面源代码分开：

```text
docs/
  WEBSITE_PLAN.md
  WEBSITE_CONTENT.xml  # 页面各位置的文案与素材规划
website/
  dist/            # 可直接发布的静态目录
    index.html     # 页面内容、语义结构、标题与描述
    styles.css     # Fluent 风格变量、响应式布局、深浅主题
    main.js        # Fluent 组件加载、主题偏好、渐进增强
    assets/        # 发布所需的 Logo、截图与样例
```

首版仅 3 个主要代码文件，图片与 Logo 另计。FAQ 使用原生 details / summary，下载使用 a 链接；内容和下载入口在 JavaScript 不可用时仍可使用。

不为了减少文件而把所有图片转成 Base64 塞进 HTML。把选用素材复制到网站发布目录，保留来源；部署包必须自包含，不能引用发布目录之外的 `../assets/`。

首版计划通过固定版本的 CDN ESM 加载 Fluent UI Web Components，验证主题 API 和组件注册后再接入页面。该路线无需 package.json、TypeScript 配置或打包器。若后续改用 npm，则增加清单与锁文件；引入构建工具前重新评估，不自动更换已确定的技术栈。

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

具体文案保存在 [WEBSITE_CONTENT.xml](WEBSITE_CONTENT.xml)。XML 按页面顺序记录每处标题、正文、链接文字、图片替代文本和交互提示；布局说明与事实来源单独存放，不显示在网页上。实现时把文案写入 HTML，不要求浏览器解析 XML。文案已按 humanizer 技能复核，保留功能边界，去除夸大承诺和重复句式。

1. **导航与首屏**：Logo、用途简介、下载预发布版、查看 GitHub；说明 Windows 原生与本机离线处理。
2. **真实作品展示**：图片转彩色 ANSI、文字转 ASCII 的原图／结果对照。
3. **主要功能**：图片转换、文字排版、ANSI 查看、编辑与对比、项目保存及导出。内容以当前 README 为准。
4. **创作流程**：导入或输入 → 调整与比较 → 保存项目 → 导出作品；结合真实截图或操作 GIF。
5. **下载与常见问题**：Windows x64、ZIP 解压运行、预发布状态、处理是否上传、静态 GIF 限制。
6. **页尾**：GitHub、Release 更新说明、路线图与素材来源。

下载入口固定指向 [GitHub Releases](https://github.com/msmapwr/ascii-studio/releases)。首版不写死某次构建的文件 URL，不依赖浏览器请求 GitHub API 才能显示下载按钮。发布前再次核实最新可下载版本与系统要求。

首版复用以下素材：

- [横版 SVG Logo](../assets/branding/asciistudio-horizontal.svg)。
- [应用截图](../assets/showcase/app.png)。
- [图片转换对照](../assets/showcase/landscape-before-after.png)。
- [文字转换对照](../assets/showcase/text-before-after.png)。
- [文字操作 GIF](../assets/showcase/text-workflow.gif)：作为次要内容；减少动态效果模式使用静态截图。

Logo 保留 SVG 源文件，不重新生成品牌。需要新增 PNG / ICO 时沿用现有脚本。图片声明固有尺寸，首屏外素材延迟加载；必要时生成适合网页的压缩副本，保留仓库原文件。

## 6. 官方组件与版本策略

首版使用 Fluent UI Web Components，先验证主题切换与所需控件，再集成。官方 README 支持 CDN 模块加载，并建议部署时固定到已经开发与测试过的版本：[Fluent UI Web Components](https://github.com/microsoft/fluentui/blob/master/packages/web-components/README.md)。

不直接复制技能文件中的版本示例：本次核实的官方文档使用 `setTheme` 与 `webLightTheme` / `webDarkTheme`，技能中的部分示例使用 `provideFluentDesignSystem`。实现时以选定发布版本的文档和实际导出为准，不能混用不同代际 API，也不使用浮动 latest 地址。

确认 CDN ESM 入口及其依赖能够在浏览器正确解析；下载入口与正文保持原生 HTML，组件加载失败时仍能访问。如有 CDN 兼容问题，先评估固定版本的本地组件资源；需要打包工具时另行确定，不默认转为 Vite + TypeScript。

Fluent UI Blazor 是 ASP.NET Core Blazor 的组件库；它属于独立的 Web 技术路线：[官方仓库](https://github.com/microsoft/fluentui-blazor)。当前不安装该库。

## 7. 开发与 GitHub Pages 发布

用户已确定：固定顶部导航、手机折叠菜单、Release 链接新标签页打开、GIF 进入视野播放、不添加访问统计、发布到 GitHub Pages。减少动态效果时使用静态图。

单页介绍站按 Medium 实施，开始前已提出并收到六项交互与交付选择。内容来自 XML 文案，Fluent Web Components 固定为 `3.1.3`，主题 token 固定为 `1.0.0-alpha.24`。CDN 不可用时隐藏外观控件，正文和下载入口保持可用。

建议拆成以下 Small：

1. HTML 内容与真实素材整理。
2. Fluent 主题与响应式布局。
3. 主题切换、键盘操作与渐进增强。
4. 浏览器验证、版本／Changelog 整理与发布准备。

实现位于 `feat/website-introduction`，基于最新 main 并带入既有网页计划提交。因共享目录被其他任务切换分支，网页改动转移至独立工作目录。提交遵循 Conventional Commits。

使用 GitHub Pages 分支发布：把 `website/dist` 导出到 `release/website-pages`，Pages 仅发布该分支根目录。应用代码、规划 XML 与开发工具不进入发布目录。后续更新网页后重新导出并推送该分支，不手工修改发布副本。未创建 Sites 项目。

网站独立版本为 `0.9.0-alpha.1`，记录在 HTML 的 `application-version` 元数据和 [网站 Changelog](WEBSITE_CHANGELOG.md) 中。桌面应用版本保持 `0.9.0-alpha.8`，遵守预发布约束，不发布 1.0.0。源代码与静态发布分支均 Push。

开发与验证方式见 [网站开发说明](WEBSITE_DEVELOPMENT.md)。

## 8. 验收标准

- 访客首屏能够理解产品用途并找到下载入口。
- 内容、版本、平台要求与 README／实际 Release 一致，没有把规划功能当成已实现功能。
- 手机、平板和桌面宽度下无意外横向溢出；截图清晰，文本可读。
- 深浅主题、键盘操作、高对比度和减少动态效果模式可用。
- 禁用 JavaScript 后，介绍内容、作品图片、FAQ 和下载链接仍可访问。
- Logo、图片和内部链接在实际发布路径下正常加载；无浏览器控制台阻断错误。
- HTML / CSS 检查与 JavaScript 静态检查通过；使用 TypeScript 时执行类型检查；框架路线执行适用构建。
- 完成真实浏览器截图核验；网页使用浏览器验证，WinUI UI 测试仅在桌面应用代码发生变化时适用。
- GitHub Pages 发布后验证实际 URL、静态资源与下载链接。


## 更新日志与动效迭代（网站 0.10.0-alpha.1）

任务级别为 Medium，拆分为日志页面与静态生成、共享交互与动效、验证及部署三个 Small。用户已确认：软件版本记录、倒序时间线、适中动画、品牌紫柔光与悬停高光、从仓库 Changelog 生成静态页面、验证后合并 main 并更新 GitHub Pages。

日志页左对齐，桌面左侧是固定版本索引，右侧按日期倒序展示版本、阶段、分类和具体变化；手机索引成为横向可滚动链接，正文保留完整条目。首页顶部导航和页脚均增加“更新日志”，日志页保留主题与下载入口。

```text
固定导航：Logo / 章节 / 更新日志 / 外观 / 下载
更新日志标题、说明与 Release 下载入口
版本索引 | 版本标题、日期
         | 新增 / 调整 / 修复 / 验证
         | 更早的版本
公共页脚
```

沿用 Segoe UI Variable / Segoe UI 字体及现有配色：浅色背景 #F5F5F8、表面 #FFFFFF、正文 #242424、品牌紫 #7741D8；深色背景 #17171C、品牌紫 #BD9AF7。版本时间线连接点表示时间顺序，预发布徽标说明版本阶段。

动效集中在首次展示和访客操作：截图只扫过一次高光，背景柔光保持静态；日志正文直接显示，避免长列表逐条入场影响查阅。阅读进度、索引高亮和跳转反馈帮助定位。系统减少动态效果时关闭动画并保留内容，高对比度时关闭装饰光效。新增文案已按 humanizer 校对，版本条目保持仓库原文。

网站独立升到 0.10.0-alpha.1，桌面应用版本保持不变；此次不创建桌面 Release 标签。
