# XReality 0.3.25 无窗口验收

核验对象：`XReality-0.3.25-Windows-x64-portable-preview.zip`，SHA-256 `45def488e34f7e5d6339f24b4de6b77d8fdec6b9ff5b2cb211b6632a624458b7`。按要求仅使用无窗口截图，不开启实体摄像头，不使用 OBS。

| 项目 | 结果 | 边界 |
|---|---|---|
| Windows x64 包 | 通过 | SHA-256、CRC、183 个文件和 8 个 AMD64 原生映像通过验证。包内模型字节与锁定 SHA-256 一致，包含 Apache 2.0 授权和上游来源说明，不包含人像。 |
| 首次离线使用 | 通过 | 解包运行时经应用正常模型加载路径选择内置模型，面板显示「内置面捕模型已就绪 · 可离线使用」，没有开启相机。实际 CPU 推理也使用包内模型。 |
| 双入口、猫咪互动和存档 | 通过（离屏） | 验证首页转场、抚摸和追球、独立档跨进程恢复。不覆盖实际鼠标键盘。 |
| 化身和纯绿幕 | 通过（离屏） | 校准、跟踪、丢失、恢复、输出无控制面板；本地人像／空白帧测试，不代表真实摄像头。 |
| 模型修复 | 通过（隔离离屏） | 本地手动导入、损坏文件拒绝、HTTPS 断点续传。正常使用不依赖公网 Google 下载；未声明公网修复已通过。 |
| 720p 长时运行 | 通过（30 分钟离屏） | 连续 1,800.01 秒，平均 39.38 FPS，P95 帧间隔 29 ms，最长 480.8 ms；主动插入的丢失和恢复各一次。不是实拍性能。 |
| 实际设备、真人跟踪和第二台 Windows | 未验证 | 本轮约束不允许运行可见窗口或启用实体摄像头。 |

复现：`python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --local-manual-resume-screenshots --soak-seconds 30`。

证据：`Builds/QA-Packaged-jtspna23/`，39 张 1280×720 PNG，2,631 次窗口采样均未发现可见窗口。`verify-evidence` 验证同一 ZIP 的 43 个文件。见 `LiveBundledModelReady.png`、`live-soak.txt` 和两个 Player 日志。

[六张截图总览](Builds/XReality-0.3.25-Offscreen-Contact-Sheet.png) 是从上述 30 秒验收的原图拼合的便览，不是新增测试；核验以单张原图和 `evidence.json` 为准。

## 同一 ZIP：30 分钟离屏耐久

- 将上述命令的 `--soak-seconds 30` 改为 `--soak-seconds 1800`。进程退出码 0；证据 `Builds/QA-Packaged-xhnka06a/` 有 39 张 1280×720 原图（包括 7 个耐久检查点）、61,826 次窗口采样均未发现可见窗口。`verify-evidence` 核验 43 个文件。
- `live-soak.txt`：连续 1,800.01 秒／39.38 FPS；70,880 次离屏绘制；P95 帧间隔 29 ms、最长 480.8 ms。长帧说明平均帧率达标并不意味着无卡顿。181 次工作集采样，初值／末值／峰值 417.0／434.1／434.1 MiB；单次工作集变化不能证明有或没有泄漏。
- 完成后再次验证内置模型离线就绪、宠物跨进程恢复及本机 HTTPS 断点续传。测试服务在收到一次提前关闭的连接时打印 WinError 10053，但后续确实收到预期 `Range: bytes=524288-`，应用通过 HTTP 206 校验安装，测试整体成功。
- 离屏只处理本地夹具，不包含实体摄像头、可见桌面呈现、桌面失焦、真实输入设备或第二台电脑。
