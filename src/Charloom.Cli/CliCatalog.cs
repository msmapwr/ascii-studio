using System.Globalization;
using System.Text;
using Charloom.Core;
using Charloom.Services;

namespace Charloom.Cli;

public sealed record CliOption(string Name, string Value, string Chinese, string English, bool IsFlag = false);
public sealed record CliCommand(string Name, string Chinese, string English, CliOption[] Options, string Example, Type[] Models);

public static class CliCatalog
{
    private static CliOption O(string name, string value, string zh, string en, bool flag = false) => new(name, value, zh, en, flag);
    public static readonly CliOption[] GlobalOptions = [
        O("help", "", "完整帮助；支持每个命令及命令组，不分页", "Complete help for every command/group; no pager", true),
        O("version", "", "显示版本", "Print version", true),
        O("language", "LANG", "system（默认）| zh-CN | en-US；帮助与诊断语言", "system (default) | zh-CN | en-US; help/diagnostic language"),
        O("json", "", "结构化输出与错误；不混入进度", "Structured output/errors without progress text", true),
        O("quiet", "", "关闭普通诊断；错误与结果保留", "Suppress normal diagnostics, keep errors/results", true),
        O("data-directory", "PATH", "隔离数据目录，默认 %LOCALAPPDATA%/AsciiStudio/Cli", "Isolated data directory; default %LOCALAPPDATA%/AsciiStudio/Cli"),
        O("font-directory", "PATH", "字体目录，默认与桌面共享 %LOCALAPPDATA%/AsciiStudio/fonts", "Font directory; default shared %LOCALAPPDATA%/AsciiStudio/fonts"),
        O("desktop-data", "", "显式使用桌面设置目录；CLI工作区仍单独保存；不可与 data-directory 同用", "Explicitly use desktop settings directory; CLI workspace remains separate; conflicts with data-directory", true),
        O("workspace", "PATH", "CLI工作区清单，默认隔离数据目录下cli-workspace.json；目标项目省略时使用active", "CLI workspace manifest; default cli-workspace.json in isolated data directory; omitted project uses active")
    ];
    private static readonly CliOption Input = O("input", "PATH", "输入文件，严格 UTF-8 文本；图片/ANSI使用原始字节", "Input file; strict UTF-8 for text, raw bytes for image/ANSI");
    private static readonly CliOption Text = O("text", "TEXT", "直接输入文本；与 input/stdin/project 互斥", "Literal text; exclusive with input/stdin/project");
    private static readonly CliOption Stdin = O("stdin", "", "从标准输入读取文本，与其他来源互斥", "Read text from stdin, exclusive with other sources", true);
    private static readonly CliOption Project = O("project", "PATH", "读取项目保存的结果与来源", "Read saved project document/source");
    private static readonly CliOption Output = O("output", "PATH", "输出文件；省略时文本到stdout，位图必须指定文件", "Output file; omitted: text to stdout; bitmap requires a file");
    private static readonly CliOption Format = O("format", "NAME", "TXT（默认）/PNG/JPEG/GIF/HTML/SVG/ANSI/JSON/Markdown", "TXT (default)/PNG/JPEG/GIF/HTML/SVG/ANSI/JSON/Markdown");
    private static readonly CliOption Overwrite = O("overwrite", "", "允许覆盖目标文件；默认拒绝覆盖", "Allow replacing output files; default refuses replacement", true);
    private static readonly CliOption Set = O("set", "NAME=VALUE", "可重复；完整属性、默认值、枚举见下方。颜色用#AARRGGBB/#RRGGBB", "Repeatable; all properties/defaults/enums below. Colors: #AARRGGBB/#RRGGBB");
    private static readonly CliOption Options = O("options", "PATH", "严格 JSON 参数文件；优先级：set/命令参数＞文件＞项目＞默认", "Strict JSON options; precedence: set/flags > file > project > defaults");
    private static readonly CliOption SaveProject = O("save-project", "PATH", "保存来源与参数的可继续创作项目；不覆盖已有文件", "Save editable source/parameters project; existing file requires overwrite");
    private static readonly CliOption Font = O("font", "NAME", "结果显示字体，默认Consolas；系统字体必须安装", "Result display font, default Consolas; system font must be installed");
    private static readonly CliOption Size = O("font-size", "NUMBER", "位图导出字号1–120，默认14；不改变文字生成参数", "Bitmap export size 1–120, default 14; separate from generation");
    private static readonly CliOption Scale = O("scale", "INTEGER", "位图导出倍率1–4，默认1", "Bitmap export scale 1–4, default 1");
    private static readonly CliOption Transparent = O("transparent", "", "PNG透明画布；JPEG不支持透明", "Transparent PNG canvas; JPEG has no transparency", true);
    private static readonly CliOption[] TextInput = [Input, Text, Stdin, Project];
    private static readonly CliOption[] ExportOptions = [Output, Format, Overwrite, Size, Scale, Transparent];
    private static readonly CliOption[] ImageOptions = [Input, Project, Options, Set, Font, SaveProject,
        O("geometry", "PATH", "裁剪/旋转/翻转 JSON；也可 --set geometry.QuarterTurns=1", "Crop/rotation/flip JSON; or --set geometry.QuarterTurns=1"),
        O("columns", "INTEGER", "字符列数8–200000，默认120", "Columns 8–200000, default 120"),
        O("rows", "INTEGER", "0（默认）等比例，1–200000固定网格会拉伸", "0 (default) keeps aspect; 1–200000 fixed rows may stretch"),
        O("manual-aspect", "", "采用 CellAspect 参数；默认实测所选字体宽高比", "Use CellAspect; default measures selected font aspect", true),
        O("native-size", "", "按原图像素与字符实测尺寸推导网格，仍遵循资源上限", "Derive grid from source pixels and measured cells, within budgets", true), .. ExportOptions];
    private static readonly CliOption Selection = O("selection", "", "操作已保存选区，默认全文；选区按Unicode显示列", "Use saved selection, default whole document; Unicode display columns", true);
    private static readonly CliOption Apply = O("apply", "", "显式写入项目旁编辑状态，保留原文件与可撤销历史；必须使用project输入", "Explicitly apply to sidecar with undo; original file kept; requires project input", true);
    private static readonly CliOption Find = O("find", "TEXT", "按字面文本查找，不使用正则；最多1000个字符簇完整匹配", "Literal search, no regex; at most 1000 whole-grapheme matches");
    private static readonly CliOption IgnoreCase = O("ignore-case", "", "Ordinal忽略大小写，默认区分", "Ordinal ignore-case, default case-sensitive", true);
    private static readonly CliOption Row = O("row", "INTEGER", "起点行，0开始，默认0", "Zero-based start row, default 0");
    private static readonly CliOption Column = O("column", "INTEGER", "起点显示列，0开始，默认0；不能截断宽字符", "Zero-based display column, default 0; cannot split wide glyphs");
    private static readonly CliOption[] ReplacementInput = [Text, Input, Stdin];
    public static readonly CliCommand[] Commands = [
        new("image", "图片转换：桌面全部质量与几何参数", "Image conversion: all desktop quality/geometry parameters", ImageOptions,
            "image --input photo.png --columns 120 --set Style=Braille --output art.txt", [typeof(ConversionOptions), typeof(ImageGeometry)]),
        new("text", "FIGlet／中文系统字体转换与排版", "FIGlet/system-font text conversion and layout", [.. TextInput, Options, Set, Font, SaveProject,
            O("mode", "NAME", "figlet（默认）| raster", "figlet (default) | raster"), O("figlet-font", "ID", "FIGlet名称或ID，默认Standard；fonts list查询", "FIGlet name/id, default Standard; see fonts list"),
            O("system-font", "NAME", "中文栅格字体，默认Microsoft YaHei UI", "Raster source font, default Microsoft YaHei UI"), O("layout", "PATH", "TextArtOptions JSON；也可 --set layout.Wrap=true", "TextArtOptions JSON; or --set layout.Wrap=true"),
            O("allow-missing", "", "允许系统字体缺字并写入stderr警告；默认报错", "Allow missing system glyphs with stderr warning; default fails", true), .. ExportOptions],
            "text --text \"你好\" --mode raster --set Columns=120 --output art.png --format PNG", [typeof(TextRasterOptions), typeof(TextArtOptions)]),
        new("ansi", "ANSI解析、编码、SAUCE与颜色", "ANSI parsing, encoding, SAUCE and colors", [Input, Text, Stdin, Font, SaveProject,
            O("encoding", "NAME", "Auto（默认）| UTF-8 | CP437", "Auto (default) | UTF-8 | CP437"), O("columns", "INTEGER", "20–300列，默认80；未指定时使用有效SAUCE宽度", "Columns 20–300, default 80; valid SAUCE width if omitted"),
            O("ice", "", "启用iCE高亮背景；默认采用SAUCE标记", "Enable iCE bright backgrounds; default uses SAUCE flag", true), .. ExportOptions],
            "ansi --input art.ans --format HTML --output art.html", []),
        new("generate", "全部7种边框、分隔线与图案生成器", "All 7 border/divider/pattern generators", [.. TextInput, Set, Options, Font, SaveProject, .. ExportOptions],
            "generate --set Kind=2 --set Width=40 --set Height=20 --output maze.txt", [typeof(GeneratorRecipe)]),
        new("export", "导出项目或文本；支持全部9种桌面格式", "Export projects/text in all 9 desktop formats", [.. TextInput, Font, .. ExportOptions],
            "export --project work.asciiproj --format PNG --output work.png", []),
        new("candidate status", "查询候选与当前编辑状态；不会创建或改变结果", "Inspect candidate/current edit status without changing results", [Project], "candidate status --project work.asciiproj", []),
        new("candidate create", "按项目来源与当前几何草稿重新生成候选，保留手工结果；每项目一个", "Regenerate from source and draft geometry while preserving edits; one per project", [Project,
            O("replace-candidate", "", "明确替换已有候选，默认报冲突；不改变编辑历史", "Explicitly replace existing candidate; default conflicts; edit history stays intact", true)], "candidate create --project work.asciiproj", []),
        new("candidate show", "预览／导出候选，支持9种格式；不替换当前编辑", "Preview/export candidate in 9 formats without replacing edits", [Project, .. ExportOptions], "candidate show --project work.asciiproj --format PNG --output candidate.png", []),
        new("candidate accept", "明确接受候选并加入撤销历史，清空选区；原文件仍需project save", "Accept candidate into undo history, clear selection; project save still required", [Project], "candidate accept --project work.asciiproj", []),
        new("candidate save", "候选另存项目，保留来源与参数；不改变active、手工结果或候选", "Save independent candidate project with source/parameters; active, edits and candidate unchanged", [Project, Output, Overwrite], "candidate save --project work.asciiproj --output candidate.asciiproj", []),
        new("candidate discard", "只丢弃候选，保留手工结果与撤销历史", "Discard only candidate, retaining current edits and undo history", [Project], "candidate discard --project work.asciiproj", []),
        new("geometry status", "查询图片几何草稿、已应用参数和40步独立历史；不改作品", "Inspect image draft/applied geometry and independent 40-step history; artwork unchanged", [Project], "geometry status --project photo.asciiproj", []),
        new("geometry set", "修改裁剪／旋转／翻转草稿；JSON叠加当前值，set优先；生成使用candidate create", "Update crop/rotation/flip draft; JSON merges current values, set wins; generate with candidate create", [Project, Set,
            O("geometry", "PATH", "严格几何JSON，未指定字段保留；set须使用geometry.前缀", "Strict geometry JSON; omitted fields kept; set requires geometry. prefix")],
            "geometry set --project photo.asciiproj --set geometry.QuarterTurns=1", [typeof(ImageGeometry)]),
        .. new[] { ("undo", "撤销几何草稿一步，不改当前作品或已有候选", "Undo draft geometry without changing artwork or existing candidate"),
            ("redo", "重做几何草稿一步，不改当前作品或已有候选", "Redo draft geometry without changing artwork or existing candidate"),
            ("reset", "重置为完整原图、无旋转翻转；可撤销", "Reset to full source with no rotation/flips; undoable") }
            .Select(t => new CliCommand("geometry " + t.Item1, t.Item2, t.Item3, [Project], "geometry " + t.Item1 + " --project photo.asciiproj", [])),
        new("edit show", "显示当前编辑结果或选区；可导出9种格式", "Show current edited result/selection in any of 9 export formats", [Project, Selection, .. ExportOptions], "edit show --project work.asciiproj", []),
        new("edit select", "持久Unicode选区；行从0开始，末行包含、末列不包含；矩形按每行显示列", "Persist Unicode selection; zero-based, end row included/end column excluded; rectangle uses each row's display columns", [Project, Row, Column,
            O("end-row", "INTEGER", "默认起点行，跨行包含末行", "Default start row, includes end row"), O("end-column", "INTEGER", "默认起点列，不包含终点列", "Default start column, exclusive end column"), O("rectangle", "", "矩形选区，默认跨行连续范围", "Rectangle, default continuous multi-line range", true)], "edit select --project work.asciiproj --row 0 --column 0 --end-row 1 --end-column 3 --rectangle", []),
        new("edit find", "返回完整字符簇匹配的行列；不改结果", "Return whole-grapheme match positions without editing", [Project, Find, IgnoreCase], "edit find --project work.asciiproj --find abc", []),
        new("edit replace", "替换全文／选区，或查找后替换；持久撤销，原项目暂不改写", "Replace document/selection or literal matches; persistent undo, source project unchanged", [Project, .. ReplacementInput, Selection, Find, IgnoreCase,
            O("all", "", "替换所有匹配；默认首个；与find配合", "Replace all matches, default first; use with find", true)], "edit replace --project work.asciiproj --find abc --text xyz --all", []),
        new("edit insert", "在指定显示列插入；允许换行，不截断字符簇", "Insert at a display column; allows newlines, preserves graphemes", [Project, .. ReplacementInput, Row, Column], "edit insert --project work.asciiproj --row 0 --column 0 --text Hello", []),
        new("edit delete", "删除全文或已保存选区，支持撤销", "Delete whole document or saved selection with undo", [Project, Selection], "edit delete --project work.asciiproj --selection", []),
        new("edit transform", "对全文／选区应用现有文本变换", "Transform whole document or selection", [Project, Selection,
            O("operation", "NAME", "upper/lower/mirror/flip/trim/clean/ascii/expand-tabs（必须）", "Required: upper/lower/mirror/flip/trim/clean/ascii/expand-tabs")], "edit transform --project work.asciiproj --operation mirror", []),
        .. new[] { ("list", "查询历史、脏状态与预算", "Inspect history, dirty state and budget"), ("undo", "持久撤销一步", "Persistently undo one step"), ("redo", "持久重做一步", "Persistently redo one step"), ("clear", "只保留当前状态，清除历史；不丢弃当前编辑", "Retain current state only; clear history without discarding current edits") }
            .Select(t => new CliCommand("history " + t.Item1, t.Item2, t.Item3, [Project], "history " + t.Item1 + " --project work.asciiproj", [])),
        new("project save", "保存当前编辑；默认原路径，需overwrite；另存不改变活动工作区路径", "Save edits; default original path requires overwrite; save-copy leaves active workspace path unchanged", [Project, Output, Overwrite], "project save --project work.asciiproj --overwrite", []),
        new("project reload", "重新读取磁盘项目，显式丢弃侧文件编辑与撤销；保留原项目", "Reload disk project, explicitly discard sidecar edits/history; source project kept", [Project,
            O("discard-edits", "", "侧文件存在时必须明确指定，即使存在外部修改冲突", "Required when a sidecar exists, including external modification conflicts", true)], "project reload --project work.asciiproj --discard-edits", []),
        new("project recover", "外部改写／删除后，从侧文件恢复作品与撤销到新快照项目；不绑定旧来源，不覆盖", "Recover artwork/history from sidecar after source replacement/deletion into a new snapshot; no old source linkage, no overwrite", [Project, Output], "project recover --project missing.asciiproj --output recovered.asciiproj", []),
        new("workspace open", "验证并打开项目；同一路径只开一次，切为active；最多32项", "Validate/open project once per path, set active; max32", [Project], "workspace open --project work.asciiproj", []),
        new("workspace new", "新建独立快照项目并打开；必须指定新路径，不覆盖", "Create/open an independent snapshot project at an explicit new path; no overwrite", [Project, Text], "workspace new --project draft.asciiproj --text abc", []),
        new("workspace switch", "切换已打开的active项目", "Switch active open project", [Project], "workspace switch --project work.asciiproj", []),
        new("workspace close", "关闭项目；未保存默认取消，可保存或保留恢复", "Close project; unsaved default cancels, choose save or retain recovery", [Project, Overwrite,
            O("action", "NAME", "cancel（默认）/keep/save；save改写原路径需overwrite", "cancel (default)/keep/save; save to original requires overwrite")], "workspace close --project work.asciiproj --action keep", []),
        .. new[] { ("list", "列出项目、active及未保存状态；启动时自动读取全部标签", "List projects, active and dirty state; all tabs read on each invocation"), ("recent", "查询最近15个项目", "List 15 recent projects"),
            ("clear-recent", "清空CLI最近项目列表，保留作品", "Clear CLI recent list, keep project files"), ("recovery", "查询保留恢复的项目", "List retained recoveries"), ("restore", "恢复已关闭的未保存项目，逐项报告，失败返回5", "Restore retained dirty projects, report each item, failures return5") }
            .Select(t => new CliCommand("workspace " + t.Item1, t.Item2, t.Item3, [], "workspace " + t.Item1, [])),
        new("project info", "项目与来源摘要，保留原文件", "Project/source summary without changes", [Project], "project info --project work.asciiproj --json", []),
        new("project validate", "验证旧格式、网格、来源和参数", "Validate versions, grid, source and options", [Project], "project validate --project work.asciiproj", []),
        new("project migrate", "保存当前项目格式；旧格式原路径保存会备份", "Save current project format; in-place old-format migration creates backup", [Project, Output, Overwrite], "project migrate --project old.asciiproj --output new.asciiproj", []),
        new("project regenerate", "按保存来源重新生成，默认保护手工结果", "Regenerate saved source, protecting manual edits by default", [Project, SaveProject, Overwrite,
            O("replace-edited", "", "明确允许替换已手工编辑的结果", "Explicitly replace a manually edited result", true), .. ExportOptions.Where(o => o.Name != "overwrite")], "project regenerate --project work.asciiproj --output regenerated.txt", []),
        .. new[] { ("analyze","分析与ASCII校验","Analyze and validate ASCII"), ("trim","裁剪外围空白","Trim canvas whitespace"), ("clean","去除控制字符","Remove control characters"), ("ascii","仅保留ASCII","Keep ASCII only"), ("upper","大写","Uppercase"), ("lower","小写","Lowercase"), ("mirror","逐行字符簇反转","Reverse graphemes per line"), ("flip","上下反转","Reverse line order"), ("expand-tabs","Tab转4个空格","Replace tabs with 4 spaces") }
            .Select(t => new CliCommand("tools " + t.Item1, t.Item2, t.Item3, [.. TextInput, Apply, .. ExportOptions], "tools " + t.Item1 + " --text \"Hello\"", [])),
        new("comment", "所有桌面注释语言；保留结束符冲突检查", "All desktop comment languages with delimiter collision checks", [.. TextInput, Apply, Output, Overwrite,
            O("syntax", "NAME", "C/C++/C#/Python/HTML等；用 --list 查询完整列表", "C/C++/C#/Python/HTML etc.; --list shows all"), O("block", "", "块注释，默认行注释", "Block comments; default line comments", true), O("list", "", "列出所有语言及注释符", "List all languages and comment delimiters", true)],
            "comment --text \"ASCII\" --syntax Python", []),
        new("crypto algorithms", "列出全部处理方法、可逆性与平台支持", "List all processing methods, reversibility and platform support", [], "crypto algorithms --json", []),
        new("crypto apply", "全部桌面加密、编码、压缩、摘要和传统密码", "All desktop crypto, encodings, compression, digests and classical ciphers", [.. TextInput, Output, Overwrite,
            O("algorithm", "NAME", "完整名称见 crypto algorithms；推荐AES-256-GCM；传统密码仅教学，摘要不可还原", "See crypto algorithms; prefer AES-256-GCM; classical ciphers are educational, digests irreversible"),
            O("reverse", "", "解密/还原；摘要与规范化不支持", "Decrypt/reverse; digests and normalization cannot reverse", true), O("password-file", "PATH", "UTF-8口令文件，末尾换行不计；最多4096字符，不记录口令", "UTF-8 secret file, trailing newlines stripped, max4096 chars; never logged"),
            O("password-stdin", "", "从stdin读口令，不可与 --stdin 同时使用", "Secret from stdin; conflicts with --stdin", true), O("key-file", "PATH", "RSA PEM公钥/私钥，最多16KB", "RSA public/private PEM, max16KB"),
            O("base64-bytes", "", "字符编码字节用Base64，默认Hex", "Character encoding bytes as Base64; default Hex", true), O("bom", "", "UTF编码加入BOM，默认不加", "Include UTF BOM, off by default", true)],
            "crypto apply --algorithm UTF-8 --text \"中\" --base64-bytes", []),
        new("crypto keys", "生成3072位RSA公私钥，不打印私钥", "Generate 3072-bit RSA public/private PEM without printing private key", [Output, Overwrite], "crypto keys --output ./keys", []),
        new("fonts list", "FIGlet字体查询／搜索／收藏；支持系统字体筛选", "FIGlet search/favorites and installed system-font filters", [O("system", "", "列出系统字体，默认FIGlet", "List installed fonts; default FIGlet", true), O("search", "TEXT", "名称搜索", "Filter names"), O("favorites", "", "仅收藏的FIGlet字体", "Favorite FIGlet fonts only", true), O("monospace", "", "筛选等宽系统字体", "Filter monospaced system fonts", true), O("chinese", "", "筛选常见中文系统字体", "Filter common Chinese system fonts", true)], "fonts list --system --monospace", []),
        new("fonts import", "验证并导入.flf字体", "Validate/import a .flf FIGlet font", [Input], "fonts import --input custom.flf", []),
        new("fonts favorite", "切换FIGlet字体收藏", "Toggle FIGlet favorite", [O("figlet-font", "ID", "字体ID或名称", "Font id/name")], "fonts favorite --figlet-font Standard", []),
        new("fonts sample", "abc／当前输入字体预览，支持系统字体测试", "abc/current-input font samples and system-font preview", [Text, O("system", "", "系统字形预览（默认样本测试）", "System-font sample (default: 测试)", true), O("figlet-font", "ID", "默认Standard", "Default Standard"), O("system-font", "NAME", "默认Microsoft YaHei UI", "Default Microsoft YaHei UI"), .. ExportOptions], "fonts sample --figlet-font Standard", []),
        new("settings show", "查询隔离CLI或显式共享的桌面设置", "Show isolated CLI or explicitly shared desktop settings", [], "settings show --json", []),
        new("settings list", "按中文／英文名称、分类或设置键搜索，可仅查询收藏", "Search bilingual names, groups or keys, optionally favorites only",
            [O("search", "TEXT", "最多256字符，多词同时匹配", "Up to256 characters; all words must match"), O("favorites", "", "只查询已收藏设置", "Only favorite settings", true)], "settings list --search font --language en-US", []),
        new("settings favorite", "按设置键切换收藏；enabled可显式设置状态", "Toggle a favorite by key; enabled explicitly sets its state",
            [O("key", "NAME", "settings list列出的设置键", "Setting key from settings list"), O("enabled", "BOOL", "显式true/false，省略时切换", "Explicit true/false; toggle when omitted")], "settings favorite --key Theme --enabled true", []),
        new("settings set", "修改全部现有桌面设置，执行相同归一化", "Update all current desktop settings with identical normalization", [Set], "settings set --set Theme=Light", [typeof(StudioSettings)]),
        new("settings reset", "恢复默认设置，保留最近项目和作品", "Restore default settings while keeping recent projects/art", [], "settings reset", []),
        new("settings export", "导出偏好，排除最近项目路径", "Export preferences without recent paths", [Output, Overwrite], "settings export --output preferences.json", []),
        new("settings import", "导入偏好，保留已有最近项目路径", "Import preferences, keep existing recent paths", [Input], "settings import --input preferences.json", []),
        new("batch image", "逐项有界图片批处理，失败继续且报告非零退出码", "Bounded sequential image batch; continue failures and return nonzero", [.. ImageOptions.Where(o => o.Name is not ("project" or "save-project")), O("recursive", "", "递归输入目录，默认仅当前层", "Recurse input directory, default current level only", true)], "batch image --input ./photos --output ./results --format PNG", [typeof(ConversionOptions), typeof(ImageGeometry)]),
        new("clipboard read", "显式读取剪贴板，文本原样输出；图片保存PNG，不改项目", "Explicit clipboard read: exact text or PNG file; project unchanged", [Output, Overwrite, O("format", "NAME", "TXT（默认）或PNG；PNG必须指定output；文本8MB、PNG100MB/2亿像素", "TXT (default) or PNG; PNG requires output; text8MB, PNG100MB/200 million pixels")], "clipboard read --format PNG --output pasted.png", []),
        new("clipboard write", "将文本或项目当前编辑结果复制到系统剪贴板", "Copy text or the current project edit to the Windows clipboard", [.. TextInput], "clipboard write --project work.asciiproj", []),
        new("clipboard paste", "显式apply才粘贴到项目编辑状态；可撤销，原文件不自动保存", "Paste only with explicit apply; undoable sidecar edit, no automatic project save", [Project, Apply, Selection], "clipboard paste --project work.asciiproj --apply", []),
        new("capabilities", "查询功能覆盖与本次预发布待办，不将待办伪装为支持", "Capability coverage and prerelease gaps; pending items are not advertised as supported", [], "capabilities --json", [])
    ];
    public static IEnumerable<CliOption> AllOptions => GlobalOptions.Concat(Commands.SelectMany(c => c.Options)).DistinctBy(o => o.Name);
    public static bool Chinese(CliArguments args) => args.Get("language", "system") switch { "zh-CN" => true, "en-US" => false, _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) };
    public static string Help(CliArguments args)
    {
        var zh = Chinese(args); var b = new StringBuilder("Charloom CLI 1.0.0-alpha.6\n\n");
        b.AppendLine(zh ? "字织 · 离线 ASCII / ANSI 字符艺术创作工具\n把字符织成画面\n" : "Offline ASCII / ANSI art studio\nWeave characters into art\n");
        var exact = Commands.FirstOrDefault(c => c.Name == args.Command);
        b.AppendLine(zh ? "用法：charloom-cli <命令> [选项]" : "Usage: charloom-cli <command> [options]");
        if (exact is null)
        {
            foreach (var c in Commands.Where(c => args.Command.Length == 0 || c.Name.StartsWith(args.Command + " ", StringComparison.Ordinal)))
                b.AppendLine($"  {c.Name,-24} {(zh ? c.Chinese : c.English)}");
        }
        else
        {
            b.AppendLine($"\n{exact.Name}: {(zh ? exact.Chinese : exact.English)}");
            b.AppendLine(zh ? "\n命令选项：" : "\nCommand options:");
            foreach (var o in exact.Options) b.AppendLine($"  --{o.Name} {o.Value}\n      {(zh ? o.Chinese : o.English)}");
            foreach (var type in exact.Models)
            {
                b.AppendLine($"\n--set {type.Name}:");
                object? defaults = type == typeof(TextRasterOptions) ? new TextRasterOptions("Microsoft YaHei UI")
                    : type == typeof(ImageGeometry) ? new ImageGeometry() : type == typeof(GeneratorRecipe) ? new GeneratorRecipe()
                    : type == typeof(StudioSettings) ? new StudioSettings() : Activator.CreateInstance(type);
                var prefix = type == typeof(ImageGeometry) ? "geometry." : type == typeof(TextArtOptions) ? "layout." : "";
                foreach (var p in type.GetProperties().Where(p => p.SetMethod is not null))
                {
                    var value = Convert.ToString(p.GetValue(defaults), CultureInfo.InvariantCulture) ?? "";
                    b.AppendLine($"  {prefix}{p.Name} ({p.PropertyType.Name}) = {value}" + (p.PropertyType.IsEnum ? " [" + string.Join('|', Enum.GetNames(p.PropertyType)) + "]" : ""));
                    b.AppendLine("      " + PropertyHelp(type, p.Name, zh));
                }
            }
            b.AppendLine("\n" + (zh ? "示例：" : "Example:") + "\n  charloom-cli " + exact.Example);
        }
        b.AppendLine(zh ? "\n全局选项：" : "\nGlobal options:");
        foreach (var o in GlobalOptions) b.AppendLine($"  --{o.Name} {o.Value}\n      {(zh ? o.Chinese : o.English)}");
        b.AppendLine(zh ? "\n退出码：0成功，2参数错误，3输入/转换失败，4文件IO或状态冲突，5批处理/恢复部分失败，130已取消。\n文本stdout不附加换行；错误写stderr。默认拒绝覆盖；Ctrl+C取消任务。\n输入上限：图片100MB/2亿源像素（处理图12000边/1600万像素；GIF首帧）、ANSI4MB、文本8MB；算法自身更小的限制仍生效。\n位图上限1亿像素/单边32767，HTML/SVG标记16000万字符。\n编辑历史最多100步/64MB，侧文件256MB；工作区32项目、恢复32项、最近15项。\n预发布：完整桌面功能映射、未实现项请查 capabilities 和 docs/CLI.md。"
            : "\nExit codes: 0 success, 2 usage, 3 input/conversion, 4 file IO/state conflict, 5 partial batch/recovery failure, 130 canceled.\nText stdout adds no newline; errors go to stderr. No overwrite by default. Ctrl+C cancels.\nInput budgets: image100MB/200M source pixels (processing max12000 side/16M pixels; GIF first frame), ANSI4MB, text8MB; smaller algorithm limits still apply.\nBitmap100M pixels/max32767 per side; HTML/SVG160M markup chars.\nEdit history100 steps/64MB, sidecar256MB; workspace32 projects, recovery32, recent15.\nPrerelease: see capabilities and docs/CLI.md for full desktop parity tracking and gaps.");
        return b.ToString();
    }
    public static string PropertyHelp(Type type, string name, bool zh)
    {
        var description = (type.Name, name) switch
        {
            (nameof(ConversionOptions), "Columns") => "8–200000; character grid width / 字符网格列数",
            (nameof(ConversionOptions), "Rows") => "0–200000; 0 keeps image aspect / 0为等比例行数",
            (nameof(ConversionOptions), "CellAspect") => "(0,2]; measured font ratio unless --manual-aspect / 字符宽高比",
            (nameof(ConversionOptions), "Characters") => "2+ distinct single-column glyphs, <=200 UTF-16 units / 至少两个不同单列字符",
            (nameof(ConversionOptions), "Brightness") => "0–5; brightness multiplier / 亮度倍率",
            (nameof(ConversionOptions), "Contrast") => "0–5; contrast multiplier / 对比度倍率",
            (nameof(ConversionOptions), "Gamma") => "(0,5]; gamma correction / 伽马校正",
            (nameof(ConversionOptions), "Saturation") => "0–5; saturation multiplier / 饱和度倍率",
            (nameof(ConversionOptions), "Hue") => "Finite angle in degrees / 有限数值，色相角度",
            (nameof(ConversionOptions), "Grayscale") => "0–1; grayscale blend / 灰度混合",
            (nameof(ConversionOptions), "Sepia") => "0–1; sepia blend / 棕褐混合",
            (nameof(ConversionOptions), "Invert") => "true/false; invert density / 反转明暗",
            (nameof(ConversionOptions), "Color") => "true/false; per-cell foreground colors / 字符前景色",
            (nameof(ConversionOptions), "Threshold") => "true/false; binary threshold / 二值化",
            (nameof(ConversionOptions), "ThresholdValue") => "0–255; binary threshold cutoff / 二值化阈值",
            (nameof(ConversionOptions), "Edges") => "true/false; edge enhancement / 边缘增强",
            (nameof(ConversionOptions), "Sharpness") => "0–10; sharpen strength / 锐化强度",
            (nameof(ConversionOptions), "Dither") => "Error diffusion algorithm; listed enum values / 抖动算法",
            (nameof(ConversionOptions), "Background") => "ARGB unsigned integer or #RRGGBB/#AARRGGBB / 背景颜色",
            (nameof(ConversionOptions), "Style") => "Density/Structure/Braille/HalfBlock / 密度、结构、盲文、双色半块",
            (nameof(ConversionOptions), "MeasureGlyphDensity") => "true/false; measure font glyph coverage / 实测字形密度",
            (nameof(ConversionOptions), "AdaptiveStrength") => "0–1; local adaptive detail / 局部自适应细节",
            (nameof(ConversionOptions), "StructureThreshold") => "0–1; structure edge cutoff / 结构边缘阈值",
            (nameof(ConversionOptions), "PreserveTransparent") => "true/false; keep image alpha / 保留图片透明度",
            (nameof(ConversionOptions), "TrimTransparent") => "true/false; crop transparent margins / 裁掉透明边缘",
            (nameof(ConversionOptions), "AlphaThreshold") => "1–255; alpha crop cutoff / 透明裁剪阈值",
            (nameof(ConversionOptions), "PaletteMode") => "Palette enum; see listed values / 调色板模式",
            (nameof(ConversionOptions), "PaletteColors") => "Comma-separated #RRGGBB/#AARRGGBB palette / 逗号分隔颜色列表",
            (nameof(ConversionOptions), "PaletteSize") => "2–64; limited palette size / 限定调色板数量",
            (nameof(ConversionOptions), "QuickPreview") => "true/false; stored for GUI automatic preview, CLI emits full result / 保存为桌面自动预览偏好",
            (nameof(ImageGeometry), "Left" or "Top") => "0–100 percent, before rotation / 旋转前裁剪起点百分比",
            (nameof(ImageGeometry), "Width" or "Height") => "(0,100] percent; crop must stay inside source / 裁剪区域不得超出原图",
            (nameof(ImageGeometry), "QuarterTurns") => "0/1/2/3 clockwise quarter-turns / 顺时针0、90、180、270度",
            (nameof(ImageGeometry), "FlipHorizontal" or "FlipVertical") => "true/false; flip after rotation / 旋转后翻转",
            (nameof(TextRasterOptions), "Family") => "Installed system font name / 已安装系统字体名",
            (nameof(TextRasterOptions), "Columns") => "16–600; raster output columns / 字形栅格化列数",
            (nameof(TextRasterOptions), "Style") => "0 standard, 1 thin, 2 dots, 3 blocks, 4 high density / 标准、细线、点阵、方块、高密度",
            (nameof(TextRasterOptions), "Bold") => "true/false; source glyph weight / 来源字形粗细",
            (nameof(TextRasterOptions), "Stroke") => "0–12; outline width; hollow requires >0 / 描边宽度，空心需要大于0",
            (nameof(TextRasterOptions), "Filled") => "true solid / false hollow / 实心或空心",
            (nameof(TextArtOptions), "LetterSpacing" or "LineSpacing") => "0–20; extra character/line spacing / 字距或行距",
            (nameof(TextArtOptions), "MaximumWidth") => "0–2000; 0 unlimited, wrapping requires >0 / 最大宽度，换行需要大于0",
            (nameof(TextArtOptions), "Wrap") => "true/false; words then graphemes / 先按词，再按字符簇换行",
            (nameof(TextArtOptions), "Alignment") => "Left/Center/Right / 左、中、右对齐",
            (nameof(TextArtOptions), "Horizontal" or "Vertical") => "Default/Full/Kern/Smush / 默认、完整、挤紧、规则重叠",
            (nameof(TextArtOptions), "Border") => "0 none, 1 ASCII, 2 double, 3 stars / 无、ASCII、双线、星号",
            (nameof(TextArtOptions), "PaddingX" or "PaddingY") => "0–30; horizontal/vertical inner padding / 横向或纵向内边距",
            (nameof(TextArtOptions), "Title") => "<=100 chars, no controls / 边框标题，禁止控制字符",
            (nameof(TextArtOptions), "Trim") => "true/false; trim outer whitespace / 裁去外围空白",
            (nameof(TextArtOptions), "Replacement") => "Empty or one single-column BMP glyph / 空或一个单列BMP替换字符",
            (nameof(GeneratorRecipe), "Kind") => "0 border,1 divider,2 maze,3 stars,4 checker,5 stripes,6 gradient / 七种生成器",
            (nameof(GeneratorRecipe), "Style") => "0–3; border/divider style / 边框和分隔线样式",
            (nameof(GeneratorRecipe), "Text") => "<=2M chars; border <=1000 lines/2000 chars per line / 边框文本",
            (nameof(GeneratorRecipe), "Width") => "8–300; maze max100 / 宽度，迷宫最大100",
            (nameof(GeneratorRecipe), "Height") => "3–100; maze max80 / 高度，迷宫最大80",
            (nameof(GeneratorRecipe), "Seed") => "0–2147483647; deterministic maze/stars seed / 可复现随机种子",
            (nameof(StudioSettings), _) => SettingsHelp(name),
            _ => throw new InvalidOperationException("Missing help for " + type.Name + "." + name)
        };
        return description;
    }
    private static string SettingsHelp(string name) => name switch
    {
        "Theme" => "Dark/Light/System / 深色、浅色、跟随系统",
        "PreviewFontSize" => "8–30 preview size / 预览字号",
        "RecentFiles" => "Not settable via --set; retained during reset/import / 最近项目路径，无法通过--set修改",
        "Animations" => "true/false; additionally follows Windows accessibility / 遵循系统动画与辅助功能",
        "WordWrap" => "true/false; result editor wrapping / 编辑器换行",
        "ShowStats" => "true/false; result statistics / 结果统计",
        "CompactLayout" => "true/false; compact layout / 紧凑布局",
        "DefaultExportFormat" => "TXT/PNG/JPEG/GIF/HTML/SVG/ANSI/JSON/Markdown / 默认导出格式",
        "ExportScale" => "1–4; default bitmap scale / 默认位图倍率",
        "FilePrefix" => "Max32 safe filename characters / 文件名前缀，非法字符被过滤",
        "AutoConvert" => "true/false; desktop automatic conversion / 桌面自动转换",
        "ConversionDelay" => "0–1000ms; desktop debounce / 桌面自动转换合并延迟",
        "DefaultColumns" => "8–200000; new image project columns / 新项目默认列数",
        "FavoriteSettings" => "Use settings favorite or preferences import; --set rejects arrays / 收藏设置键数组，通过favorite或偏好导入修改",
        "RememberWindow" => "true/false; remember desktop window position / 记住桌面窗口位置",
        "PreviewZoom" => "0.25–4; preview only / 仅预览缩放",
        "BeginnerMode" => "true/false; detailed desktop guidance / 新手说明",
        "UiFontFamily" => "<=128 non-control characters / 界面字体名",
        "UiFontSize" => "12–24; desktop UI size / 界面字号",
        _ => throw new InvalidOperationException("Missing setting help: " + name)
    };
}
