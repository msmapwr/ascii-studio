# Unicode 网格

alpha.4 使用 .NET 的 StringInfo 枚举完整字符簇，宽度表来自 Unicode 17.0.0 的 EastAsianWidth（W/F）与 Emoji_Presentation。表在开发时生成并随程序交付，运行时离线，无网络依赖。

- 英文、线框和歧义符号一列；中文、全角字符、默认 emoji 和 emoji 呈现序列两列。
- 组合标记、ZWJ、肤色修饰和旗帜保留在字符簇内，不拆开绘制或插入颜色转义。
- 四列 Tab 制表位根据当前显示列计算。ANSI 文件保留原有八列终端制表位。
- 控制字符与独立格式字符不占显示列；不做文本规范化，不改写组合字符原文。
- 编辑器与 HTML 使用中文/emoji 字体回退；图像、HTML、SVG 根据列位定位，实际字形由系统可用字体决定。GDI+ 的 emoji 外观可能不同于浏览器的彩色 emoji。
- TXT/ANSI 的显示还取决于终端字体、Unicode 版本和歧义宽度策略。RTL 文本的视觉排序由字体排版引擎处理，本网格按逻辑顺序计列；不保证各终端的双向文本表现一致。

项目格式 v3 保存 GridVersion=1 的列位网格。缺少 GridVersion 的旧文档按 UTF-16 网格读取，迁移时以字符簇首个 UTF-16 单元颜色填充占用列，前景/背景分别保留；旧版本项目保存前仍生成最近一个 `.bak`。单文档保持 400 万列位及 800 万 UTF-16 单元上限。

图片转换的密度字符集须为单列字符，避免逐像素采样与双列文字混用导致横向变形。中文与 emoji 可用于文字、边框和 ANSI 创作。

维护脚本：`scripts/update-unicode-widths.ps1`。数据许可位于 `licenses/Unicode.txt`。

来源：

- https://www.unicode.org/Public/17.0.0/ucd/EastAsianWidth.txt
- https://www.unicode.org/Public/17.0.0/ucd/emoji/emoji-data.txt
- https://learn.microsoft.com/dotnet/api/system.globalization.stringinfo.gettextelementenumerator

版本约束：用户未明确允许之前不得发布正式版 1.0.0；继续使用 0.9.0-alpha.N。
