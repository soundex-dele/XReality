# 0.3.15 无窗口验收记录

- 测试对象：`Builds/XReality-0.3.15-Windows-x64-portable-preview.zip`。实算 SHA-256 与校验文件一致：`43b6291530aab475c48270c628a82ae4ccc95884dc1e1be32a3a22ed69162be3`。
- `python Tools/project.py screenshots --unity <Unity.exe>` 通过：本次编辑器批处理记录 15 张 1280×720 PNG，覆盖房间和猫的关键动作位、独立左右眨眼、半闭眼、张嘴与绿幕构图。产物 `Builds/QA-Offscreen/`，日志 `Builds/unity-offscreen-expressions.log`。
- `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 通过：从 ZIP 解包运行，23 张 1280×720 PNG，1,590 次采样未发现可见窗口。证据目录 `Builds/QA-Packaged-2qzor91w/`。通过首页展示和按钮路由、宠物动作及跨进程存档、离线推理与恢复、绿幕 UI 隔离、手动模型安装和模拟网络异常重试。
- 首页实图：`Builds/QA-Packaged-2qzor91w/Home.png`。猫和化身使用与对应场景共享的美术资产；没有宠物、面捕或直播组件，也没有展示物的交互碰撞体。人物区域前景像素分别为 31,821 和 27,456。
- 首次带 `--download-screenshots` 的联网运行没有通过：一轮停滞，随后一次传到 3,494,135 / 3,758,596 字节触发 60 秒上限。程序显示回退提示，保留 `Builds/QA-Packaged-txno869o/LiveModelDownloadFailed.png` 与主日志；本次测试共采样 4,344 次，未发现可见窗口。离线的手动模型安装结果不受影响。

- 单独复跑在线场景 `python Tools/project.py packaged-screenshots --download-screenshots` 仍未完成：两次进度预算到期，2,021 次采样未发现可见窗口。失败截图和日志在 `Builds/QA-Packaged-p7c87ajd/`。0.3.14 的下载成功不能代替本版实测。

不含实体相机、实际鼠标键盘、30 分钟真人面捕或第二台 Windows 机器。未使用 OBS。

