# 第三方依赖

| 依赖 | 固定版本 | 用途 | 来源 |
|---|---|---|---|
| Microsoft.WindowsAppSDK | 2.5.1 | WinUI 3、窗口与系统集成 | https://github.com/microsoft/WindowsAppSDK |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.9169 | Windows 构建工具 | https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/ |
| System.Drawing.Common | 10.0.0 | Windows 图片解码与字体栅格化 | https://github.com/dotnet/runtime |
| Figgle | 0.6.6 | FIGlet 解析与排版 | https://github.com/drewnoakes/figgle |
| Figgle.Fonts | 0.6.6 | 内置 FIGlet 字体 | https://github.com/drewnoakes/figgle/tree/master/src/Figgle.Fonts |

具体依赖及间接依赖版本见各项目 `packages.lock.json`。发行前需收集完整依赖许可证和各字体原始说明，审核字体资产分发条件；当前不是经过发行审核的安装包。不从 asciiart.eu 批量复制或分发图库作品。
