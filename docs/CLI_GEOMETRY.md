# CLI 图片几何历史（Unreleased）

源码在 `1.0.0-alpha.6` 基础上新增以下入口，已公开的 alpha.5 包不包含这些命令。先用对应版本的 `geometry --help` 核对支持情况。

```powershell
& $cli geometry status --project photo.asciiproj
& $cli geometry set --project photo.asciiproj --set geometry.Left=10 --set geometry.Width=80
& $cli geometry set --project photo.asciiproj --set geometry.QuarterTurns=1 --set geometry.FlipHorizontal=true
& $cli geometry undo --project photo.asciiproj
& $cli geometry redo --project photo.asciiproj
& $cli geometry reset --project photo.asciiproj
```

`$cli` 为本机 CLI 路径。省略 `--project` 使用工作区 active；也可指定独立 `--workspace`。仅含原图来源的 image 项目支持几何命令。

裁剪坐标是旋转前原图的百分比；`Left/Top` 非负，`Width/Height` 大于 0，区域必须在 100% 内。`QuarterTurns` 为顺时针 0/1/2/3 个直角；翻转发生在旋转之后。禁止非有限数值、未知属性及非法范围。

`geometry set --geometry crop.json` 将严格 JSON 中的已指定字段叠加到当前草稿；重复的 `--set geometry.Name=Value` 优先于 JSON。未指定字段保留当前值。`--set` 必须带 `geometry.` 前缀。

几何草稿具有独立的 40 步历史（最多 41 个状态），不保存额外图像缓冲。相同参数不增加历史；撤销后修改清除几何 redo 分支。`reset` 恢复完整原图、无旋转翻转，也可以撤销。`status` 返回 `current` 草稿、`applied` 当前作品参数、`pending`、历史索引及可撤销/重做状态。

修改草稿不转换或覆盖当前作品，也不改变文本编辑历史、选区或已有候选。生成和接受新作品使用已有候选流程：

```powershell
& $cli candidate create --project photo.asciiproj
& $cli candidate show --project photo.asciiproj --format PNG --output rotated.png
& $cli candidate accept --project photo.asciiproj
& $cli project save --project photo.asciiproj --overwrite
```

已有候选需明确 `--replace-candidate` 才能替换。候选记录生成时的几何参数；生成后再改变草稿，不会改变该候选的来源参数。候选另存与接受使用记录的参数；结果 `history undo/redo` 同时恢复作品及已应用几何参数，几何草稿仍由 `geometry undo/redo` 单独管理。

`project save` 保存当前已接受作品及其对应参数，不自动接受候选或应用草稿。未应用草稿受工作区关闭保护：默认关闭报错，`--action keep` 保留恢复，`--action save --overwrite` 保存当前作品并保留未应用草稿的恢复记录。`project reload --discard-edits` 明确丢弃全部侧文件历史、草稿与候选。

图片侧文件使用 schema 3，兼容读取旧 schema 1/2 的作品、候选与清洁状态；首次写入后须使用支持 schema 3 的 CLI。`.asciiproj` 格式没有变更，GUI 仍不读取 CLI 侧文件。侧文件 100 MB、结果历史 100 步/64 MB 的既有预算保持适用。源项目外部变更继续返回 conflict；原文件丢失后的 `project recover` 只恢复独立作品与结果历史，丢弃已失效的原图几何绑定和草稿。
