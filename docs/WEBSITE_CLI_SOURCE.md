# AsciiStudio 命令行详细教程

适用版本：**1.0.0-alpha.2**，Windows x64／ARM64。教程使用 PowerShell。命令功能与参数以当前程序的 `--help` 为准；[覆盖表](CLI.md)列出尚未实现的桌面功能。

## 目录

1. [安装与终端准备](#1-安装与终端准备)
2. [五分钟入门](#2-五分钟入门)
3. [命令、路径与输入输出](#3-命令路径与输入输出)
4. [图片转换与质量调整](#4-图片转换与质量调整)
5. [文字转换、中文与字体](#5-文字转换中文与字体)
6. [ANSI 与生成器](#6-ansi-与生成器)
7. [保存项目与九种导出](#7-保存项目与九种导出)
8. [持久编辑、选区与撤销](#8-持久编辑选区与撤销)
9. [多项目工作区与恢复](#9-多项目工作区与恢复)
10. [文本整理与代码注释](#10-文本整理与代码注释)
11. [编码、压缩、校验与加密](#11-编码压缩校验与加密)
12. [批处理与脚本](#12-批处理与脚本)
13. [设置、隔离与字体库](#13-设置隔离与字体库)
14. [常见错误与资源限制](#14-常见错误与资源限制)

## 1. 安装与终端准备

在 [GitHub Releases](https://github.com/msmapwr/ascii-studio/releases) 确认是否提供与教程匹配的版本，再下载对应电脑架构的 ZIP，**完整解压**。同目录应有 `AsciiStudio.exe`、`asciistudio-cli.exe`、DLL 和资源文件；不要只复制 EXE。程序已包含运行时，不需要安装 .NET SDK。

在解压目录打开 PowerShell，或用下面的命令进入目录。示例安装路径请改成你的实际路径：

```powershell
Set-Location 'D:\Apps\AsciiStudio'
$cli = (Resolve-Path '.\asciistudio-cli.exe').Path
& $cli --version
& $cli --help --language zh-CN
```

下文的 `& $cli` 等同于在此目录运行 `.\asciistudio-cli.exe`。变量保存绝对路径，因此切到作品目录后仍可使用。

建议把作品放到单独目录，便于管理项目、图片和导出文件：

```powershell
New-Item -ItemType Directory -Path '.\my-art' -Force | Out-Null
Set-Location '.\my-art'
```

这些命令不是安装操作。CLI 可以在桌面程序没有启动时独立运行。当前程序的中英文命令名相同，`--language` 改变帮助语言，不改变参数名；桌面界面的中英文切换仍在开发。

## 2. 五分钟入门

### 直接把文字变成 ASCII

```powershell
& $cli text --text 'Hello' --figlet-font Standard
```

结果直接显示在终端。保存到文件时增加 `--output`：

```powershell
& $cli text --text 'Hello' --output hello.txt --save-project hello.asciiproj
& $cli export --project hello.asciiproj --format PNG --output hello.png
```

`hello.txt` 是普通文本，`hello.png` 是图像，`hello.asciiproj` 保存原始输入与转换参数，之后可在桌面端打开或用 CLI 再生成。再次写入同名文件会报错；确实需要覆盖时增加 `--overwrite`。

### 不准备图片，也能体验持久编辑

```powershell
& $cli workspace new --project practice.asciiproj --text 'a中b'
& $cli edit show
& $cli edit select --row 0 --column 1 --end-column 3
& $cli edit replace --selection --text X
& $cli edit show
& $cli history undo
& $cli edit show
```

依次会看到 `a中b`、`aXb`、`a中b`。每条命令是独立进程，但编辑和撤销仍然保留。这里中文“中”占两个显示列，范围 `[1,3)` 恰好选择它。

`workspace new` 指定的路径必须不存在；重做后显式保存：

```powershell
& $cli history redo
& $cli project save --overwrite
```

## 3. 命令、路径与输入输出

### 如何查询参数

```powershell
& $cli --help --language zh-CN
& $cli image --help --language zh-CN
& $cli edit --help --language zh-CN
& $cli project recover --help --language en-US
```

根帮助列出全部命令，命令组帮助列出子命令，具体命令帮助给出参数、默认值、范围、枚举和示例。帮助不分页，可以保存：

```powershell
$helpText = & $cli image --help --language zh-CN
[IO.File]::WriteAllText((Join-Path $PWD 'image-help.txt'), ($helpText -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
```

### 含空格的路径与参数要加引号

```powershell
& $cli image --input 'D:\Pictures\my photo.png' --font 'Cascadia Mono' --output 'my art.txt'
```

字体必须已安装；先用 `fonts list --system` 查询。参数与枚举采用帮助中的拼写，例如格式是 `PNG`、`JPEG`、`Markdown`，不是 `png`、`JPG`、`markdown`。`--set` 字段名可忽略大小写，但建议照帮助书写。

### 文本输入四选一

| 参数 | 用途 |
|---|---|
| `--text 'Hello'` | 一小段直接输入的文字 |
| `--input input.txt` | UTF-8 文本文件；图片／ANSI 命令按原始字节读取 |
| `--stdin` | 从标准输入读取，适合其他程序的管道 |
| `--project work.asciiproj` | 使用项目中的结果或转换来源，具体行为见命令帮助 |

普通转换／工具的这些输入来源互斥。编辑命令的 `--project` 是目标，因此可以同时给 `--text` 作为替换内容。

保存 UTF-8 文本的可重复方法：

```powershell
[IO.File]::WriteAllText((Join-Path $PWD 'input.txt'), "Hello`nASCII", [Text.UTF8Encoding]::new($false))
& $cli text --input input.txt --output input-art.txt
```

文件可以包含实际换行。写成“反斜杠 + n”的两个字符不会被 `--text` 自动解释成换行；PowerShell 中可在双引号字符串里使用反引号后跟 n，如上例。

CLI 使用严格 UTF-8 stdin，stdout 不主动补换行。由于外层 shell 可能改变管道编码或追加换行，编码／哈希需要精确字节时优先用 UTF-8 输入文件和 `--output`，不要依赖 shell 的默认文本重定向。

### 默认不会覆盖已有文件

```powershell
& $cli text --text Hello --output hello.txt --overwrite
```

只有确认要替换输出文件时使用 `--overwrite`。它不绕过项目编辑保护、侧文件保护、工作区保护或恢复目标的“必须新路径”要求。

## 4. 图片转换与质量调整

先把一张图片放入作品目录，下面假定文件名为 `photo.png`。

### 基本转换：保持比例

```powershell
& $cli image --input photo.png --columns 120 --output photo.txt --save-project photo.asciiproj
```

`--columns` 是字符列数，范围 8–2000，默认 120。默认 `Rows=0`，按照原图比例与所选字体的实测宽高比推导行数。字符通常不是正方形，默认补偿可减少图像变形。

固定行列会拉伸到指定网格，仅在有明确目标格数时使用：

```powershell
& $cli image --input photo.png --columns 100 --rows 40 --output fixed-grid.txt
```

按原图像素与实测字符尺寸推导网格：

```powershell
& $cli image --input photo.png --native-size --output native-grid.txt
```

这不是“一个像素变成一个字符”，也不是固定 1920×1080 网格。大图仍受列数和资源上限约束。原图尺寸模式不要另外指定行数。

### 常用质量参数

```powershell
& $cli image --input photo.png --columns 160 --set Brightness=1.1 --set Contrast=1.2 --set Gamma=1.0 --set MeasureGlyphDensity=true --output detailed.txt
```

| 参数 | 作用与常用值 |
|---|---|
| `Characters` | 从浅到深的字符序列，例如 `' .:-=+*#%@'` |
| `Brightness`／`Contrast`／`Gamma` | 亮度、对比、伽马，默认均为 1 |
| `MeasureGlyphDensity=true` | 按所选显示字体实测字形密度 |
| `Style=Density` | 常规明暗字符画 |
| `Style=Structure` | 强调轮廓和结构 |
| `Style=Braille` | Unicode 盲文点阵，细节更密集，输出不是严格 ASCII |
| `Style=HalfBlock` | 前景／背景双色半块，适合彩色 ANSI |
| `Dither=FloydSteinberg` | 用误差扩散表现明暗过渡；另有 JarvisJudiceNinke、Stucki、Atkinson |
| `Color=true` | 保留颜色；TXT 不包含颜色，导出 ANSI／图像／HTML 等才能体现 |
| `PaletteMode=Ansi16`／`Ansi256` | 限制到相应终端色彩 |
| `Invert=true` | 反转明暗 |

设置字符序列和颜色：

```powershell
& $cli image --input photo.png --set 'Characters= .:-=+*#%@' --set Dither=Atkinson --output dither.txt
& $cli image --input photo.png --set Style=HalfBlock --set Color=true --format ANSI --output color.ans
& $cli image --input photo.png --set Style=Braille --set Color=true --format HTML --output braille.html
```

黑白文本的颜色由终端决定；要保留作品的字体、色彩或展示效果，使用图像或 HTML 导出。

### 裁剪、旋转与翻转

```powershell
& $cli image --input photo.png --set geometry.Left=10 --set geometry.Top=10 --set geometry.Width=80 --set geometry.Height=80 --set geometry.QuarterTurns=1 --set geometry.FlipHorizontal=true --output cropped.txt
```

裁剪单位是百分比，以上保留中心 80%×80%。`Left+Width`、`Top+Height` 不能超过 100。`QuarterTurns` 为顺时针 90° 的次数：0／1／2／3 对应 0°／90°／180°／270°。顺序是**裁剪 → 旋转 → 翻转**，不修改原图片文件。

### JSON 参数文件：便于复用

创建 `image-options.json`：

```json
{
  "Columns": 160,
  "Rows": 0,
  "Color": true,
  "Style": "Density",
  "MeasureGlyphDensity": true,
  "Dither": "FloydSteinberg",
  "Contrast": 1.15
}
```

使用它，并临时覆盖亮度：

```powershell
& $cli image --input photo.png --options image-options.json --set Brightness=1.05 --format HTML --output tuned.html
```

优先级是：明确命令参数／`--set` ＞ JSON 文件中的字段 ＞项目保存参数＞默认值。JSON 未提供的字段不会重置已有项目参数。未知字段、非法枚举和 `NaN` 等非有限数值会报错。

几何参数另放 `geometry.json`，使用 `--geometry geometry.json`；不要把几何字段混入图片质量 JSON。

## 5. 文字转换、中文与字体

### FIGlet：适合拉丁字符标题

```powershell
& $cli fonts list --search Standard
& $cli fonts sample --figlet-font Standard
& $cli text --text 'ASCII Studio' --figlet-font Standard --output title.txt
```

字体样本默认 `abc`，可指定当前文字：

```powershell
& $cli fonts sample --figlet-font Standard --text Hello
```

FIGlet 字体名或 ID 来自 `fonts list`，不要把系统字体名当作 FIGlet 名。中文通常使用下面的 `raster` 模式。

### 中文：系统字体栅格化

```powershell
& $cli fonts list --system --chinese
& $cli text --text '测试 ASCII' --mode raster --system-font 'Microsoft YaHei UI' --set Columns=120 --output chinese.txt --save-project chinese.asciiproj
& $cli export --project chinese.asciiproj --format PNG --font-size 18 --scale 2 --output chinese.png
```

生成参数中的 `Columns` 决定组成字形的字符数量；导出的 `--font-size` 决定每个结果字符的显示大小，两者不同。中文看不清时，先选有中文支持的来源字体并提高生成列数，再提高图像导出字号或倍率。

空心描边与粗体：

```powershell
& $cli text --text '中文' --mode raster --set Columns=160 --set Bold=true --set Filled=false --set Stroke=2 --output outline.txt
```

`Stroke` 为 0–12；空心字必须大于 0。`Style` 为 0 标准、1 细线、2 点阵、3 方块、4 高密度。默认缺字报错；明确接受缺字时增加 `--allow-missing`，警告写 stderr。

`--system-font` 决定来源字形；`--font` 决定生成结果的显示字体和字符比例。两者用途不同。

### 排版、换行与标题边框

```powershell
& $cli text --text 'Hello ASCII Studio' --set layout.MaximumWidth=100 --set layout.Wrap=true --set layout.Alignment=Center --set layout.LetterSpacing=1 --set layout.Border=1 --set layout.Title=Demo --set layout.PaddingX=2 --output framed-title.txt
```

自动换行需要正的 `MaximumWidth`；默认不换行。排版支持 `Horizontal`／`Vertical` 的 Default、Full、Kern、Smush。宽度不足以容纳单个字形时，应增大宽度或换用较窄字体。完整选项见 `text --help`。

### 导入、收藏与查询字体

```powershell
& $cli fonts import --input custom.flf
& $cli fonts list --search custom
& $cli fonts favorite --figlet-font Standard
& $cli fonts list --favorites
& $cli fonts list --system --monospace
```

`fonts favorite` 是切换收藏，再执行一次会取消。导入字体经验证后复制到字体库，不要求每次保留原 `.flf` 路径。使用其他电脑缺少导入字体时，项目结果仍在，但重新生成前应重新导入或换字体。

## 6. ANSI 与生成器

### ANSI：读取已有彩色字符画

```powershell
& $cli ansi --input existing.ans --encoding Auto --format HTML --output existing.html --save-project existing.asciiproj
& $cli ansi --input existing.ans --encoding CP437 --columns 80 --ice --format PNG --output existing.png
```

编码支持 Auto、UTF-8、CP437。有效的 SAUCE 信息可提供默认宽度和 iCE 标记；明确的命令参数覆盖它。列数范围 20–300。查看原终端效果可导出 ANSI；网页观看可导出 HTML。

### 七种现有生成器

| `Kind` | 内容 |
|---|---|
| 0 | 文字边框 |
| 1 | 分隔线 |
| 2 | 迷宫 |
| 3 | 星点 |
| 4 | 棋盘 |
| 5 | 条纹 |
| 6 | 渐变 |

```powershell
& $cli generate --text 'Hello' --set Kind=0 --set Style=1 --output border.txt
& $cli generate --set Kind=1 --set Width=60 --set Style=2 --output divider.txt
& $cli generate --set Kind=2 --set Width=40 --set Height=20 --set Seed=42 --output maze.txt
```

相同迷宫尺寸和 Seed 可复现结果。宽度通常 8–300，高度 3–100；迷宫上限为 100×80。边框文字另有每行和行数上限。

## 7. 保存项目与九种导出

### 项目与普通结果的区别

- `.asciiproj`：保存转换来源、参数和作品，可继续创作。
- `TXT`：只有字符，没有图片来源、颜色或转换参数。
- 导出的 `JSON`：作品文档 JSON，**不是项目 JSON**，不能改扩展名后当 `.asciiproj` 打开。

```powershell
& $cli project info --project photo.asciiproj --json
& $cli project validate --project photo.asciiproj
& $cli project regenerate --project photo.asciiproj --output regenerated.txt
```

再生成按项目原始来源和参数工作。它不会因你改了外部 `photo.png` 就自动改用新图片；图片项目保存的是来源数据。

### 导出格式

| `--format` | 用途 |
|---|---|
| TXT | 普通文本 |
| PNG | 无损图像，可透明 |
| JPEG | 有损图像，不支持透明 |
| GIF | 静态图像，不是动画 |
| HTML | 浏览器展示字体和颜色 |
| SVG | 可缩放矢量作品 |
| ANSI | 带终端颜色控制序列的文字 |
| JSON | 作品文档及颜色网格 |
| Markdown | Markdown 代码块 |

```powershell
& $cli export --project photo.asciiproj --format PNG --font-size 14 --scale 2 --output photo-export.png
& $cli export --project photo.asciiproj --format PNG --transparent --output transparent.png
& $cli export --project photo.asciiproj --format SVG --output photo.svg
& $cli export --project photo.asciiproj --format Markdown --output photo.md
```

图像必须提供输出文件。字号 1–120、倍率 1–4；总像素预算仍会限制很大的结果。PNG 透明画布与图片转换的 `PreserveTransparent` 是不同参数。

### 旧项目升级

```powershell
& $cli project migrate --project old.asciiproj --output upgraded.asciiproj
```

优先另存新路径。旧格式原路径保存时需 `--overwrite`，并保留最近一份迁移前 `.bak`。若项目已有 CLI 编辑侧文件，先使用 `project save` 管理当前编辑，不直接用 migrate 或再生成覆盖侧文件会话。

## 8. 持久编辑、选区与撤销

编辑结果在 `<项目文件>.cli-state.json` 中，原 `.asciiproj` 暂不变。读取该项目的导出、工具和编辑命令会看到当前编辑版本。

### 全文替换、插入、删除

```powershell
& $cli workspace new --project edit-demo.asciiproj --text "abc`ndef"
& $cli edit replace --text "Hello`nASCII"
& $cli edit insert --row 0 --column 5 --text '!'
& $cli edit show
```

`edit replace` 没有 `--find` 或 `--selection` 时替换全文；`edit insert` 使用显示列，不是 UTF-16 字符索引。`edit delete` 没有 `--selection` 时清空全文，可以撤销。

### 持久选区

```powershell
& $cli edit select --row 0 --column 0 --end-row 0 --end-column 5
& $cli edit show --selection
& $cli edit transform --selection --operation upper
```

行列从 0 开始，末列不包含。选区写入侧文件，所以后续进程能使用。实际编辑、撤销或重做之后清除旧选区，避免落到错误位置。

矩形选区包含起止行，每行使用相同列范围：

```powershell
& $cli edit select --row 0 --column 0 --end-row 1 --end-column 2 --rectangle
& $cli edit replace --selection --text XX
```

单行替换文字会重复到每行；多行替换必须恰好等于选区行数。短行不会自动补空格。切到中文／emoji 宽字符的中间会报错；组合字符和 emoji 序列不会被拆开。

### 查找与替换

```powershell
& $cli edit find --find ASCII --ignore-case
& $cli edit replace --find ASCII --text ART --all --ignore-case
```

查找是字面文本，不是正则表达式；结果返回行列坐标，匹配必须覆盖完整字符簇。默认只替换第一个，`--all` 替换全部，超过 1000 个匹配时报错，请缩小查找范围。

### 撤销、重做、保存

```powershell
& $cli history list
& $cli history undo
& $cli history redo
& $cli project save --overwrite
& $cli project save --output edit-demo-copy.asciiproj
```

最多 100 步、64 MB 历史预算，超限时移除最旧记录；保留当前结果。撤销后再编辑会清掉 redo 分支。保存后历史仍在，撤销到未保存修订会再次显示 dirty。

另存副本不会改变 active 项目；需要编辑副本时再 `workspace open --project edit-demo-copy.asciiproj`。`history clear` 只清掉旧记录，**保留当前未保存结果**，不等于丢弃编辑。

文字编辑会清除当前结果颜色网格；撤销和原生成基准保留之前颜色。需要原样保留彩色作品时，先保存项目或输出副本。

## 9. 多项目工作区与恢复

### 打开、切换与隔离

```powershell
& $cli workspace open --project photo.asciiproj
& $cli workspace open --project hello.asciiproj
& $cli workspace list
& $cli workspace switch --project photo.asciiproj
& $cli edit show
```

同一路径只打开一次并切为 active。每个项目的来源和编辑历史独立，最多 32 个打开项目。工作区清单每次启动都会读取；不需要常驻进程来保持“标签”。

不同工作区可显式指定清单路径，后续命令保持使用同一参数：

```powershell
& $cli workspace open --workspace '.\poster-workspace.json' --project photo.asciiproj
& $cli edit show --workspace '.\poster-workspace.json'
```

省略 `--project` 的 edit／history／project 命令，以及没有其他输入的 export，使用 active。普通 text/image 转换仍需明确输入；转换到新项目后用 `workspace open` 加入清单。

### 未保存关闭与恢复

```powershell
& $cli workspace close --action keep
& $cli workspace recovery
& $cli workspace restore
```

- 默认 `--action cancel`：未保存时取消关闭；干净项目可关闭。
- `--action keep`：关闭并保留恢复记录，不改原项目。
- `--action save --overwrite`：保存原项目后关闭。

恢复最多 32 项，失败逐项报告，返回退出码 5，失败记录不删除。最近项目用 `workspace recent` 查询，`workspace clear-recent` 仅清掉列表。

### 原项目被其他程序改写或删除

CLI 会检查源文件 SHA-256，检测到冲突后停止编辑和保存，保留侧文件。要保留之前作品：

```powershell
& $cli project recover --project edit-demo.asciiproj --output recovered-demo.asciiproj
& $cli workspace open --project recovered-demo.asciiproj
```

恢复的是作品、原生成基准和撤销历史，生成独立 snapshot 项目；失效的图片／文字来源绑定不保留。输出必须新路径，不能覆盖旧项目。

如果确实放弃 CLI 编辑，重新读磁盘版本：

```powershell
& $cli project reload --project edit-demo.asciiproj --discard-edits
```

这个操作明确丢弃侧文件编辑和历史，不会删除或修改原项目。原文件已经删除时先 recover，不能 reload。

## 10. 文本整理与代码注释

### 默认只输出副本

```powershell
& $cli tools analyze --project photo.asciiproj
& $cli tools trim --project photo.asciiproj --output trimmed.txt
& $cli tools upper --text Hello
& $cli tools mirror --text 'abc'
```

九种工具：analyze、trim、clean、ascii、upper、lower、mirror、flip、expand-tabs。`mirror` 按字符簇逐行反转，`flip` 反转行顺序；`ascii` 会删除非 ASCII 内容，中文也会被移除。

明确写入可撤销编辑：

```powershell
& $cli tools trim --project photo.asciiproj --apply
& $cli history undo --project photo.asciiproj
```

`--apply` 只支持以项目为输入的文本变换和注释，analyze 不支持。原文件仍等到 `project save` 才改写。

### 一键套编程语言注释

```powershell
& $cli comment --list
& $cli comment --project hello.asciiproj --syntax Python --output hello-comment.py
& $cli comment --project hello.asciiproj --syntax HTML --block --output hello-comment.html
& $cli comment --project hello.asciiproj --syntax Python --apply
```

语言名称以 `comment --list` 为准。默认使用该语言支持的行注释；块注释需要结束符冲突检查，冲突会报错而不会生成错误代码。这里生成的是注释，不是语言变量；变量包装仍在计划中。

## 11. 编码、压缩、校验与加密

### 先查询支持的方法

```powershell
& $cli crypto algorithms --json
```

每项包含方法名、当前平台是否支持、是否可逆。方法名需要完整匹配输出，例如 `Base64URL`、`AES-256-GCM`。这是当前已有的方法集合，不代表支持所有密码算法。

### 字符编码与 Base64

```powershell
& $cli crypto apply --algorithm UTF-8 --text '中' --output utf8.hex
& $cli crypto apply --algorithm UTF-8 --input utf8.hex --reverse --output restored.txt
& $cli crypto apply --algorithm UTF-16LE --text '测试' --base64-bytes --bom --output utf16.base64
& $cli crypto apply --algorithm UTF-16LE --input utf16.base64 --base64-bytes --reverse --output utf16-restored.txt
& $cli crypto apply --algorithm Base64 --input input.txt --output input.base64
& $cli crypto apply --algorithm Base64 --input input.base64 --reverse --output decoded.txt
```

字符编码默认以 Hex 表示字节，`--base64-bytes` 可改为 Base64；还原时保持同样字节表示。默认无 BOM，`--bom` 可添加 UTF BOM。不支持的字符严格报错，例如 ASCII 不能表示中文。编码只改变表示方式，不提供保密性。

### 压缩与摘要

```powershell
& $cli crypto apply --algorithm GZIP --input input.txt --output input.gzip.base64
& $cli crypto apply --algorithm GZIP --input input.gzip.base64 --reverse --output uncompressed.txt
& $cli crypto apply --algorithm SHA-256 --input input.txt --output input.sha256.txt
& $cli crypto apply --algorithm CRC32 --input input.txt
```

GZIP／Zlib／Brotli 的输出是压缩字节的 Base64 文本，不是直接的 `.gz` 二进制文件；解压上限 4 MB。摘要和校验不可 `--reverse`，NFC／NFKC 规范化也不可还原。

### 口令加密

先用文本编辑器创建 UTF-8 的 `password.txt`，内容为你的口令。不要把口令写入命令参数；末尾换行不会计入口令。

```powershell
& $cli crypto apply --algorithm AES-256-GCM --input input.txt --password-file password.txt --output encrypted.txt
& $cli crypto apply --algorithm AES-256-GCM --input encrypted.txt --password-file password.txt --reverse --output decrypted.txt
```

现代封装使用随机盐与随机 nonce，因此同一输入与口令产生不同密文是正常的。错误口令或损坏密文报错。也可用 `--password-stdin` 从另一个进程读取口令，但不能同时用 `--stdin` 读取正文。

### RSA 混合加密

```powershell
& $cli crypto keys --output '.\keys'
& $cli crypto apply --algorithm 'RSA-OAEP-SHA256 + AES-256-GCM' --input input.txt --key-file '.\keys\public.pem' --output rsa-encrypted.txt
& $cli crypto apply --algorithm 'RSA-OAEP-SHA256 + AES-256-GCM' --input rsa-encrypted.txt --key-file '.\keys\private.pem' --reverse --output rsa-decrypted.txt
```

生成 3072 位公私钥，文件默认拒绝覆盖；私钥不打印到 stdout。公钥用于加密，私钥用于解密。传统密码如 ROT13、Caesar 用于教学，不用于保护敏感内容。压缩、编码和哈希与加密用途不同。

## 12. 批处理与脚本

### 批量转换图片

```powershell
& $cli batch image --input '.\photos' --output '.\results' --columns 120 --set Color=true --format PNG
& $cli batch image --input '.\photos' --output '.\results-recursive' --recursive --format HTML
```

默认只处理当前目录，`--recursive` 才递归。输出目录必须在输入目录之外，不能放到 `photos\results`。最多 1000 张，当前串行处理；同批重名自动编号，已有文件仍需 `--overwrite`。

单项失败会继续其他项，报告每项输入、输出和状态。部分失败返回 5，不要只看到若干结果文件就认为全部成功。

### 结构化输出与退出码

```powershell
$raw = & $cli project info --project hello.asciiproj --json
if ($LASTEXITCODE -ne 0) { throw '读取项目失败' }
$report = ($raw -join "`n") | ConvertFrom-Json
$report.result.Width

& $cli batch image --input '.\photos' --output '.\script-results' --format TXT --json
switch ($LASTEXITCODE) {
    0 { '全部成功' }
    5 { '部分失败，请检查逐项报告' }
    default { throw "任务失败，退出码：$LASTEXITCODE" }
}
```

JSON 查询结果通常为 `{ "ok": true, "command": "...", "result": ... }`。错误写 stderr，`--json` 错误含固定 code。成功的查询命令通常直接返回 JSON；`--json` 也能给普通作品输出加封装。`--quiet` 不隐藏错误。

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 2 | 参数、用法、选区或必需选项错误 |
| 3 | 输入校验／转换失败，包括损坏或未来版本状态文件 |
| 4 | 文件 I/O／状态冲突；conflict 表示占用或检测到外部修改 |
| 5 | 批处理／恢复部分失败 |
| 130 | 已取消 |

Ctrl+C 取消任务。为避免半个文件，写入通常通过同目录临时文件替换；项目＋侧文件等多文件操作不是事务，失败时保留现有项目与侧文件并检查报告。

## 13. 设置、隔离与字体库

默认 CLI 数据目录为 `%LOCALAPPDATA%\AsciiStudio\Cli`，不读取或改写桌面标签恢复文件。

```powershell
& $cli settings show --json
& $cli settings set --set Theme=Light --set PreviewZoom=1.5 --set BeginnerMode=true
& $cli settings export --output preferences.json
& $cli settings import --input preferences.json
& $cli settings reset
```

主题、动画、窗口和新手模式等是保存的偏好，**不会给终端创建图形界面**。CLI 显示／导出参数仍以各命令为准，例如位图字号使用 `--font-size`，不是把 `PreviewFontSize` 当作导出字号。设置会按桌面相同规则归一化；导出不含最近项目路径，重置／导入保留已有最近路径。

明确操作桌面偏好：

```powershell
& $cli settings show --desktop-data
& $cli settings set --desktop-data --set Theme=System
```

CLI 工作区清单仍独立保存，不会变成桌面标签。`--desktop-data` 与 `--data-directory` 互斥。

便携脚本或实验使用独立目录；每条命令保持相同参数：

```powershell
& $cli workspace open --data-directory '.\cli-data' --font-directory '.\cli-fonts' --project hello.asciiproj
& $cli edit show --data-directory '.\cli-data' --font-directory '.\cli-fonts'
```

字体库默认与桌面共享 `%LOCALAPPDATA%\AsciiStudio\fonts`；`fonts import/favorite` 会修改所选库。`--font-directory` 管理 FIGlet 导入和收藏，不会安装或卸载 Windows 系统字体。

## 14. 常见错误与资源限制

| 现象 | 处理 |
|---|---|
| EXE 没反应／找不到 DLL | 在终端运行 CLI 查看 stderr，重新完整解压对应架构 ZIP |
| 找不到字体 | `fonts list` 或 `fonts list --system` 查询实际名称；FIGlet 与系统字体分开选择 |
| 中文字太小 | raster 增加 `Columns`，图片导出增加 `--font-size`／`--scale` |
| 图像拉伸 | 用 `Rows=0` 的默认等比例模式；不要随意给固定行数；确认所选显示字体 |
| 输出已存在 | 改文件名，或明确增加 `--overwrite` |
| `No active project` | `workspace open --project ...`，或每条命令显式 `--project` |
| 选区越界或截断宽字符 | 按显示列计算，中文／emoji 通常占两列；短行不自动补齐 |
| 原项目被外部修改 | `project recover --output NEW_PATH` 保存旧编辑，或明确 `reload --discard-edits` |
| 状态被其他进程占用 | 等该命令完成后重试；空 `.lock` 文件本身不是占用信号 |
| 原项目已删除 | 保留侧文件，用 `project recover` 另存快照；不要先删除历史文件 |
| 缺字 | 换来源系统字体；明确接受时用 `--allow-missing` |
| 摘要无法还原 | 正常：哈希／校验／NFC／NFKC 不可逆；需要还原选择可逆方法 |
| JSON 导出不能当项目打开 | 使用转换的 `--save-project` 或 `project save`；作品 JSON 与项目格式不同 |

资源边界：图片输入 40 MB／8000 万像素，文本输入 8 MB，ANSI 4 MB；算法自身的更低限制仍生效，例如加密编码通常输入 1 MB、还原输入 4 MB。作品网格最多 400 万列位；图像导出最多 4000 万像素且每边不超过 32767，HTML／SVG 标记最多 1600 万字符。侧文件 100 MB、历史 100 步／64 MB、工作区 32 项。

当前没有独立 CLI 剪贴板、原图对比／视图控制、几何历史、候选结果管理、代码变量包装等入口，也没有终端交互编辑器。动画／摄像头／3D 继续冻结。用 `capabilities --json` 查询实际支持能力，不把计划中的命令当成已实现功能。
