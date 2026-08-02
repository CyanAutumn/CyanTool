using System.Collections.Generic;

namespace CyanTools.Localization
{



    internal static class LocalizationDialogsSupplement
    {
        internal static void AddTo(Dictionary<string, LocalizationManager.LocalizedText> translations)
        {
            Add(translations, "错误", "Error", "エラー");
            Add(translations, "提示", "Notice", "お知らせ");
            Add(translations, "警告", "Warning", "警告");
            Add(translations, "成功", "Success", "成功");
            Add(translations, "完成", "Completed", "完了");
            Add(translations, "信息", "Information", "情報");
            Add(translations, "CUDA 安装", "CUDA Installation", "CUDA インストール");
            Add(translations, "cuDNN 安装", "cuDNN Installation", "cuDNN インストール");
            Add(translations, "cuDNN 安装失败", "cuDNN Installation Failed", "cuDNN のインストールに失敗しました");
            Add(translations, "CUDA/cuDNN 检查", "CUDA/cuDNN Check", "CUDA/cuDNN の確認");
            Add(translations, "CUDA/cuDNN 检查/安装", "CUDA/cuDNN Check/Installation", "CUDA/cuDNN の確認/インストール");
            Add(translations, "CUDA 环境可用，使用 GPU 推理的工具可以正常启用。", "The CUDA environment is available. Tools that use GPU inference can now be enabled.", "CUDA 環境を利用できます。GPU 推論を使用するツールを有効にできます。");
            Add(translations, "下载失败", "Download Failed", "ダウンロードに失敗しました");
            Add(translations, "CSD 模型下载失败：{0}\n\n将为你打开模型页面，请手动下载后重试。", "CSD model download failed: {0}\n\nThe model page will open. Download it manually and try again.", "CSD モデルのダウンロードに失敗しました：{0}\n\nモデルページを開きます。手動でダウンロードしてから再試行してください。");
            Add(translations, "缺少模型", "Missing Model", "モデルがありません");
            Add(translations, "检测到缺少 CSD 模型文件：\n{0}\n\n是否自动下载该模型？", "The CSD model file is missing:\n{0}\n\nDownload this model automatically?", "CSD モデルファイルがありません：\n{0}\n\nこのモデルを自動的にダウンロードしますか？");
            Add(translations, "启动 winget 失败：{0}\n\n将为你打开 CUDA 与 cuDNN 下载页面，请手动安装。", "Failed to start winget: {0}\n\nThe CUDA and cuDNN download pages will open. Install them manually.", "winget を起動できませんでした：{0}\n\nCUDA と cuDNN のダウンロードページを開きます。手動でインストールしてください。");
            Add(translations, "{0}\n\n是否尝试使用 winget 安装 CUDA Toolkit 12.x（推荐 12.4）？\n\n选择“否”会打开 NVIDIA CUDA 与 cuDNN 下载页面。cuDNN 可在本页拖入 zip 自动安装。", "{0}\n\nTry installing CUDA Toolkit 12.x with winget (12.4 recommended)?\n\nSelecting No opens the NVIDIA CUDA and cuDNN download pages. You can drag a cuDNN zip onto this page to install it automatically.", "{0}\n\nwinget で CUDA Toolkit 12.x（12.4 推奨）をインストールしますか？\n\n「いいえ」を選ぶと NVIDIA CUDA と cuDNN のダウンロードページを開きます。cuDNN の zip をこのページにドロップすると自動インストールできます。");
            Add(translations, "{0}\n\n{1}\n\n如果仍提示缺少 DLL，请重启本软件后再检查一次。", "{0}\n\n{1}\n\nIf a DLL is still reported missing, restart this application and check again.", "{0}\n\n{1}\n\nDLL が見つからないと表示される場合は、このアプリを再起動してもう一度確認してください。");
            Add(translations, "{0}\n\n请确认拖入的是 NVIDIA cuDNN 9 Windows zip 包，并且程序目录或 CUDA 12 bin 目录可写。", "{0}\n\nMake sure the file is an NVIDIA cuDNN 9 Windows zip package and that the application folder or CUDA 12 bin folder is writable.", "{0}\n\nNVIDIA cuDNN 9 Windows の zip パッケージであること、およびアプリのフォルダーまたは CUDA 12 の bin フォルダーに書き込めることを確認してください。");
            Add(translations, "{0}\n\n检测到当前程序的 onnxruntime.dll 不支持 CUDA 执行提供器。\n这不是安装 CUDA Toolkit 能解决的问题，请更新/重新发布带 `Microsoft.ML.OnnxRuntime.Gpu` 的版本。", "{0}\n\nThe current onnxruntime.dll does not support the CUDA execution provider.\nInstalling CUDA Toolkit will not fix this. Update or republish the application with `Microsoft.ML.OnnxRuntime.Gpu`.", "{0}\n\n現在の onnxruntime.dll は CUDA Execution Provider に対応していません。\nCUDA Toolkit のインストールでは解決しません。`Microsoft.ML.OnnxRuntime.Gpu` を含む版に更新または再配布してください。");
            Add(translations, "WSL 本地代理端口必须是 0-65535 的整数；填 0 或留空表示不使用代理。", "The WSL local proxy port must be an integer from 0 to 65535. Enter 0 or leave it blank to disable the proxy.", "WSL ローカルプロキシのポートは 0～65535 の整数にしてください。0 または空欄でプロキシを無効にします。");
            Add(translations, "{0}\n\n请查看选项页日志。WSL/Ubuntu 安装可能需要管理员权限或重启 Windows；Windows 后端首次部署会下载 Git、uv、Python 与 CUDA 依赖。", "{0}\n\nSee the log on the Options page. WSL/Ubuntu installation may require administrator privileges or a Windows restart. The first Windows backend deployment downloads Git, uv, Python, and CUDA dependencies.", "{0}\n\nオプションページのログを確認してください。WSL/Ubuntu のインストールには管理者権限または Windows の再起動が必要な場合があります。Windows バックエンドの初回展開では Git、uv、Python、CUDA の依存関係をダウンロードします。");
            Add(translations, "{0} 部署", "Deploy {0}", "{0} を展開");
            Add(translations, "确认一键部署全部训练后端？\n\n将按当前状态依次处理 sd-scripts、wsl-sd-scripts、Anima Fast LoRA 和 diffusion-pipe：可用项执行校验，损坏项重装，未安装项安装。WSL 后端要求先准备好 WSL2 与 Ubuntu。", "Deploy all training backends with one click?\n\nsd-scripts, wsl-sd-scripts, Anima Fast LoRA, and diffusion-pipe will be processed according to their current state: available items are verified, damaged items are reinstalled, and missing items are installed. WSL2 and Ubuntu must be ready for WSL backends.", "すべての学習バックエンドを一括展開しますか？\n\nsd-scripts、wsl-sd-scripts、Anima Fast LoRA、diffusion-pipe を現在の状態に応じて順に処理します。利用可能な項目は検証し、破損した項目は再インストールし、未インストールの項目はインストールします。WSL バックエンドには WSL2 と Ubuntu が必要です。");
            Add(translations, "重装 {0}", "Reinstall {0}", "{0} を再インストール");
            Add(translations, "确认重装 {0}？\n\n这会删除该后端的旧仓库和独立 Python/conda 环境，然后重新下载；不会删除训练集、模型或整个 Ubuntu 子系统。", "Reinstall {0}?\n\nThis deletes the backend's old repository and isolated Python/conda environment, then downloads them again. Training sets, models, and the Ubuntu subsystem are not deleted.", "{0} を再インストールしますか？\n\nこのバックエンドの古いリポジトリと専用 Python/conda 環境を削除して再ダウンロードします。学習セット、モデル、Ubuntu サブシステム全体は削除しません。");
            Add(translations, "删除 Ubuntu 子系统", "Remove Ubuntu Subsystem", "Ubuntu サブシステムを削除");
            Add(translations, "重新安装 Ubuntu 子系统", "Reinstall Ubuntu Subsystem", "Ubuntu サブシステムを再インストール");
            Add(translations, "确认删除 Ubuntu 子系统？\n\n注意：这会删除 Ubuntu 子系统内的全部数据，包括所有 WSL 训练后端。", "Remove the Ubuntu subsystem?\n\nWarning: this deletes all data in the Ubuntu subsystem, including every WSL training backend.", "Ubuntu サブシステムを削除しますか？\n\n注意：すべての WSL 学習バックエンドを含め、Ubuntu サブシステム内の全データが削除されます。");
            Add(translations, "检测到 Ubuntu 子系统已经存在。继续将注销并重新安装 Ubuntu，永久删除该子系统内的全部文件和训练环境。确认继续？", "An Ubuntu subsystem already exists. Continuing unregisters and reinstalls Ubuntu, permanently deleting every file and training environment in that subsystem. Continue?", "Ubuntu サブシステムはすでに存在します。続行すると Ubuntu を登録解除して再インストールし、サブシステム内のすべてのファイルと学習環境を完全に削除します。続行しますか？");

            Add(translations, "再敲咬你啊", "Knock again and I'll bite!", "また叩いたら噛むよ！");
            Add(translations, "URL 格式无效。", "The URL format is invalid.", "URL の形式が正しくありません。");
            Add(translations, "列表页标签不能关闭。", "The list-page tab cannot be closed.", "一覧ページのタブは閉じられません。");
            Add(translations, "未检测到帖子标签页（/posts/{id}）。请先中键打开帖子页标签。", "No post tab (/posts/{id}) was detected. Middle-click a post to open its tab first.", "投稿タブ（/posts/{id}）が見つかりません。先に投稿を中クリックしてタブを開いてください。");
            Add(translations, "初始化浏览器失败: {0}", "Failed to initialize the browser: {0}", "ブラウザーの初期化に失敗しました：{0}");
            Add(translations, "加载分布图失败: {0}", "Failed to load the distribution chart: {0}", "分布図の読み込みに失敗しました：{0}");
            Add(translations, "打开结果目录失败: {0}", "Failed to open the result folder: {0}", "結果フォルダーを開けませんでした：{0}");
            Add(translations, "请拖入 PNG/JPG/JPEG/BMP/GIF 格式的图片。", "Drop an image in PNG, JPG, JPEG, BMP, or GIF format.", "PNG/JPG/JPEG/BMP/GIF 形式の画像をドロップしてください。");
            Add(translations, "分层融合完成。", "Layered merge completed.", "レイヤー別マージが完了しました。");
            Add(translations, "请先输入有效的输入路径。", "Enter a valid input path first.", "先に有効な入力パスを指定してください。");
            Add(translations, "请至少保留一种文件类型。", "Keep at least one file type.", "少なくとも 1 種類のファイル形式を残してください。");
            Add(translations, "操作完成。共删除了 {0} 个文件。", "Operation completed. Deleted {0} files.", "操作が完了しました。{0} 個のファイルを削除しました。");
            Add(translations, "处理文件时发生错误: {0}", "An error occurred while processing files: {0}", "ファイルの処理中にエラーが発生しました：{0}");
            Add(translations, "输入路径无效。", "The input path is invalid.", "入力パスが無効です。");
            Add(translations, "输出路径无效。", "The output path is invalid.", "出力パスが無効です。");
            Add(translations, "输出路径不能与输入路径相同，且不能位于输入路径内。", "The output path cannot be the same as or inside the input path.", "出力パスを入力パスと同じ場所、または入力パス内に設定することはできません。");
            Add(translations, "同源图片归类完成！", "Same-source image grouping completed!", "同一ソース画像の分類が完了しました！");
            Add(translations, "处理失败: {0}", "Processing failed: {0}", "処理に失敗しました：{0}");
            Add(translations, "模型元数据已保存。张量数据未被解析或修改。", "Model metadata was saved. Tensor data was neither parsed nor modified.", "モデルのメタデータを保存しました。テンソルデータは解析も変更もされていません。");

            Add(translations, "无法打开视频文件。", "Could not open the video file.", "動画ファイルを開けませんでした。");
            Add(translations, "无法获取视频帧率，将使用默认值 30 FPS。", "Could not read the video frame rate. The default value of 30 FPS will be used.", "動画のフレームレートを取得できません。既定値の 30 FPS を使用します。");
            Add(translations, "无法读取帧。", "Could not read the frame.", "フレームを読み取れませんでした。");
            Add(translations, "请先选择视频并确保已选择输出路径。", "Select a video and an output path first.", "先に動画と出力パスを選択してください。");
            Add(translations, "当前帧已保存到: {0}", "The current frame was saved to: {0}", "現在のフレームを次の場所に保存しました：{0}");
            Add(translations, "保存当前帧失败: {0}", "Failed to save the current frame: {0}", "現在のフレームを保存できませんでした：{0}");
            Add(translations, "所有帧已根据帧间隔保存到: {0}", "All frames were saved at the selected interval to: {0}", "指定した間隔ですべてのフレームを次の場所に保存しました：{0}");
            Add(translations, "保存所有帧失败: {0}", "Failed to save all frames: {0}", "すべてのフレームを保存できませんでした：{0}");
            Add(translations, "GIF 已保存到: {0}", "The GIF was saved to: {0}", "GIF を次の場所に保存しました：{0}");
            Add(translations, "导出 GIF 失败: {0}", "Failed to export the GIF: {0}", "GIF のエクスポートに失敗しました：{0}");
            Add(translations, "加载图片失败: {0}", "Failed to load the image: {0}", "画像の読み込みに失敗しました：{0}");
            Add(translations, "请先拖入图片。", "Drop an image first.", "先に画像をドロップしてください。");
            Add(translations, "无法解析 JSON 元数据。", "Could not parse the JSON metadata.", "JSON メタデータを解析できませんでした。");
            Add(translations, "元数据已更新。", "Metadata was updated.", "メタデータを更新しました。");
            Add(translations, "更新元数据失败: {0}", "Failed to update metadata: {0}", "メタデータの更新に失敗しました：{0}");
            Add(translations, "已将文本写入 PNG tEXt (Prompt)。", "The text was written to PNG tEXt (Prompt).", "テキストを PNG tEXt（Prompt）に書き込みました。");
            Add(translations, "已将文本写入 EXIF ImageDescription。", "The text was written to EXIF ImageDescription.", "テキストを EXIF ImageDescription に書き込みました。");
            Add(translations, "写入纯文本失败: {0}", "Failed to write plain text: {0}", "プレーンテキストの書き込みに失敗しました：{0}");

            Add(translations, "分类{0}的目标文件夹未设置或不存在。", "The destination folder for category {0} is not set or does not exist.", "カテゴリ {0} の保存先フォルダーが未設定か、存在しません。");
            Add(translations, "处理文件 {0} 时出错: {1}", "Error while processing file {0}: {1}", "ファイル {0} の処理中にエラーが発生しました：{1}");
            Add(translations, "应用更改时出错: {0}", "Error while applying changes: {0}", "変更の適用中にエラーが発生しました：{0}");
            Add(translations, "读取文本失败: {0}", "Failed to read text: {0}", "テキストの読み込みに失敗しました：{0}");
            Add(translations, "请输入大于等于 0 的整数。", "Enter an integer greater than or equal to 0.", "0 以上の整数を入力してください。");
            Add(translations, "保存标签翻译失败: {0}", "Failed to save tag translations: {0}", "タグ翻訳の保存に失敗しました：{0}");
            Add(translations, "翻译未翻译标签失败: {0}", "Failed to translate untranslated tags: {0}", "未翻訳タグの翻訳に失敗しました：{0}");
            Add(translations, "未成功处理任何图片，请检查图片格式或模型是否正确。", "No images were processed successfully. Check the image formats and model.", "画像を処理できませんでした。画像形式とモデルを確認してください。");
            Add(translations, "{0}\n\n未能生成分布图。", "{0}\n\nThe distribution chart could not be generated.", "{0}\n\n分布図を生成できませんでした。");
            Add(translations, "没有在子文件夹中找到需要移动的文件。", "No files to move were found in the subfolders.", "サブフォルダー内に移動対象のファイルが見つかりませんでした。");
            Add(translations, "操作完成。\n成功移动 {0} 个文件。\n跳过 {1} 个重复文件。", "Operation completed.\nMoved {0} files.\nSkipped {1} duplicate files.", "操作が完了しました。\n{0} 個のファイルを移動しました。\n重複ファイル {1} 個をスキップしました。");
            Add(translations, "发生错误: {0}", "An error occurred: {0}", "エラーが発生しました：{0}");
            Add(translations, "目标目录中已有 {0} 个同名文件，是否覆盖？", "The destination already contains {0} files with the same names. Overwrite them?", "保存先に同名ファイルが {0} 個あります。上書きしますか？");
            Add(translations, "覆盖训练配置", "Overwrite Training Configuration", "学習設定を上書き");

            Add(translations, "该内容已存在于文件: {0}\n跳过添加。", "This content already exists in file: {0}\nThe addition was skipped.", "この内容はすでに次のファイルにあります：{0}\n追加をスキップしました。");
            Add(translations, "该内容已存在于文件: {0}", "This content already exists in file: {0}", "この内容はすでに次のファイルにあります：{0}");
            Add(translations, "检查重复内容时出错: {0}", "Error while checking for duplicate content: {0}", "重複内容の確認中にエラーが発生しました：{0}");
            Add(translations, "确定要删除 {0} 吗？", "Delete {0}?", "{0} を削除しますか？");
            Add(translations, "删除关联图片文件失败: {0}", "Failed to delete the associated image file: {0}", "関連する画像ファイルを削除できませんでした：{0}");
            Add(translations, "已创建文件: {0}", "Created file: {0}", "ファイルを作成しました：{0}");
            Add(translations, "分类失败: {0}", "Bucketing failed: {0}", "分類に失敗しました：{0}");
            Add(translations, "分类完成!\n\n总计图片: {0} 张\n创建分类: {1} 个", "Bucketing completed!\n\nTotal images: {0}\nBuckets created: {1}", "分類が完了しました！\n\n画像合計：{0} 枚\n作成した分類：{1} 個");
            Add(translations, "无法打开路径选择器: {0}", "Could not open the path picker: {0}", "パス選択画面を開けませんでした：{0}");
            Add(translations, "生成预览时出现错误: {0}", "An error occurred while generating the preview: {0}", "プレビューの生成中にエラーが発生しました：{0}");
            Add(translations, "请先选择有效的输入路径并生成预览。", "Select a valid input path and generate a preview first.", "先に有効な入力パスを選択してプレビューを生成してください。");
            Add(translations, "没有需要重命名的文件。", "There are no files to rename.", "名前を変更するファイルがありません。");
            Add(translations, "请确保当前目录中至少包含两张图片。", "Make sure the current folder contains at least two images.", "現在のフォルダーに画像が 2 枚以上あることを確認してください。");
            Add(translations, "图片数量不足，无法打乱。", "There are not enough images to shuffle.", "画像が足りないためシャッフルできません。");
            Add(translations, "打乱过程中出现错误: {0}", "An error occurred while shuffling: {0}", "シャッフル中にエラーが発生しました：{0}");
            Add(translations, "重命名失败: {0}", "Rename failed: {0}", "名前の変更に失敗しました：{0}");
            Add(translations, "切换 Git commit", "Switch Git Commit", "Git commit を切り替え");
            Add(translations, "当前工作区有未提交改动。切换 commit 可能失败，也可能覆盖当前正在查看的状态。\n\n请先提交或备份；确认继续切换吗？", "The working tree has uncommitted changes. Switching commits may fail or overwrite the state currently being viewed.\n\nCommit or back up the changes first. Continue switching?", "作業ツリーに未コミットの変更があります。commit の切り替えに失敗したり、現在表示中の状態が上書きされたりする可能性があります。\n\n先にコミットまたはバックアップしてください。切り替えを続けますか？");
            Add(translations, "删除路径绑定", "Delete Path Binding", "パスの関連付けを削除");
            Add(translations, "确定删除路径绑定“{0}”吗？不会删除任何外部程序或目录。", "Delete the path binding “{0}”? No external application or folder will be deleted.", "パスの関連付け「{0}」を削除しますか？外部プログラムやフォルダーは削除されません。");
            Add(translations, "跑图后端路径", "Image-generation Backend Path", "画像生成バックエンドのパス");
            Add(translations, "路径绑定", "Path Binding", "パスの関連付け");
            Add(translations, "删除配置文件", "Delete Configuration", "設定ファイルを削除");
            Add(translations, "确定删除配置文件“{0}”吗？", "Delete configuration “{0}”?", "設定ファイル「{0}」を削除しますか？");
            Add(translations, "生成 TOML 失败: {0}", "Failed to generate TOML: {0}", "TOML の生成に失敗しました：{0}");
            Add(translations, "训练执行失败: {0}", "Training execution failed: {0}", "学習の実行に失敗しました：{0}");
            Add(translations, "请先选择 lora-scripts 根目录。", "Select the lora-scripts root folder first.", "先に lora-scripts のルートフォルダーを選択してください。");
            Add(translations, "请先设置 TOML 输出路径。", "Set the TOML output path first.", "先に TOML の出力パスを設定してください。");
            Add(translations, "请先设置有效的训练脚本路径。", "Set a valid training script path first.", "先に有効な学習スクリプトのパスを設定してください。");
            Add(translations, "处理过程中发生错误: {0}", "An error occurred during processing: {0}", "処理中にエラーが発生しました：{0}");
            Add(translations, "请先选择一个包含子目录的有效输入路径。", "Select a valid input path containing subfolders first.", "先にサブフォルダーを含む有効な入力パスを選択してください。");
            Add(translations, "确认操作", "Confirm Operation", "操作の確認");
            Add(translations, "您确定要根据表格中的重复次数重命名文件夹吗？\n此操作将直接修改您的文件系统，且不可逆。", "Rename folders according to the repeat counts in the table?\nThis directly modifies the file system and cannot be undone.", "表の繰り返し回数に従ってフォルダー名を変更しますか？\nこの操作はファイルシステムを直接変更し、元に戻せません。");
            Add(translations, "无法重命名 '{0}' 为 '{1}'，因为目标路径已存在。", "Could not rename '{0}' to '{1}' because the destination path already exists.", "保存先のパスがすでに存在するため、'{0}' を '{1}' に変更できません。");
            Add(translations, "操作完成！成功重命名 {0} 个文件夹。", "Operation completed. Renamed {0} folders.", "操作が完了しました。{0} 個のフォルダー名を変更しました。");
            Add(translations, "重命名过程中发生错误：\n{0}", "An error occurred while renaming:\n{0}", "名前の変更中にエラーが発生しました：\n{0}");
        }

        private static void Add(
            Dictionary<string, LocalizationManager.LocalizedText> translations,
            string key,
            string english,
            string japanese)
            => translations[key] = new LocalizationManager.LocalizedText(english, japanese);
    }
}
