# XReality 0.3.26 无窗口验收

核验对象：`XReality-0.3.26-Windows-x64-portable-preview.zip`，SHA-256 `a197a09a5f5dfe9894e1b7f39d8df1dad01e5a2440e23c1eb0dc54ab5dd81bb0`。此版重做化身发型、眼部比例和衣领，未更换角色驱动或面捕模型。全程仅执行无窗口截图，不启用实体摄像头、OBS 或物理输入。

- 编译及 ZIP 核验：183 个文件、8 个原生 AMD64 映像；面捕模型已锁定哈希，测试原图不随包发行。
- 编辑器离屏关键帧：左眼半闭／左眼全闭／右眼全闭在各自眼区分别有 806／903／919 个变化像素，对侧为 0；张嘴区域有 1,122 个变化像素。截图验证表情在新造型上仍可见，不是摄像头测试。日志 `Builds/unity-offscreen-expressions.log`。
- 解包后执行 `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --local-manual-resume-screenshots --soak-seconds 60`，退出码 0。证据 `Builds/QA-Packaged-l_t9ouih/`：39 张 1280×720 PNG、3,786 次采样未发现可见窗口；`verify-evidence` 绑定同一 ZIP 的 43 个文件。
- 通过内置模型离线就绪、校准、跟踪归位与恢复、纯绿幕隔离、猫咪互动与跨进程存档、模型修复路径本地 HTTPS 续传。720p 连续 60.01 秒，平均 39.39 FPS，P95 29 ms，最长单帧间隔 115.3 ms；固定夹具，不是实拍或桌面呈现性能。
- [前后对比图](Builds/XReality-0.3.26-Avatar-Before-After.png) 由两个版本的解包截图裁切拼合，仅作视觉评审，原始截图和日志以各自证据包为准。

正式发行仍未验收真实摄像头权限与释放、真实键鼠、桌面失焦、30 分钟真人跟踪及第二台干净 Windows。0.3.25 的 30 分钟离屏报告不适用于这个 ZIP。

## 同一 ZIP：30 分钟离屏耐久

- 同样的解包测试改用 `--soak-seconds 1800`，退出码 0；证据 `Builds/QA-Packaged-ognsn_q_/`，包含 39 张 1280×720 原图和 7 个耐久检查点。59,055 次窗口采样均未发现可见窗口；`verify-evidence` 核验 43 个文件。自动选择 Direct3D 11。
- 连续 720p 绿幕 **1,800.01 秒**、平均 **38.43 FPS**，P95 帧间隔 32 ms，最长 371.1 ms；有个别长帧，不能据平均值声称全程无卡顿。测试人为插入一次丢失并确认恢复，记录 69,177 次离屏绘制。181 次采样的工作集初值／末值／峰值为 409.3／422.8／422.8 MiB，单次测试不能证明有或没有内存泄漏。
- 同一轮确认内置模型离线就绪、宠物跨进程恢复和本机 HTTPS 断点续传。屏幕呈现、桌面失焦、实际输入及真人相机仍未验证。

## 同包 Direct3D 12 离屏补测

`python Tools/project.py packaged-screenshots --graphics d3d12 --offline-tracking --pet-interaction` 退出码 0，日志确认实际使用 **Direct3D 12（feature level 12.1）**。`Builds/QA-Packaged-6x7d28ho/` 有 26 张 1280×720 截图、1,509 次窗口检查均未发现可见窗口；`verify-evidence` 核验 28 个文件。双入口、猫咪存档恢复、内置模型、跟踪校准和绿幕隔离通过。同一设备的另一渲染后端不等于另一台电脑，没有复跑 30 分钟。

## 跨版本存档（不重新打包）

通过 `python Tools/project.py upgrade-screenshots --upgrade-from <旧版ZIP>`。从两个原始便携 ZIP 分别解包启动：旧版用独立 QA 档写入猫咪名「窗边栗子」、亲密度 5、窗边偏好；退出后，新版首次打开同一档，断言数据原样保留且猫会自己去窗边。未打开可见窗口、未启用摄像头，未读写正式宠物档。

| 实际源版→目标版 | 截图 | 可见窗口检查 | 证据目录 |
|---|---:|---|---|
| 0.3.24→0.3.26 | 15 | 1,393 次均未发现 | `Builds/QA-Upgrade-3y03irba/` |
| 0.3.25→0.3.26 | 16 | 1,438 次均未发现 | `Builds/QA-Upgrade-88w3oswh/` |

`verify-evidence --evidence <目录>/evidence.json --upgrade-from <旧版ZIP>` 同时验证源、目标 ZIP 哈希、两段运行日志及截图完整性；用错误的源 ZIP 会被拒绝。这验证同机跨版本存档格式迁移，不等于另一台 Windows 或真实鼠标键盘的验收。
