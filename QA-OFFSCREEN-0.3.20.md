# 0.3.20 无窗口验收记录

- ZIP：`Builds/XReality-0.3.20-Windows-x64-portable-preview.zip`；SHA-256 `86cf1666a289615b8289fde6ef38d24a657bb0f50adf9df3ff14fcd550fa6260`。静态发布包校验通过，包含 180 个文件与 8 个原生 AMD64 二进制。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --soak-seconds 30` 通过。33 张 1280×720 PNG，2,148 次窗口可见性采样均无可见窗口，截图及日志见 `Builds/QA-Packaged-g9muz0bc/`。
- `XR_PLAYER_CALIBRATION_OK`：测试人像从稳定采样到丢失重计时，再完成校准回正；施加有明确相对偏移的合成姿态后，头部、单眼和嘴部权重保持独立。三个离屏截图分别名为 `LiveCalibrationPending.png`、`LiveCalibrationNeutral.png` 与 `LiveCalibrationExpression.png`。
- 修复后的应用内工作集在短测得到 4 个样本，首个 402.8 MiB，末个和峰值均为 412.0 MiB。该轮连续离屏渲染 30.01 秒，平均 27.66 FPS（未达 30 FPS 目标），P95 55 ms；这里只验证在当前 Windows 电脑上的本地 CPU 测试输入和离屏渲染。
- 0.3.19 的 1,800.11 秒离屏耐久结果独立记录，不能替代 0.3.20 长测或真人测试。
- `python Tools/project.py packaged-screenshots --resume-screenshots` 通过。12 张 1280×720 PNG，349 次窗口采样均无可见窗口。真实下载主动中断后保留 395,565 字节，收到 HTTP 206 后校验并安装，临时文件清理；见 `Builds/QA-Packaged-ijfujg2v/`。

未启用实体摄像头或 OBS。真实鼠标键盘操作、30 分钟真人摄像头、不同 Windows 电脑仍是独立发布门槛。

