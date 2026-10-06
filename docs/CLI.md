# Charloom CLI

从安装到创作、编辑、恢复和自动化，参见 [命令行详细教程](CLI_TUTORIAL.md)。本页为功能覆盖与行为约定。

## 版本与范围

当前源码控制台版本为 `1.0.0-alpha.6`，共有 69 个命令（包含 Unreleased 新增的 5 个几何历史入口；已发布版本以对应 help 为准）。Windows x64 / ARM64 ZIP 同时包含 `Charloom.exe` 和 `charloom-cli.exe`。全部解压后在终端运行；无需开发 SDK。旧 CLI 名称 `asciistudio-cli.exe` 在首个公开 Charloom 预发布周期保留为同实现兼容入口。默认数据目录继续使用 AsciiStudio，以保留旧偏好和字体。

**1.0 的最终验收要求是桌面功能全部有命令入口，而非仅支持转换。当前尚未达到全部覆盖。** 中文／英文桌面界面、新个性化设置和扩展动效也在后续预发布实现。正式 1.0.0 仍需用户明确授权。

## 开始使用

```powershell
.\charloom-cli.exe --help --language zh-CN
.\charloom-cli.exe image --help --language en-US
.\charloom-cli.exe text --text "Hello" --figlet-font Standard
.\charloom-cli.exe image --input photo.png --columns 120 --set Color=true --format ANSI --output art.ans --save-project art.asciiproj
.\charloom-cli.exe export --project art.asciiproj --format PNG --output art.png
.\charloom-cli.exe capabilities --json
```

每个命令和命令组支持 `--help`，包括参数、默认值、范围、枚举、示例、资源限制和退出码。帮助不分页，可重定向到文件。`--language system|zh-CN|en-US` 切换帮助语言；部分共享服务错误与分析内容暂保留原语言，固定机器错误码不随语言改变。

## 当前功能覆盖

| 桌面能力 | 命令入口 | 状态 |
|---|---|---|
| 图片全部质量参数、比例补偿、原图尺寸、裁剪／旋转／翻转 | `image`，`--options`，重复 `--set`，`--geometry`；`geometry status/set/undo/redo/reset` | 已实现；独立40步几何历史见 [几何教程](CLI_GEOMETRY.md) |
| FIGlet、系统字形、中文描边／填充、换行与排版 | `text`，`--options`，`--layout` | 已实现；缺字默认报错 |
| ANSI 编码、SAUCE、列数、iCE | `ansi` | 已实现 |
| 七种现有生成器及全部配方参数 | `generate` | 已实现 |
| 九种作品导出格式 | `export`，各转换命令 `--format` | 已实现 |
| 项目读取、校验、旧版本升级、来源重新生成、编辑保存与独立恢复 | `project info/validate/migrate/regenerate/save/reload/recover` | 已实现；编辑结果受保护 |
| 九种文本整理 | `tools analyze/trim/clean/ascii/upper/lower/mirror/flip/expand-tabs` | 已实现；默认输出副本 |
| 所有现有代码注释语言、结束符检查 | `comment --list`，`comment --syntax` | 已实现；变量包装待补 |
| 所有现有加密／编码／压缩／摘要／传统密码 | `crypto algorithms/apply/keys` | 已实现；平台支持可查询 |
| 字体搜索、导入、收藏、系统筛选、示例预览 | `fonts list/import/favorite/sample` | 已实现 |
| 全部现有偏好查看、修改、重置、导入／导出 | `settings show/set/reset/import/export` | 已实现；不允许通过 `--set` 修改最近文件数组 |
| 目录图片批量转换、递归开关、逐项报告 | `batch image` | 已实现；当前串行，最多 1000 项 |
| 替换／插入／删除／查找／选区变换、持久撤销重做 | `edit show/select/replace/insert/delete/find/transform`，`history list/undo/redo/clear` | 已实现，100 步／64 MB |
| 多项目工作区、active 项目、最近文件、会话恢复 | `workspace new/open/switch/close/list/recent/clear-recent/recovery/restore` | 已实现，CLI 独立清单与状态 |
| 候选生成结果接受／保留／独立保存 | `candidate create/status/show/accept/save/discard` | 已实现，保留手工结果和来源 |
| 剪贴板文本复制／粘贴、图片读取 | `clipboard read/write/paste` | 显式调用；文本8MB，PNG40MB/8000万像素；粘贴需apply |
| 缩放、适应、分页、行跳转、选区定位、原图对比 | 后续 `preview/selection` | 待实现，TXT／ANSI／PNG／HTML 预览 |
| 设置搜索／工具栏收藏、个人配方、平台建议 | 后续设置与配方命令 | 待实现，保留输入和编辑结果 |
| 教程、新手说明与全部导航功能对应 | [中文详细教程](CLI_TUTORIAL.md)，后续 `tutorial` 与帮助扩展 | 文档已完成；命令入口与新手说明待实现 |

