# XReality · Windows 运行环境

先把 ZIP **完整解压**到同一个目录，再双击 `XReality.exe`。仅复制 EXE 无法运行；不要从 DLL 下载站单独下载文件或替换包内 DLL。应用不需要安装 Unity Editor 或 Unity Hub。

## 首页与宠物房间可以打开，但面捕无法启动

Windows x64 版的面捕组件 `mediapipe_c.dll` 直接依赖 Microsoft Visual C++ x64 运行库和 Windows Media Foundation。ZIP 不会自动安装这两项。是否已具备取决于这台电脑的 Windows 配置。

1. 确认面捕模型安装界面显示「面捕模型已就绪」。没有模型时，先用应用里的下载或手动安装流程。
2. 如果提示原生依赖加载失败，或已安装模型但无法初始化，使用微软官方 [Visual C++ Redistributable 下载页](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist) 中的 **x64** 安装包。安装受限时请找电脑管理员，不必以管理员身份运行 XReality。
3. 若 Windows 版本名称带 **N**，检查「可选功能」中是否安装 **Media Feature Pack**。参见微软 [Windows N 的 Media Feature Pack](https://support.microsoft.com/en-us/windows/media-feature-pack-for-windows-n-8622b390-4ce6-43c9-9b42-549e5328e407)，安装后按系统提示重启。
4. 重新打开应用；模型已就绪但仍没有摄像头时，查看 Windows 的摄像头隐私设置、设备连接和是否被其他应用占用。这是不同于模型和运行库的问题。

## 给 Windows 验收者

在未安装 Unity 的另一台电脑上，先记录 ZIP SHA-256、Windows 版本（含是否为 N 版）和是否已有运行库。若首次失败且安装微软组件后成功，把两个结果分开记录；不要把依赖补齐后的通过写成原始环境开箱通过。测试不用 OBS、不上传或提交相机画面。详细步骤见 `QA-WINDOWS.md`。
