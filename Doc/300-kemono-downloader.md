# Kemono下载器

`Kemono下载器`用于从 `kemono.cr` 的 API 拉取某个画师（用户）的 posts，并下载其中的图片资源（含 post 主文件与 attachments）。

该页面默认隐藏，需要在主页解锁后才会出现在菜单中。

## 解锁方式

在主页对隐藏图片连续点击 5 次后解锁下载器菜单（仅当前进程有效，重启后会恢复未解锁）。

## 快速上手

1. 填写“画师 ID”（Kemono URL 中的 user id）。
2. 选择“服务”（如 `fanbox/patreon/fantia/...`）。
3. 设置“输出目录”（默认 `output/kemono`）。
4. 点击“开始下载”，再次点击可停止。
5. 需要时可点击“打开目录”快速打开输出目录。

## 下载逻辑说明

- 程序会按页拉取作品列表（每页 50 条），直到返回空结果为止。
- 下载 post 主文件与 attachments 的所有文件类型。
- 每个资源下载间隔固定 5 秒。
- 下载失败会重试 3 次。

## 文件命名

下载保存时会统一重命名为：

`{artistId}_{yyyy_MM_dd}_{page}_{type}_{assetIndex}{ext}`

- `type` 为 `file` 或 `attachment`
- 日期从 post 的 `published` 字段解析
- 会额外保存 post 的 `content` 到同名的 `.txt` 文件中；如果列表缺失 `content`，会再请求单条 post 详情补全

## 注意事项

- 该工具会为每个下载文件保存一份对应的 `content` 文本。
- 大量下载可能触发站点限流，请合理使用。
