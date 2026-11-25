# 0.3.32 通用主播 · 面捕漏检容错验证

Windows x64 本地开发构建，未生成发布 ZIP。`python Tests/presenter_screenshots.py` 通过：31 张 1280×720 截图，369 次窗口采样未发现可见窗口。有效构建和截图哈希见 `Builds/QA-RealistDev-6mocik3_/evidence.json`；对应完成标记为 `XR_PLAYER_OFFSCREEN_OK 0.3.32`。

- `XR_PLAYER_FACE_DROPOUT_OK`：在纯绿幕下，以 30 FPS 合成时间戳覆盖前三次检测失败时保持头部和表情；第 4 次（距最后一次有效样本约 133 毫秒）已超过 120 毫秒容错，开始平滑回正。另覆盖没有新帧时按渲染时钟过期、恢复时采用新姿态、关闭清除残留。截图覆盖漏检前、保持和回到待机三个阶段。
- 已复核 `LiveHeldFaceDropout.png` 与 `LiveExpiredFaceDropout.png` 中的转头保持和中性姿态。面捕模型的离线静态人像推理以及绿幕、校准、设备诊断、关闭逻辑等已有回归仍通过；漏检序列是合成的，不是实际 30 FPS 性能测量或真人输入。
- 未使用实体摄像头、真实键盘、OBS 或长测。实际真人跟踪、系统焦点、硬件占用与释放仍未验证。
