# 0.3.11 无窗口验收记录

- 预览 ZIP 的 SHA-256：`c8cd025519eb14f9277ae176e0afe3b5e09c6982bcc5b1ca2c70f8bcd1a2ce9f`。
- 复现：`python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction --download-screenshots`。
- 证据：`Builds/QA-Packaged-yo2apnen/`；两次启动，22 张 1280×720 PNG，1,566 次窗口检查未发现可见窗口。
- 在线流程：开始下载后取消，确认部分文件清除；再次点击下载后从官方地址下载、校验和安装成功。`Curl error 42: Callback aborted` 与第一次手动取消相符。第二次请求没有记录自动重试，故本次不声称验证了自动重试分支。
- 其余通过项：手动安装校验、绿幕面板隔离、公开人像离线推理和恢复、宠物动作和跨进程存档。

未覆盖实际鼠标键盘、物理摄像头及连续 30 分钟稳定性、第二台 Windows 电脑。未启用 OBS。
