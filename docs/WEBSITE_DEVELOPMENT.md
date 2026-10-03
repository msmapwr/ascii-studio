# 网站开发与发布

五页 HTML 共用一份 CSS 和 JavaScript，代码位于 `website/dist`，可直接由静态服务器提供。素材来自仓库的 `assets/branding` 和 `assets/showcase`，发布副本位于 `website/dist/assets`。页面正文直接写在 HTML 中，浏览器无需读取 XML。

## 修改内容

文案依据 [WEBSITE_CONTENT.xml](WEBSITE_CONTENT.xml)，修改时同步更新 XML 和 HTML。设计与技术约定见 [WEBSITE_PLAN.md](WEBSITE_PLAN.md)，网站版本记录在 [WEBSITE_CHANGELOG.md](WEBSITE_CHANGELOG.md)。版本改变时同步更新 HTML 的 `application-version` 元数据。

`main.js` 加载固定版本的官方 Fluent UI Web Components 浏览器包及 Fluent 主题 token。导航和演示交互先初始化，CDN 失败不会阻止这些功能。外观控件只在组件与主题就绪后出现。

## 更新日志页

软件更新日志位于 `website/dist/changelog.html`。根目录 `CHANGELOG.md` 是版本记录的来源，导航、外观控件和页脚复用首页 HTML，页面标题与分类文案来自 `WEBSITE_CONTENT.xml` 的 `changelogPage`。生成脚本保留已注明日期的版本、全部分类和条目，跳过 `Unreleased`，不把开发记录标为可下载的 Release。

更新 `CHANGELOG.md`、首页公共导航或 XML 日志文案后，先运行：

```powershell
python scripts/build-website-changelog.py
```

将生成的 HTML 一并提交。`--check` 可确认页面仍与源文件一致。浏览器先显示静态 HTML，再通过 changelog.js 请求 raw.githubusercontent.com 的 main/CHANGELOG.md。只读取有日期的版本，用文本节点渲染，不执行源内容。请求限时 8 秒；失败保留静态记录。正在阅读、焦点位于日志或跟随版本锚点时不替换。禁用脚本时日志仍可使用。来源状态在标题下说明。

## 动画与光效

首屏文案渐入、截图渐入和一次高光扫描构成页面入场；品牌紫柔光保持静态。按钮提供悬停抬升、光晕与按下反馈，外观弹层、手机导航和 FAQ 展开使用短动画。日志页的顶部阅读进度与版本索引跟随滚动，版本跳转节点短暂高亮。滚动更新通过 requestAnimationFrame 合并，每帧最多执行一次，无常驻定时器。

`prefers-reduced-motion: reduce` 关闭入场、扫描、交互过渡和流畅滚动，GIF 使用静态图；高对比度关闭装饰光效。验证过 320px、390px 手机布局，版本索引在窄屏横向滚动，页面自身不产生横向溢出。减少动态效果验收使用本地 QA 变体，将原媒体规则激活并模拟匹配偏好，未更改系统设置。

## 本地验证

安装 Python 时，可从仓库根目录运行：

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory website/dist
```

也可用任意静态 HTTP 服务器提供 `website/dist`。通过 HTTP 访问页面，避免浏览器对 file URL 的模块加载限制。

检查命令在仓库根目录执行。工具通过 npx 使用，不向网站新增框架或依赖清单。

```powershell
python scripts/build-website-changelog.py --check
python scripts/build-website-pages.py --check
python scripts/check-website.py
python scripts/check-doc-links.py
node --check website/dist/main.js
npx --yes --package=html-validate@10.2.1 html-validate "website/dist/*.html"
npx --yes --package=typescript@5.9.3 tsc --allowJs --checkJs --noEmit --target ES2022 --module ESNext --moduleResolution bundler --lib ES2022,DOM website/dist/main.js
npx --yes --package=eslint@9.39.1 eslint website/dist/main.js --no-config-lookup --rule 'no-unused-vars:error' --rule 'no-unreachable:error' --rule 'no-constant-condition:error' --rule 'no-duplicate-imports:error' --rule 'valid-typeof:error'
```

CSS 使用 Lightning CSS 解析验证，输出到临时目录，不覆盖源文件。浏览器检查覆盖桌面和手机布局、主题切换与保留、菜单键盘操作、FAQ、GIF 进入与离开视野播放、无脚本页面和 CDN 故障。减少动态效果使用模拟偏好的本地测试页面验证；上线后仍建议在操作系统开启该选项做一次人工验收。

## GitHub Pages

已发布源代码合并在 `main`，修改在独立的 `feat/*` 或 `fix/*` 分支完成。仓库 Pages 使用 `release/website-pages` 分支根目录，部署内容仅为 `website/dist`，包含 `.nojekyll`。

首版使用 `git subtree split --prefix=website/dist` 生成静态发布提交。后续从包含已发布历史的源代码分支生成发布提交并进行普通快进 Push；若远程发布分支出现独立修改，应先查明并整合，不默认强制覆盖。

发布前完成适用检查、更新网站版本与 Changelog，并提交源代码。先推送源代码，再推送静态发布提交。等待 GitHub Pages 构建成功后检查在线页面与资源。


## 访客页面

入门教程及后续作品、下载页的文案与结构位于 [WEBSITE_PAGES.xml](WEBSITE_PAGES.xml)。修改后运行 `python scripts/build-website-pages.py`，公共导航与版本信息来自首页。随后运行原日志生成脚本更新共用导航。两个生成脚本均支持 `--check`，静态检查读取 XML 确认文案保留。新增页面继续共用 CSS、JavaScript 和 Fluent 外观控件。

本轮目标网站 0.11.0，三个 Medium 分别推送 alpha.1、alpha.2、alpha.3；全部检查后正式发布网站 0.11.0。桌面应用版本与发布标签不改变。


作品素材从 `assets/showcase` 原样复制到发布目录，参数来自 `tools/AsciiStudio.Showcase/Program.cs`。类型筛选与放大仅由脚本增强；无脚本时显示原有并排图和图片原文件链接。比较使用原生 range 输入，图像保持完整比例并在统一画框中裁显，不拉伸源图。原生 dialog 负责模态焦点、Escape 与关闭后的焦点恢复。首版只有仓库已有两组样例，不提供用户投稿或在线转换。


下载页只链接仓库 Releases，不在浏览器请求 GitHub API，不写死最新安装包版本。便携包与 SHA256SUMS.txt 的存在已通过 Release API 核对；命令要求访客替换实际 ZIP 版本号。所有复制操作的状态显示在对应面板内，拒绝剪贴板权限时保留手动选择。下载及校验在访客设备进行，网页不读取、上传文件。


0.11.0 本地验收覆盖五页 320px / 1024px 布局、跨页主题、手机菜单、复制成功及模拟拒绝、作品筛选与计数、滑块 Home/End、对话框关闭与焦点恢复、回到顶部、无脚本静态作品下载、组件 CDN 故障和模拟减少动态效果。生成检查确保文案与源文件一致，发布素材与仓库样例逐字节相同。桌面程序未修改，CI 仍执行原有构建与测试。


动态日志额外执行 `node scripts/check-website-history.mjs`，验证当前仓库全部版本/条目、CRLF、分类、非法输入和原样保留 HTML 文本。JavaScript 类型与 lint 检查包含 `website/dist/changelog.js`。
