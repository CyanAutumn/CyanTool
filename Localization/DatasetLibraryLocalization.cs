using System.Collections.Generic;

namespace CyanTools.Localization;

internal static class DatasetLibraryLocalization
{
    public static void AddTo(Dictionary<string, LocalizationManager.LocalizedText> translations)
    {
        translations["生成索引失败"] = new("Index Generation Failed", "インデックス生成に失敗");
        translations["查看"] = new("View", "表示");
        translations["还原"] = new("Restore", "復元");
        translations["远景图检测"] = new("Distant Shot Detection", "遠景画像検出");
        translations["远景检测设置"] = new("Distant Shot Settings", "遠景検出設定");
        translations["素材目录"] = new("Material Folder", "素材フォルダー");
        translations["选择素材目录（包含原图文件夹）"] = new("Select Material Folder (containing 原图)", "素材フォルダーを選択（原图 を含む）");
        translations["结果目录"] = new("Result Folder", "結果フォルダー");
        translations["半身面积阈值（%）"] = new("Half-body Area Threshold (%)", "半身面積のしきい値（%）");
        translations["执行与查看"] = new("Run and View", "実行と表示");
        translations["复用图片三分法的半身模型。任一半身框占整图面积超过阈值即为近景，其余为远景；未检出半身也归为远景。仅生成索引，不裁切或复制图片。"] = new("Uses the Three-Stage half-body model. Any half-body box above the image area threshold means close-up; all others, including no detections, are distant shots. Generates indexes only, without cropping or copying images.", "三段階処理の半身モデルを使用。半身枠の面積が画像全体に対するしきい値を超える場合は近景、それ以外や未検出は遠景です。インデックスのみ生成し、画像の切り抜きやコピーは行いません。");
        translations["查看会先还原上一分类，再移动图片并打开结果目录；还原会按记录移回原路径。"] = new("View restores the previous classification, moves indexed images, then opens the result folder. Restore moves them back to their recorded original paths.", "表示時は前の分類を復元し、画像を移動してから結果フォルダーを開きます。復元時は記録された元のパスに戻します。");
        translations["按半身检测面积生成近景与远景索引，查看时移动，支持还原。"] = new("Index close-up and distant shots by half-body area; move on view and restore originals.", "半身面積で近景・遠景を索引化。表示時に移動し、元に戻せます。");
        translations["生成索引"] = new("Generate Index", "インデックスを生成");
        translations["排除指定高度以下的图片"] = new("Exclude images below the minimum height", "最小高さ未満の画像を除外");
        translations["排除指定宽度以下的图片"] = new("Exclude images below the minimum width", "最小幅未満の画像を除外");
        translations["排除指定分辨率以下的图片"] = new("Exclude images below the minimum resolution", "最小解像度未満の画像を除外");
        translations["还原原图"] = new("Restore Originals", "原画像を復元");
        translations["返回素材库"] = new("Return to Library", "素材ライブラリに戻る");
        translations["该功能正在处理任务，请等待完成后再从素材库进入。"] = new("This tool is busy. Wait for it to finish before opening it from the material library.", "この機能は処理中です。完了後に素材ライブラリから開いてください。");
        translations["打开素材目录"] = new("Open Material Folder", "素材フォルダーを開く");
        translations["素材目录不存在"] = new("Material folder does not exist", "素材フォルダーが存在しません");
        translations["选择素材子文件夹"] = new("Select Material Subfolder", "素材サブフォルダーを選択");
        translations["返回"] = new("Return", "戻る");
        translations["仅删除本次成功转换的源图片；保留目标格式图片、标签、非图片附件和子文件夹。"] = new("Remove only successfully converted source images; keep target-format images, tags, attachments and subfolders.", "変換に成功した元画像のみ削除します。出力形式の画像、タグ、添付ファイル、サブフォルダーは保持します。");
        translations["输出格式"] = new("Output Format", "出力形式");
        translations["检查"] = new("Check", "確認");
        translations["可用占位符: {time} {id} {name}；通过正则手动获取按钮打开浏览器采集"] = new("Placeholders: {time} {id} {name}; use manual collection to open the browser.", "プレースホルダー: {time} {id} {name}。手動取得ボタンでブラウザーを開きます。");
        translations["素材库"] = new("Material Library", "素材ライブラリ");
        translations["素材库管理"] = new("Library Management", "ライブラリ管理");
        translations["管理目录"] = new("Library Folder", "管理フォルダー");
        translations["刷新素材库"] = new("Refresh Library", "ライブラリを更新");
        translations["保存选中素材参数"] = new("Save Selected Material Settings", "選択素材の設定を保存");
        translations["添加到素材库"] = new("Add to Library", "ライブラリに追加");
        translations["Danbooru 手动下载器"] = new("Danbooru Manual Downloader", "Danbooru 手動ダウンローダー");
        translations["不下载本地已有图片（检查同名不同后缀）"] = new("Skip existing images (including other extensions)", "既存画像をスキップ（同名の別形式も含む）");
        translations["素材文件夹名（支持 {name}、{artist}、{id}、{time}；输出到素材目录下的原图）"] = new("Material folder template: {name}, {artist}, {id}, {time}. Downloads go into 原图.", "素材フォルダー名：{name}、{artist}、{id}、{time}。原图 フォルダーに保存します。");
        translations["管理素材库，保存每个素材的下载参数，并检查补全原图。"] = new("Manage materials, save their download settings, and check for missing originals.", "素材ごとにダウンロード設定を保存し、不足している原画像を取得します。");
        translations["通过浏览器正则采集 Danbooru 帖子，并按标签设置下载。"] = new("Collect Danbooru posts with browser regular expressions and download with tag settings.", "ブラウザーの正規表現で Danbooru 投稿を収集し、タグ設定に従ってダウンロードします。");
    }
}
