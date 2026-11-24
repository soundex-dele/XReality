# 0.3.9 无窗口验收记录

- ZIP：`Builds/XReality-0.3.9-Windows-x64-portable-preview.zip`
- SHA-256：`1d9ec5db69099092e583beab810859e55bca5200f1498c117e8c7a0d68ecf3b5`
- 复现：`python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --download-screenshots`
- 证据：`Builds/QA-Packaged-u93fd_2v/`；两次启动，共 22 张 1280×720 PNG，1,567 次可见窗口检查均未发现窗口。

在线步骤通过直播面板真实按钮的回调触发，而非注入桌面输入。下载进入进度态，取消后进度消失、重试可用且没有残留部分文件；再次从官方地址下载、校验并安装成功，未启用摄像头，临时缓存随后清理。对应 `LiveModelDownloading.png`、`LiveModelDownloadCancelled.png`、`LiveModelDownloadInstalled.png` 及 `XR_PLAYER_DOWNLOAD_SCREENSHOTS_OK` 日志。其余通过项为手动安装校验、绿幕 UI 隔离、公开人像的离线推理与恢复、猫咪动作和跨进程恢复。

仍未验证真实鼠标和键盘输入、物理摄像头及 30 分钟稳定性、第二台 Windows 设备兼容性。无需 OBS。
