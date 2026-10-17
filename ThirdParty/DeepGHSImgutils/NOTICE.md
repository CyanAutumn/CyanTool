# DeepGHS aesthetic scoring

CyanTools' aesthetic scorer is ported directly from
[deepghs/imgutils](https://github.com/deepghs/imgutils), revision
`46df848dc4d20ac93f4919a40e2636d5f7c19766`:

- [`imgutils/generic/classify.py`](https://github.com/deepghs/imgutils/blob/46df848dc4d20ac93f4919a40e2636d5f7c19766/imgutils/generic/classify.py): image encoding and classifier probability output.
- [`imgutils/metrics/dbaesthetic.py`](https://github.com/deepghs/imgutils/blob/46df848dc4d20ac93f4919a40e2636d5f7c19766/imgutils/metrics/dbaesthetic.py): weighted score, sample-based percentile conversion, and percentile grade thresholds.
- [`imgutils/data/image.py`](https://github.com/deepghs/imgutils/blob/46df848dc4d20ac93f4919a40e2636d5f7c19766/imgutils/data/image.py) and [`encode.py`](https://github.com/deepghs/imgutils/blob/46df848dc4d20ac93f4919a40e2636d5f7c19766/imgutils/data/encode.py): white-background RGB loading and float32 channel encoding.

The upstream code is MIT licensed, Copyright (c) 2023 DeepGHS. Its complete
license is included in `LICENSE` and copied into application output.
That original MIT notice remains applicable to the upstream material.
CyanTools' original C# adaptation and integration contributions are licensed
under the project's root GPL-3.0-only license and attribution notice.

The C# port uses ImageSharp and .NET ONNX Runtime, consumes the model's `output`
probabilities without another softmax, reads the original `samples.npz` without
a Python runtime, and preserves the upstream search/interpolation and grade
thresholds. ImageSharp and Pillow resize rounding can produce small numerical
differences. WPF integration, cancellation, CPU fallback, verified downloads,
JSON reports, and paired classification copies are provided by CyanTools.

Model resources: [deepghs/anime_aesthetic](https://huggingface.co/deepghs/anime_aesthetic),
variant `swinv2pv3_v0_448_ls0.2_x`, revision
`a83ab545e1d2a869f1180b99b2a7dee22ec3b97e`. The model card identifies its license
as OpenRAIL. Model weights and calibration samples are downloaded separately.

| File | SHA-256 |
| --- | --- |
| `model.onnx` | `174a988ff458e84bd28c2a71cd22a5e7e160ec2518e501efbdfc3de1d09d82a2` |
| `meta.json` | `c8b90241aa5f99ad98b17a224711688ae1f7fa29ea9aeb4f7f010435c26d0eef` |
| `samples.npz` | `f37d22a7fbfb2748c007cefddf7b4c95602cb5037f847a81ab47fc5d4c1df013` |
