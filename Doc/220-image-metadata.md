# 图片元数据

`图片元数据`用于查看与修改图片中的元数据（PNG tEXt / EXIF）。左侧支持拖拽图片加载，右侧以 JSON 形式展示并可编辑。

## 快速上手

1. 将图片文件拖到左侧区域（支持 `png/jpg/jpeg/bmp/gif`）。
2. 右侧会显示 JSON：
   - `novelAI`：PNG tEXt（仅 PNG 有效）
   - `exif`：EXIF 标签（若存在）
3. 修改 JSON 后点击 “Update Metadata” 写回文件。

## 写入规则

### JSON 可解析时

- `exif`：
  - 会重建 `ExifProfile`
  - 仅写入 `ImageDescription` 与 `UserComment` 两个字段（若存在）
- `novelAI`（仅 PNG）：
  - 清空现有 `TextData`
  - 将 `novelAI` 对象中的键值对写为 PNG tEXt

### JSON 无法解析时（纯文本模式）

当 JSON 语法错误时，会把右侧文本整体当作字符串写入：

- PNG：写入 tEXt，关键词为 `Prompt`
- 非 PNG：写入 EXIF `ImageDescription`

## 注意事项

- 元数据会直接写回原文件，请先备份再改写。
- 对非 PNG 图片，`novelAI` 不会生效（会显示“仅 PNG 支持…”提示）。

