# 图片三分法

`图片三分法`会对输入图片进行多阶段检测与裁剪，生成更“聚焦”的训练素材：人物 → 半身 → 头部（可选：眼部）。输出为一组裁剪后的新图片文件。

## 使用前准备

需要在程序基目录下存在对应 ONNX 模型（项目已配置为随程序复制）：

- 人物检测：`Models/anime_person_detection/person_detect_v1.1_m.onnx`
- 半身检测：`Models/anime_halfbody_detection/halfbody_detect_v1.0_s.onnx`
- 头部检测：`Models/anime_head_detection/head_detect_v0_s.onnx`
- 眼部检测：`Models/anime_eye_detection/eye_detect_v1.0_s.onnx`

若模型缺失，开始处理时会报错并停止。

## 快速上手

1. 选择输入/输出文件夹（只处理输入目录顶层图片）。
2. 按需开启“启用人物检测”“启用眼部检测”，并设置各阶段置信度阈值与缩放倍率。
3. 点击“开始处理”。

## 输出命名规则

假设输入文件为 `a.png`：

- 人物裁剪：`a_person1.png`、`a_person2.png` ...（检测到多个人物时）
- 半身裁剪：`a_person{n}_halfbody.png`（每个人物只取第一个半身检测结果）
- 头部裁剪：`a_person{n}_head.png`（每个人物只取第一个头部检测结果）
- 眼部裁剪：`a_person{n}_head_eye0.png`、`a_person{n}_head_eye1.png` ...（对每个头部可检测多个眼部）

## 参数说明（建议）

- 各“置信度阈值”越高，输出越少但更稳定；阈值过低可能产生误检裁剪。
- “头部缩放/眼部缩放”会在检测框中心周围扩张为更大的方形裁剪区域，避免裁剪过紧。

## 注意事项

- 该工具输出到单一输出目录，不会保留原有子目录结构。
- 处理过程不会修改输入原图，仅在输出目录写入新文件。

