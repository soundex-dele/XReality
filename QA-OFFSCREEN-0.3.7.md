# 0.3.7 无窗口验收记录

- Windows x64 便携预览 ZIP：`Builds/XReality-0.3.7-Windows-x64-portable-preview.zip`
- SHA-256：`95ca092bec4bf348401b216a5edd03c2252f6c6a9f591645667b26fed8855bfc`
- 复现命令：`python Tools/project.py packaged-screenshots --offline-tracking --pet-interaction`
- 证据目录：`Builds/QA-Packaged-5ndolr1r/`。两次启动，共 16 张 1280×720 图；1,361 次窗口检查未检测到可见窗口。

| 已验证 | 证据 |
|---|---|
| 包内程序加载首页、宠物房间、直播间 | `Home.png`、`PetRoom.png`、`LiveStudio.png` |
| 初始直播控制面板显示；纯绿幕清除面板；退出恢复 | `LiveStudio.png`、`LiveOutput.png`、`LiveControlsRestored.png`；前后面板区域像素一致 |
| 本地原生推理驱动角色、空白时回待机、人像再入时恢复 | `LiveTracked.png`、`LiveTrackingLost.png`、`LiveTrackingRecovered.png`；只输出化身 |
| 宠物打盹动耳、醒时蹭手、先关注再追球、隔离存档重启后主动访窗 | `Pet*.png`、`player.log`、`player-pet-restore.log` |

未验证：实际鼠标和键盘、已安装模型后的控件交互、网络下载、真实摄像头及 30 分钟运行、另一台 Windows 电脑。此记录不把离屏验收当作这些项目的替代。
