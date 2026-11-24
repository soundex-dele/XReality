# 0.3.19 无窗口验收记录

- 打包并校验 `Builds/XReality-0.3.19-Windows-x64-portable-preview.zip`；SHA-256 `92f1ad76dc1150ccbf4eba8b552d1130e7ec43780325dadbb35c10cc9fe41eff`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --soak-seconds 30` 通过。30 张 1280×720 PNG，2,604 次窗口可见性采样未发现可见窗口；证据 `Builds/QA-Packaged-9r_20le7/`。
- 短耐久测试：CPU 推理 879 帧，实际离屏持续渲染 30.01 秒，均速 29.25 FPS，P95 帧间隔 39 ms；81 帧空白输入，离屏绿幕从追踪切到待机并重新恢复。非实体摄像头，不代表桌面呈现帧率。测试没有达成严格 30 FPS 平均值。应用自己的工作集采样返回 0 项，应进一步诊断。
- 30 分钟测试已启动：`python Tools/project.py packaged-screenshots --offline-tracking --soak-seconds 1800`，证据 `Builds/QA-Packaged-pltxzztd/`。未结束前不计通过；另从系统每十秒采样该进程的工作集，不替代应用自身诊断。
- 中途检查（300.01 秒）：第 2 个离屏检查点截图正常，累计 8,894 次渲染、8,894 次识别；系统侧当时已有 30 个工作集样本，418.7–419.4 MiB。仅为中途观察。
- 中途检查（602.03 秒）：第 3 个检查点通过，空白输入期间化身回到待机，且没有输出原始测试人像。累计 17,826 次渲染；系统侧 61 个样本，418.7–419.9 MiB。这是丢脸检查点，与下列恢复检查点分开。
- 额外的静态包校验：`python Tools/project.py verify-package --package Builds/XReality-0.3.19-Windows-x64-portable-preview.zip` 通过。校验 SHA-256、CRC、180 个文件、8 个原生 AMD64 二进制、三场景数据、第三方声明及 Unity 应用身份；此命令未启动应用。
- 中途检查（900.01 秒）：第 4 个检查点通过，空白输入结束后恢复跟踪，累计 26,697 次渲染、26,638 次检测到人像及 59 次空白检测；系统侧当时共 93 个样本，已观测峰值 419.9 MiB。整轮尚未结束。

不包含真实输入、实体摄像头/30 分钟真人跟踪或第二台干净 Windows 机器。未使用 OBS。