以 `capabilities` 的机器可读覆盖表和上述矩阵共同跟踪验收。冻结的动画／视频、摄像头与 3D 不进入此次功能范围。

## 参数、输入与文件

- 参数优先级：命令参数／`--set` ＞ `--options`／`--layout`／`--geometry` JSON ＞项目参数＞默认值。JSON 未知字段、非法枚举和非有限数值报错。
- 文本来源四选一：`--text`、UTF-8 `--input`、`--stdin`、`--project`。文本 stdout 不额外加换行；需要脚本精确 UTF-8 输入时直接使用重定向文件或 UTF-8 管道。
- `--set Name=Value` 支持模型字段；嵌套用 `geometry.Left=10` 或 `layout.MaximumWidth=80`。完整字段名与默认值由对应命令 `--help` 提供。
- `--output` 默认拒绝覆盖；只有 `--overwrite` 可覆盖。项目与导出结果须使用不同路径。单文件写入采用同目录临时文件后替换；多文件输出不是事务。
- 工具默认输出副本；文本整理和代码注释用项目作为输入时，`--apply` 明确写入可撤销侧文件，原项目暂不改写。已有侧文件的项目再生成请输出副本，当前不覆盖其编辑会话；可使用 candidate 命令生成并保留独立候选。
- 图像 40 MB／8000 万像素，文本 8 MB，ANSI 4 MB；算法可能有更低限制。位图最多 4000 万像素且每边不超过 32767，HTML／SVG 最多 1600 万标记字符，解压上限 4 MB。
- 批处理默认仅当前目录，`--recursive` 才递归，不跟随目录链接；输出目录必须在输入目录之外。同批重名自动编号，已有文件仍须 `--overwrite`；失败继续，报告每项状态并返回 5。

## 数据隔离与安全

默认 CLI 数据位于 `%LOCALAPPDATA%/AsciiStudio/Cli`，不会恢复或改写桌面会话。`--data-directory` 可指定隔离目录，`--desktop-data` 明确访问桌面设置，两者互斥。字体默认使用桌面 `%LOCALAPPDATA%/AsciiStudio/fonts`，`--font-directory` 可另设；导入／收藏会修改所选字体库。

加密口令从 `--password-file` 或 `--password-stdin` 读取，不接受命令行明文口令。口令 stdin 与文本 stdin 互斥，末尾换行不计入口令。RSA 公私钥通过 `--key-file` 导入；`crypto keys --output ./keys` 显式生成 3072 位 PEM 文件，私钥不打印，文件默认拒绝覆盖。传统密码用于教学，编码与压缩不提供保密性，摘要不可还原；现代保密用途优先 AES-256-GCM。

编辑与撤销记录保存在项目旁 `<项目路径>.cli-state.json`，项目列表与活动项目保存到 CLI 数据目录的 `cli-workspace.json`；也可用 `--workspace PATH` 指定独立清单。CLI 清单与桌面标签／恢复文件始终独立，即使使用 `--desktop-data`。空 `.lock` 文件用于进程锁，保留它不表示任务仍在运行。历史最多 100 步且受 64 MB 预算限制，取序列化大小与文档内存估算中较大值；当前单状态超预算时保留当前结果，侧文件总计最多 100 MB。

## 持久编辑与工作区

```powershell
.\charloom-cli.exe workspace open --project art.asciiproj
.\charloom-cli.exe edit select --row 0 --column 0 --end-row 1 --end-column 3 --rectangle
.\charloom-cli.exe edit replace --selection --text "ABC"
.\charloom-cli.exe history undo
.\charloom-cli.exe tools upper --project art.asciiproj --apply
.\charloom-cli.exe project save --overwrite
.\charloom-cli.exe workspace close --action keep
.\charloom-cli.exe workspace restore
```

