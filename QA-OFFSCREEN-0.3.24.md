# 0.3.24 无窗口验收记录

- ZIP `Builds/XReality-0.3.24-Windows-x64-portable-preview.zip`，SHA-256 `99aa9376dbc4841455d20c7858c9bcbf4a9cdacdd52e348b59cc869ba5bac120`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --local-manual-resume-screenshots` 通过：31 张 1280×720 PNG，1,647 次窗口采样均未发现可见窗口。证据 `Builds/QA-Packaged-lg43o9fd/`；`verify-evidence` 核验 34 个截图／日志／报告文件完整性通过。
- 本机 HTTPS 服务在返回 524,288 字节后关闭连接，未缩小声明的 Content-Length。应用在自动尝试用尽后保留分段，截图显示「继续下载模型」。点击后，服务端实收 `Range: bytes=524288-`，返回 HTTP 206 和剩余数据；应用校验大小与 SHA-256、成功安装并移除 `.partial` 文件。`loopback-range.txt` 和 `XR_PLAYER_MANUAL_RESUME_OK` 从两端记录事务。
- 本机模型、证书和私钥不进入程序包。封装的测试入口只在隔离无窗口运行时访问 `127.0.0.1`，按证书指纹校验，不关闭正式下载的证书检查。此轮通过不代表公网下载通过。
- 独立公网 `--manual-resume-screenshots` 未完成安装：首次继续下载的 HTTP 206 将进度推进到 3,098,259 / 3,758,596 字节，该 206 请求被传输进度预算中止，下一轮未收到 HTTP 响应；应用未安装未校验文件。该结果不能判断 Google 服务整体可用性。4,528 次窗口检查未发现可见窗口，记录 `Builds/QA-Packaged-l7riz0mw/`。

- 单独复测 `--download-screenshots`（正常自动重试，无人为注入故障）也未完成：HTTP 200 传输后保留 2,472,804 / 3,758,596 字节，下一次请求未收到 HTTP 响应；截图显示可继续下载或改用浏览器。程序退出码 1，4,473 次窗口检查，没有发现可见窗口。日志和失败截图位于 `Builds/QA-Packaged-fo_sy5q9/`；这是失败诊断，未生成通过证据清单。

## 同一 ZIP：30 分钟离屏耐久

- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --local-manual-resume-screenshots --soak-seconds 1800` 退出码为 0。证据目录 `Builds/QA-Packaged-c8ezjs2z/`：38 张 1280×720 PNG（含 7 个耐久检查点），62,478 次窗口采样均未发现可见窗口。`verify-evidence` 按同一 ZIP 核验 42 个文件的完整性。
- `live-soak.txt`：连续绿幕 **1,800.02 秒**，平均 **39.20 FPS**，通过 ≥1,800 秒与平均 ≥30 FPS 两个离屏门槛。P95 帧间隔 29 ms，最长 411.3 ms；均值达标不意味着全程无卡顿。
- 日志记录 70,559 次离屏绘制，检测中断 1 次、恢复 1 次；181 次工作集采样，初值／末值／采样峰值分别为 408.3／422.2／422.2 MiB。单次工作集变化不能证明有或没有内存泄漏。
- 同轮确认 `XR_PLAYER_SOAK_OK`、`XR_PLAYER_MANUAL_RESUME_OK`、`XR_PLAYER_PET_RESTORE_OK`。耐久使用本地人像／空白帧做 CPU 推理和 URP 离屏绘制，不代表真人面捕、桌面呈现或后台跟踪。

未使用实体摄像头或 OBS。实际鼠标键盘、30 分钟真人面捕与第二台 Windows 电脑未验收。

