# 0.3.16 无窗口验收记录

- 对象：`Builds/XReality-0.3.16-Windows-x64-portable-preview.zip`。SHA-256 实算与旁签一致：`51635721aa4ad09c2d617efb2ddc3e3fe9519e7432f1f04d786804a880bce26f`。
- 解包版运行 `python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --retry-screenshots` 退出码 0。23 张 1280×720 PNG；1,535 次窗口可见性采样未发现可见窗口。日志和 PNG：`Builds/QA-Packaged-smyi_imt/`。
- `XR_PLAYER_MODEL_RANGE_OK`：使用本地测试模型在 2,000,017 字节处分段；拒绝缺失或不匹配的 Content-Range；拒绝短分段且不修改前段；拼接后通过 SHA-256，直播间显示模型就绪。这个离线用例不证明服务器返回 HTTP 206。
- 同轮通过：两个首页入口、宠物互动和跨进程存档、离线人像推理及丢失恢复、绿幕/控制台隔离，模拟断线后的重试与失败回退。
- 另跑 `python Tools/project.py packaged-screenshots --download-screenshots` 通过：11 张 1280×720 PNG，1,651 次窗口可见性采样均未发现可见窗口。取消后清理临时文件；重新发起后第一次请求无字节到达，第二次下载了完整文件并通过 SHA-256。见 `Builds/QA-Packaged-omxk080v/` 和 `XR_MODEL_DOWNLOAD_FULL`。此次未观察到真实 HTTP 206，不能作为服务器续传实测。

未测试实体摄像头、鼠标键盘、30 分钟真人面捕或者第二台 Windows 机器。不使用 OBS。

