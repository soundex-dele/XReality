# 0.3.23 无窗口验收记录

- ZIP：`Builds/XReality-0.3.23-Windows-x64-portable-preview.zip`；SHA-256 `ad45fc3d7600b851279eebf872440ef7f69211c31cfb3eef982cb7eb9d5ef1aa`。截图运行前检查了 SHA-256、CRC 与 Windows x64 依赖。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 通过：27 张 1280×720 PNG、1,593 次窗口可见性采样均未发现可见窗口。`Builds/QA-Packaged-n1caey9w/evidence.json` 中 29 个文件完整性复核通过。
- `--manual-resume-screenshots` 第一次未通过最终安装：测试在最后一次自动请求拿到 394,590 字节后主动中断。「继续下载模型」出现，点击后确认真实 HTTP 206，实际又收到 1,238,675 字节并保留合计 1,633,265 字节；剩余请求超时，未安装未验证模型。4,626 次窗口采样未发现可见窗口。截图、日志见 `Builds/QA-Packaged-h__uojn6/`。
- 第二次联网复跑未取得可续传的数据，未完成最终安装；1,026 次窗口采样未发现可见窗口，见 `Builds/QA-Packaged-0_0urqoe/`。上述真实字节追加不是端到端安装成功，手动安装与 SHA-256 检查已在离线回归验证。

未启用实体相机或 OBS。系统鼠标键盘操作、30 分钟真人摄像头和第二台 Windows 电脑尚未验收。

