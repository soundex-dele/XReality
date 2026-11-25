# 0.3.31 通用写实主播 · 动作响应验证

Windows x64 本地开发构建，未生成发布 ZIP。`python Tests/presenter_screenshots.py` 通过：28 张 1280×720 截图，423 次窗口采样未发现可见窗口。最终构建与截图的 SHA-256 见 `Builds/QA-RealistDev-spwesapo/evidence.json`，日志标记为 `XR_PLAYER_OFFSCREEN_OK 0.3.31`。

- `XR_PLAYER_MOTION_RESPONSE_OK`：用合成输入模拟 30/60 FPS 更新，同样时长的单侧快速眨眼在两组测试均达到 95.9% 闭合；另一侧保持不变，随后睁开。另验证了组合转头限幅与回正。这不是实测帧率或相机延迟。
- 人工检查同一构建的 `LiveQuickBlink.png` 和 `LiveBoundedTurn.png`：左右眼可独立闭合；组合转头截图未再出现修正前可见的内口网格外露。截图只覆盖所测姿态，不能证明所有姿态下网格均正常。
- 保留离线人像跟踪、校准、丢脸恢复、绿幕输出、摄像头诊断与关闭流程回归。未使用实体摄像头、真实键盘、OBS 或长时间测试；未验证真实设备占用与释放、真人跟踪质量或系统焦点行为。
