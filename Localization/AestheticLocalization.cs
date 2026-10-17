using System.Collections.Generic;

namespace CyanTools.Localization;

internal static class AestheticLocalization
{
    public static void AddTo(Dictionary<string, LocalizationManager.LocalizedText> translations)
    {
        translations["美学评分"] = new("Aesthetic Scoring", "美的品質スコア");
        translations["评估动漫图片的美学质量，查看七档评分并按等级整理训练集。"] = new("Assess anime image aesthetics, review seven quality levels, and organize your dataset by grade.", "アニメ画像の美的品質を評価し、7 段階のスコアを確認して学習画像を分類します。");
        translations["美学评分模型"] = new("Aesthetic Model", "美的品質評価モデル");
        translations["下载 / 修复模型"] = new("Download / Repair Model", "モデルのダウンロード / 修復");
        translations["选择模型目录"] = new("Choose Model Folder", "モデルフォルダーを選択");
        translations["模型目录（包含 model.onnx、meta.json 和 samples.npz）"] = new("Model folder containing model.onnx, meta.json and samples.npz", "model.onnx、meta.json、samples.npz を含むモデルフォルダー");
        translations["评分设置"] = new("Scoring Settings", "スコア設定");
        translations["选择图片"] = new("Choose Image", "画像を選択");
        translations["选择文件夹"] = new("Choose Folder", "フォルダーを選択");
        translations["包含子文件夹"] = new("Include Subfolders", "サブフォルダーを含める");
        translations["使用 GPU"] = new("Use GPU", "GPU を使用");
        translations["复制到等级文件夹"] = new("Copy into Grade Folders", "評価別フォルダーにコピー");
        translations["下载使用代理设置"] = new("Use Proxy Settings for Downloads", "ダウンロードにプロキシ設定を使用");
        translations["按 DeepGHS 百分位确定等级，自动复制图片与同名标签并保留原图。输出包含全部七个等级。模型与校准数据首次下载约 419 MiB。"] = new("Grades follow DeepGHS percentiles. Images and matching tags are copied while originals are kept. All seven grade folders are created. Model and calibration downloads total about 419 MiB.", "DeepGHS の百分位で評価を決定し、元画像を保持して画像と同名タグをコピーします。7 段階すべてのフォルダーを作成します。モデルと校正データの初回ダウンロードは約 419 MiB です。");
        translations["开始评分"] = new("Start Scoring", "スコア計算開始");
        translations["导出评分结果"] = new("Export Scores", "スコアをエクスポート");
        translations["打开输出目录"] = new("Open Output Folder", "出力フォルダーを開く");
        translations["打开选中图片"] = new("Open Selected Image", "選択した画像を開く");
        translations["文件名"] = new("File Name", "ファイル名");
        translations["等级"] = new("Grade", "評価");
        translations["评分（0～6）"] = new("Score (0–6)", "スコア（0～6）");
        translations["百分位"] = new("Percentile", "百分位");
        translations["最高类别概率"] = new("Max Probability", "最大クラス確率");
        translations["置信度"] = new("Confidence", "信頼度");
        translations["错误信息"] = new("Error", "エラー");
        translations["运行日志"] = new("Run Log", "実行ログ");
        translations["模型已就绪"] = new("Model ready", "モデルの準備完了");
        translations["请先下载模型或选择已有模型目录。"] = new("Download the model first or select an existing model folder.", "モデルをダウンロードするか、既存のモデルフォルダーを選択してください。");
        translations["GPU 不可用，已切换到 CPU。"] = new("GPU unavailable; switched to CPU.", "GPU が使用できないため CPU に切り替えました。");
        translations["GPU 推理失败，已切换到 CPU。"] = new("GPU inference failed; switched to CPU.", "GPU 推論に失敗したため CPU に切り替えました。");
        translations["输出路径不能是输入路径或其上级目录。"] = new("The output folder cannot be the input folder or one of its parents.", "出力先に入力フォルダーまたはその親フォルダーは指定できません。");
        translations["请选择输出路径。"] = new("Choose an output folder.", "出力フォルダーを選択してください。");
        translations["没有找到支持的图片。"] = new("No supported images were found.", "対応する画像が見つかりませんでした。");
        translations["正在下载并校验模型…"] = new("Downloading and verifying the model…", "モデルをダウンロードして検証しています…");
        translations["正在校验模型…"] = new("Verifying the model…", "モデルを検証しています…");
        translations["正在评分…"] = new("Scoring images…", "画像のスコアを計算しています…");
        translations["评分完成"] = new("Scoring complete", "スコア計算完了");
        translations["评分完成，部分图片失败。"] = new("Scoring complete; some images failed.", "スコア計算が完了しました。一部の画像で失敗しました。");
        translations["成功 {0} 张，失败 {1} 张。"] = new("Succeeded: {0}; failed: {1}.", "成功 {0} 枚、失敗 {1} 枚。");
        translations["正在导出…"] = new("Exporting…", "エクスポートしています…");
        translations["导出完成"] = new("Export complete", "エクスポート完了");
        translations["操作失败，请查看日志。"] = new("Operation failed. See the log for details.", "操作に失敗しました。ログを確認してください。");
    }
}
