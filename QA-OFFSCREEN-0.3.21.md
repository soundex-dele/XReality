# 0.3.21 无窗口验收记录

- `Builds/XReality-0.3.21-Windows-x64-portable-preview.zip` 已打包，SHA-256 `aabfb105eb191e3386f6c0096e472ad84ea17151a5652728b024642bb91d942d`。解包截图命令校验旁签、ZIP 和原生 x64 二进制后运行。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --soak-seconds 30` 通过：33 张 1280×720 PNG，2,686 次窗口采样未发现可见窗口；证据 `Builds/QA-Packaged-rocac1wg/`。
- 和 0.3.20 相同的 820×1024 本地人像，每次 720p 离屏绘制前运行一次 CPU 推理。连续绘制 30.01 秒，均值 39.05 FPS、P95 帧间隔 30 ms、最长 136.7 ms；1,173 次推理，检出 1,056 次，一次丢失后一次恢复；应用内工作集 4 个样本，404.6 / 416.3 / 416.3 MiB。与 0.3.20 短测 27.66 FPS 的比较不是受控多轮基准，不代表实体摄像头或桌面呈现。
- `python Tools/project.py packaged-screenshots --resume-screenshots` 通过：12 张截图、996 次窗口检查均无可见窗口，证据 `Builds/QA-Packaged-7gicm656/`。接收到 490,567 字节后主动中断，真实 HTTP 206 续传并校验安装。
- 本版本 1,800 秒离屏长测完成，进程退出码 0；24 张 1280×720 PNG，56,419 次窗口采样均无可见窗口，见 `Builds/QA-Packaged-qrd6ffdp/`。连续绘制 **1,800.01 秒、37.94 FPS**，达成同一台电脑的离屏时长和平均帧率门槛；P95 帧间隔 33 ms，最长 544.6 ms。68,300 次离屏绘制/推理，检测到本地人像 68,184 次，一次丢失后恢复。181 个工作集样本：401.2 / 425.2 / 425.4 MiB（首/末/峰）。长测显式选择了 Direct3D 11。
- 单独复验应用默认图形后端：没有强制 API，当前电脑实际选用 Direct3D 11。26 张截图，1,617 次窗口采样均无可见窗口；`Builds/QA-Packaged-1fu0jh5o/evidence.json` 对应版本、图形配置和 28 个文件哈希，已用 `verify-evidence` 通过完整性核验。
- 第 900.00 秒检查点已通过：化身从空白输入恢复，共 34,573 次绘制、34,457 次检出、116 次空白检测。`WINDOWS-PREREQUISITES.md` 另记录了静态检查发现的依赖：从该 ZIP 内 `mediapipe_c.dll` 直接导入表确认需要 `msvcp140.dll`、`concrt140.dll`、`vcruntime140.dll`、`vcruntime140_1.dll` 和 `mfplat.dll`、`mf.dll`、`mfreadwrite.dll`。这不等于另一台电脑已具备这些组件。
- 第 1,200.01 秒检查点通过，累计 45,171 次 720p 离屏绘制，说明长测在恢复后继续运行；

未启用实体摄像头或 OBS。实际输入、长时间真人摄像头及不同 Windows 电脑仍未验收。




