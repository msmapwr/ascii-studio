# 界面语言、设置搜索、收藏与偏好迁移

“界面语言”支持跟随系统、中文和 English，立即更新已有页面的导航、标签、选项、帮助、状态与确认按钮，无需重启或重建编辑器。跟随系统时，中文系统使用中文，其他系统使用英文。作品、输入、字体名称、文件名、项目标题与转换参数保持原样。操作系统文件选择器与尚未迁移的共享服务领域错误可能保留原语言。

语言以稳定设置键 `UiLanguage` 保存，取值 `system|zh-CN|en-US`；旧偏好缺少此字段时使用 `system`。导入、导出、重置和设置收藏均支持该键。CLI 的 `--language` 独立决定 CLI 帮助和设置目录输出语言。

设置页顶部可按中文名称、英文名称、分类或设置键搜索，例如 `主题`、`font appearance`、`DefaultColumns`。多词必须全部匹配，最多 256 字符；搜索只过滤显示，不改设置值。无结果时清空搜索或取消“仅显示收藏”即可恢复。

每项右侧的星标将设置加入或移出收藏。“仅显示收藏”可与搜索同时使用。收藏以稳定设置键保存到本机 `settings.json` 的 `FavoriteSettings`；旧文件缺少该字段时默认无收藏，重复／大小写不同的键合并，未知键忽略。搜索和过滤条件仅保留在当前页面，收藏会跨启动保留。

设置页也可调整默认预览缩放（0.25–4）；预览缩放与导出倍率独立，作品字体与界面字体继续分开。

“导出偏好…”保存 JSON，包括设置收藏，排除最近项目路径。“导入偏好…”读取最多 1 MB 的 JSON，替换界面、转换、导出和收藏偏好，并保留本机最近项目记录。未知字段、错误类型、损坏 JSON 和超限文件均在写入前拒绝；现有偏好保留。导出禁止覆盖正在使用的 `settings.json`。重置清空收藏、恢复偏好默认值，保留最近记录和项目文件。

CLI 与桌面共用目录和归一化规则，但 CLI 默认数据目录仍独立；`--desktop-data` 才明确操作桌面偏好。正在运行的桌面程序不会监视 CLI 写入，重新启动后读取更新。

```powershell
charloom-cli settings set --set UiLanguage=en-US --desktop-data
charloom-cli settings list --search "font appearance" --language en-US
charloom-cli settings list --search 主题 --language zh-CN
charloom-cli settings favorite --key Theme --enabled true
charloom-cli settings list --favorites
charloom-cli settings favorite --key Theme --enabled false
charloom-cli settings export --output preferences.json
charloom-cli settings import --input preferences.json
```

`settings favorite` 省略 `--enabled` 时切换状态；显式 true／false 可重复执行且结果不变。`settings list` 返回稳定键、当前语言的名称和分类、收藏状态及当前值，不返回最近路径。`--language` 决定目录输出名称，检索始终支持两种语言。

本模块已完成桌面界面语言切换、双语设置检索与设置收藏。工具栏收藏、个人配方、平台助手和共享服务／CLI 领域错误资源迁移仍在路线图中。
