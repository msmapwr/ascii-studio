# 展示素材

Before / After 为真实转换结果，不是人工绘制的字符效果。源图为本仓库工具自绘山景及 Windows 系统字形。

```powershell
dotnet run --project tools/AsciiStudio.Showcase/AsciiStudio.Showcase.csproj -c Release
```

工具调用应用的 ImageConverter、ImagingService 和 ExportService，输出山景半块 ANSI、系统字形密度 ASCII、原图与导出 PNG、TXT / ANSI。
并排图仅增加标题和排版。样例采用固定 100 列、0.5 字符宽高比；应用可使用实测字体补偿。

app.png 来自 WinApp UI 对真实应用窗口的截图；text-workflow.gif 由隔离测试会话内依次输入、生成的真实窗口截图组成，显示文字转换流程。
重录操作：启动隔离数据目录的应用，运行 `scripts/capture-showcase.ps1 -AppPid <应用PID>`，
再用安装了 Pillow 的 Python 运行 `tools/AsciiStudio.Showcase/make-gif.py`。
不要混入个人项目、文件路径或未发布功能。

系统字体渲染依赖 Windows 已安装字体，像素边缘可能随系统版本变化；转换用的文本／ANSI 也一并保留供核对。
