# XReality 0.3.27 无窗口验收

ZIP：`XReality-0.3.27-Windows-x64-portable-preview.zip`，SHA-256 `b1ba1b471101755850981fc6c0bb80b249e14177f9a0f18cdd7aad5489fd706d`。Windows x64 包结构核验：183 个文件、8 个 AMD64 原生映像。继续遵守只做无窗口截图、不使用 OBS 或实体摄像头的限制。

- 宠物截图经相机投影、实际物理碰撞体和共用的指针处理方法触发。断言屋内点击可分别触发耳朵回应和蹭手，连续点击只计一次；抓起毛线球不跳位或触发追逐，指针移到面板区域时不拖动，返回场景后保留抓取偏移，面板上释放后不留下拖动状态。验证的是应用内部的射线与输入路由，不是系统真实键鼠、操作系统失焦通知或窗口面板布局。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --local-manual-resume-screenshots --soak-seconds 60` 退出码 0。39 张 1280×720 截图、2,703 次窗口采样均未发现可见窗口；`verify-evidence` 核验 43 个文件。证据：`Builds/QA-Packaged-jk2_52d4/`。
- 同轮验证双入口、跨进程宠物档恢复、内置模型离线就绪、化身校准／恢复、绿幕和本地 HTTPS 模型修复。60.01 秒 720p 离屏平均 33.26 FPS、P95 帧间隔 49 ms、最长 187.4 ms，不代表真人跟踪。0.3.27 的 30 分钟离屏测试已按用户要求取消，未生成验收报告，不计为通过或失败。后续只做短时无窗口截图回归；0.3.26 的长测结果不沿用到本版。

- `python Tools/project.py upgrade-screenshots --upgrade-from Builds/XReality-0.3.26-Windows-x64-portable-preview.zip` 退出码 0。两个版本分别解包运行，使用同一隔离测试档；新版恢复断言通过。16 张截图、1,517 次窗口采样未发现可见窗口，18 个文件通过证据完整性核验。证据：`Builds/QA-Upgrade-8blu053x/evidence.json`。

仍无真实键鼠、真人跟踪、物理摄像头生命周期／权限、两台 Windows 兼容性证据。
