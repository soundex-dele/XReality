# 0.3.22 无窗口验收记录

- ZIP：`Builds/XReality-0.3.22-Windows-x64-portable-preview.zip`，SHA-256 `8419ae3dc42614adae11c52e9beb6c8f4c82689d34ff6bf2b358da302df6a718`。静态发布包校验通过，181 个文件，8 个原生 AMD64 二进制；包内新增 `WINDOWS-PREREQUISITES.md`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots --soak-seconds 30` 通过：34 张 1280×720 PNG、2,998 次窗口检查均未发现可见窗口。`Builds/QA-Packaged-3l0zg06_/evidence.json` 的 37 个媒体/日志/报告文件已通过完整性复核。
- 模拟面捕加载时抛出包装的 `DllNotFoundException`，界面提示 VC++ x64 运行库和 Windows 媒体功能，同时保持正确模型就绪。不把模型损坏或摄像头 I/O 错误混为一类。证据：`XR_PLAYER_SETUP_ERRORS_OK` 和 `LiveDependencyMissing.png`。未修改或卸载系统组件，因此不代表在缺失这些组件的第二台电脑上实测过。
- 0.3.22 连续离屏短测 30.02 秒，平均 39.38 FPS，应用内工作集正常采样。0.3.21 的 1,800.01 秒、37.94 FPS 长测是独立证据，不能直接记作 0.3.22 长测通过。
- `--resume-screenshots` 联网实测两次均未通过：第一次一轮连接重置，下一轮下载到 2,575,248 / 3,758,596 字节后到达 90 秒上限（3,769 次窗口采样）；第二次两轮均没有收到数据（1,320 次窗口采样）。未发现可见窗口，正确显示浏览器下载回退提示；证据 `Builds/QA-Packaged-j4tg3nk1/` 和 `Builds/QA-Packaged-ua23ice2/`。0.3.21 的线上续传结果不替代本版实测。

所有应用测试均无窗口；未启用实体摄像头或 OBS。实际输入、不同电脑以及 30 分钟真人面捕仍未验证。

