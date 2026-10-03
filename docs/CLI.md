# AsciiStudio CLI

## 版本与范围

`1.0.0-alpha.1` 是首个控制台预发布，Windows x64 / ARM64 ZIP 同时包含 `AsciiStudio.exe` 和 `asciistudio-cli.exe`。全部解压后在终端运行；无需开发 SDK。

**1.0 的最终验收要求是桌面功能全部有命令入口，而非仅支持转换。alpha.1 尚未达到全部覆盖。** 中文／英文桌面界面、新个性化设置和扩展动效也在后续预发布实现。正式 1.0.0 仍需用户明确授权。

## 开始使用

```powershell
.\asciistudio-cli.exe --help --language zh-CN
.\asciistudio-cli.exe image --help --language en-US
.\asciistudio-cli.exe text --text "Hello" --figlet-font Standard
.\asciistudio-cli.exe image --input photo.png --columns 120 --set Color=true --format ANSI --output art.ans --save-project art.asciiproj
.\asciistudio-cli.exe export --project art.asciiproj --format PNG --output art.png
.\asciistudio-cli.exe capabilities --json
```

每个命令和命令组支持 `--help`，包括参数、默认值、范围、枚举、示例、资源限制和退出码。帮助不分页，可重定向到文件。`--language system|zh-CN|en-US` 切换帮助语言；部分共享服务错误与分析内容暂保留原语言，固定机器错误码不随语言改变。

## alpha.1 功能覆盖

| 桌面能力 | 命令入口 | 状态 |
|---|---|---|
| 图片全部质量参数、比例补偿、原图尺寸、裁剪／旋转／翻转 | `image`，`--options`，重复 `--set`，`--geometry` | 已实现；几何历史待补 |
| FIGlet、系统字形、中文描边／填充、换行与排版 | `text`，`--options`，`--layout` | 已实现；缺字默认报错 |
| ANSI 编码、SAUCE、列数、iCE | `ansi` | 已实现 |
| 七种现有生成器及全部配方参数 | `generate` | 已实现 |
| 九种作品导出格式 | `export`，各转换命令 `--format` | 已实现 |
| 项目读取、校验、旧版本升级、来源重新生成 | `project info/validate/migrate/regenerate` | 已实现；编辑结果受保护 |
| 九种文本整理 | `tools analyze/trim/clean/ascii/upper/lower/mirror/flip/expand-tabs` | 已实现；默认输出副本 |
| 所有现有代码注释语言、结束符检查 | `comment --list`，`comment --syntax` | 已实现；变量包装待补 |
| 所有现有加密／编码／压缩／摘要／传统密码 | `crypto algorithms/apply/keys` | 已实现；平台支持可查询 |
| 字体搜索、导入、收藏、系统筛选、示例预览 | `fonts list/import/favorite/sample` | 已实现 |
| 全部现有偏好查看、修改、重置、导入／导出 | `settings show/set/reset/import/export` | 已实现；不允许通过 `--set` 修改最近文件数组 |
| 目录图片批量转换、递归开关、逐项报告 | `batch image` | 已实现；当前串行，最多 1000 项 |
| 替换／插入／删除／查找／选区变换、持久撤销重做 | 后续 `edit/history` | 待实现，100 步／64 MB |
| 多项目工作区、active 项目、最近文件、会话恢复 | 后续 `workspace/recent/recovery` | 待实现 |
| 候选生成结果接受／保留／独立保存 | 后续项目结果管理 | 待实现 |
| 剪贴板图片／文字读写 | 后续 `clipboard` | 待实现，显式调用 |
| 缩放、适应、分页、行跳转、选区定位、原图对比 | 后续 `preview/selection` | 待实现，TXT／ANSI／PNG／HTML 预览 |
| 设置搜索／工具栏收藏、个人配方、平台建议 | 后续设置与配方命令 | 待实现，保留输入和编辑结果 |
| 教程、新手说明与全部导航功能对应 | 后续 `tutorial` 与帮助扩展 | 待实现 |

以 `capabilities` 的机器可读覆盖表和上述矩阵共同跟踪验收。冻结的动画／视频、摄像头与 3D 不进入此次功能范围。

## 参数、输入与文件

- 参数优先级：命令参数／`--set` ＞ `--options`／`--layout`／`--geometry` JSON ＞项目参数＞默认值。JSON 未知字段、非法枚举和非有限数值报错。
- 文本来源四选一：`--text`、UTF-8 `--input`、`--stdin`、`--project`。文本 stdout 不额外加换行；需要脚本精确 UTF-8 输入时直接使用重定向文件或 UTF-8 管道。
- `--set Name=Value` 支持模型字段；嵌套用 `geometry.Left=10` 或 `layout.MaximumWidth=80`。完整字段名与默认值由对应命令 `--help` 提供。
- `--output` 默认拒绝覆盖；只有 `--overwrite` 可覆盖。项目与导出结果须使用不同路径。单文件写入采用同目录临时文件后替换；多文件输出不是事务。
- 工具只输出副本。未来 `--apply` 写回入口会遵守编辑保护；当前不提供该参数。重新生成到原编辑项目须同时显式 `--replace-edited` 和 `--overwrite`。
- 图像 40 MB／8000 万像素，文本 8 MB，ANSI 4 MB；算法可能有更低限制。位图最多 4000 万像素且每边不超过 32767，HTML／SVG 最多 1600 万标记字符，解压上限 4 MB。
- 批处理默认仅当前目录，`--recursive` 才递归，不跟随目录链接；输出目录必须在输入目录之外。同批重名自动编号，已有文件仍须 `--overwrite`；失败继续，报告每项状态并返回 5。

## 数据隔离与安全

默认 CLI 数据位于 `%LOCALAPPDATA%/AsciiStudio/Cli`，不会恢复或改写桌面会话。`--data-directory` 可指定隔离目录，`--desktop-data` 明确访问桌面设置，两者互斥。字体默认使用桌面 `%LOCALAPPDATA%/AsciiStudio/fonts`，`--font-directory` 可另设；导入／收藏会修改所选字体库。

加密口令从 `--password-file` 或 `--password-stdin` 读取，不接受命令行明文口令。口令 stdin 与文本 stdin 互斥，末尾换行不计入口令。RSA 公私钥通过 `--key-file` 导入；`crypto keys --output ./keys` 显式生成 3072 位 PEM 文件，私钥不打印，文件默认拒绝覆盖。传统密码用于教学，编码与压缩不提供保密性，摘要不可还原；现代保密用途优先 AES-256-GCM。

未来编辑状态保存在项目旁独立状态文件；撤销记录持久化到独立工作区，项目列表与活动项目独立保存，默认仍与桌面隔离。矩形选区按 Unicode 显示列计数。

## 输出与退出码

作品默认直接写 stdout，错误写 stderr。查询命令返回 JSON；`--json` 使作品输出和错误也使用结构化封装。`--quiet` 关闭可选警告，不隐藏错误。Ctrl+C 取消。

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 2 | 参数／用法错误 |
| 3 | 输入校验或转换失败 |
| 4 | 文件读写失败 |
| 5 | 批处理部分失败 |
| 130 | 取消 |

当前首版不提供终端交互编辑器。后续所有命令须同步加入中英文逐级帮助和桌面功能一致性测试。