- 省略目标 `--project` 时，编辑／历史／项目命令与无输入的 `export` 使用工作区 active。新建 `workspace new --project NEW_PATH` 必须指定未使用路径；同一路径打开时只切换 active。工作区最多 32 项，最近项目最多 15 项，保留恢复最多 32 项。
- 所有行列从 0 开始。连续选区跨行范围按起点至终点计算；矩形包含起止行，每行末列不包含。中文、emoji 和组合字符按 Unicode 显示列，不接受截断宽字符或字符簇的边界。短行不自动补空格，越界报错；矩形替换接受单行（重复到每行）或恰好相同数量的多行。
- `edit replace` 默认替换全文；`--selection` 使用持久选区，`--find` 使用完整字符簇的字面匹配，默认首个，`--all` 全部，最多 1000 匹配。`edit insert` 在指定行列插入，`edit delete` 默认清空全文。实际文本编辑与桌面相同，会清除编辑结果的颜色网格；原生成结果与撤销快照保留颜色。
- 编辑只改侧文件；`project save --overwrite` 保存原路径。`project save --output NEW_PATH` 保存副本，不切换工作区活动路径。撤销后进行新编辑会清除 redo；保存后仍能撤销，脏状态根据已保存修订计算。
- 未保存关闭默认取消；`--action keep` 留在恢复列表，`--action save --overwrite` 保存后关闭。重启时自动读取之前全部打开项目，`workspace list` 报告各项目是否可用及未保存状态。恢复失败返回 5 并保留失败记录。
- 检测到原项目被外部改写后停止编辑／保存，不静默覆盖。`project recover --project OLD_PATH --output NEW_PATH` 从侧文件恢复作品、生成基准和历史到新快照项目，原来源已失效时不保留其图片／文字参数绑定。`project reload --discard-edits` 则明确丢弃侧文件历史，重新读取磁盘文件。多文件保存与恢复不是事务；冲突或中断时先保留侧文件。

## 输出与退出码

作品默认直接写 stdout，错误写 stderr。查询命令返回 JSON；`--json` 使作品输出和错误也使用结构化封装。`--quiet` 关闭可选警告，不隐藏错误。Ctrl+C 取消。

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 2 | 参数／用法错误 |
| 3 | 输入校验或转换失败 |
| 4 | 文件读写失败／状态占用或外部修改冲突（固定 code: conflict） |
| 5 | 批处理／恢复部分失败 |
| 130 | 取消 |

当前不提供终端交互编辑器。后续所有命令须同步加入中英文逐级帮助和桌面功能一致性测试。

## 剪贴板

alpha.6 开发源码提供显式剪贴板命令；已发布的 alpha.5 尚无这些入口。不会监听剪贴板，也不会自动读取、转换或替换作品。

- `clipboard read` 原样输出 Unicode 文本，不额外添加换行；`--output` 保存无 BOM UTF-8，可用 `--json` 返回结构化文本。
- `clipboard read --format PNG --output image.png` 读取注册 PNG 或 Windows 位图；图片必须显式指定输出路径，默认拒绝覆盖。传统 Windows 位图来源可能不含透明度；PNG 来源保留原字节。
- `clipboard write --text/--input/--stdin/--project` 四选一，复制纯文本；项目输入使用侧文件中的当前编辑结果，不接受候选，不保存原文件。
- `clipboard paste --project art.asciiproj --apply` 写入可撤销编辑状态。省略 project 时使用 active；`--selection` 粘贴到已有 Unicode 选区。粘贴时换行／制表符沿用编辑器的标准化。保存仍使用 project save。
- 文本上限8MB UTF-8，图片40MB PNG／8000万像素；NUL、损坏编码与超限输入拒绝处理。打开剪贴板最多尝试5次，间隔50ms；占用返回退出码4与固定 `clipboard_busy`，缺失／无效内容为退出码3与 `clipboard_format`。其他 I/O 使用既有错误码。
- Windows剪贴板不是事务；写入前验证和准备内存，但系统在清空旧内容后若传输失败，旧内容可能已改变。明确运行 write 才会改动系统剪贴板。
- 自动测试使用注入替身，检查命令、文件保护、历史及错误。真实系统剪贴板只在显式验收时使用，本轮未读取或改写个人剪贴板。
