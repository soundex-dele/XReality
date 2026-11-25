# 0.3.33 通用主播 · 校准眨眼容错

Windows x64 本地开发构建，未生成发布 ZIP。`python Tests/presenter_screenshots.py` 通过：31 张 1280×720 截图，387 次窗口采样未发现可见窗口。证据：`Builds/QA-RealistDev-57vj8phg/evidence.json`，完成标记 `XR_PLAYER_OFFSCREEN_OK 0.3.33`。

- `XR_PLAYER_CALIBRATION_BLINK_OK`：在 CPU 推理所得的静态人像姿态中插入 4 个 33 毫秒间隔的合成眨眼样本。断言短眨眼不清零、不推进进度，睁眼后继续并完成；原有中性姿态、独立闭眼与表情渲染断言通过。
- 独立合成序列验证持续闭眼、张嘴、采样过期和逐步偏离初始头位时重计时；静态人像切到空帧后的丢失重计时回归仍通过。
- 人工复核 `LiveCalibrationNeutral.png`，完成后的主播为正向中性姿态。未使用实体摄像头、真实键盘、OBS 或长测，未验证现场光照下的识别质量、设备占用或释放。
