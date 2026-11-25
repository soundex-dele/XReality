# XReality 3DGS 主播预览（Windows / D3D11）

这是第一阶段的**管线原型**。Unity 编辑器把项目已有的 MakeHuman/MPFB CC0 主播烘焙成预制高斯资产；它不是多视角训练出来的真人复刻，也不是 GaussianAvatars/FLAME 导入器。普通静态 `.ply` 没有表情绑定，不能直接替换。素材来源见 [AVATAR-ASSETS.md](AVATAR-ASSETS.md)，不需要下载第三方研究样例或采集真人。

## 构建和直播预演

1. 按 [README.md](README.md) 准备 Unity 6000.3.10f1 和项目依赖。打开 Unity，选择 **XReality → Build Windows 3DGS preview**；它先离线烘焙，再输出到 `Builds/3DGS-Preview/Windows/`，不覆盖现有便携包。只想生成资产时用 **XReality → Bake 3DGS sample**。生成资产位于忽略提交的 `Assets/XReality/Generated/StorybookV19/Resources/XReality/`；留在目录时也会被标准构建收录，但不会默认启用。
2. 从仓库根目录执行 `& .\Builds\3DGS-Preview\Windows\XReality.exe -force-d3d11 -avatar-3dgs -screen-width 1920 -screen-height 1080`，再进入直播间。此目录是本地开发构建，不是便携 ZIP。当前工作区的预览已放入通过 SHA-256 校验的面捕模型；重新构建后若缺模型，按直播间提示安装。不加 `-avatar-3dgs` 时仍是原有网格主播。模型未装不影响角色预览，但无法使用摄像头面捕。
3. 手动启用摄像头，正视镜头校准；按 **F8** 进入纯绿幕，**Esc** 返回，**F9** 停止摄像头。3DGS 和原主播共用面捕、表情校准和绿幕控制；摄像头画面不是渲染输入，麦克风另接 OBS。查看 Unity Player 日志：`XR_3DGS_ACTIVE` 表示启用；`XR_3DGS_FALLBACK` 表示缺资产、不匹配、不支持或渲染更新失败，已保留/恢复网格主播。
4. OBS 先按 **1920×1080 / 30 fps** 本地录制，捕获 XReality 窗口、用色度键抠绿，再加独立麦克风。实际确认捕获源为 1920×1080，而不是把 720p 放大。连续讲话、眨眼、微笑、转头及短时遮挡 30 分钟；记录掉帧、起止音画偏移、边缘绿溢、眼睑和牙齿穿漏、丢脸后的中性回退与重捕。先验收再开播；若有绿边、细节丢失或性能不足，去掉启动参数切回网格对照。

## 无摄像头回归

关闭占用同项目的 Unity 窗口后，在仓库根目录运行（按实际安装位置修改 `$unity`）：

```powershell
$unity = 'D:\Programs\Unity\6000.3.10f1\Editor\Unity.exe'
$project = (Get-Location).Path
Start-Process $unity -ArgumentList "-batchmode -quit -force-d3d11 -projectPath `"$project`" -executeMethod XReality.Editor.GaussianPresenterSnapshots.Run -logFile `"$project\Builds\unity-3dgs-qa.log`"" -WindowStyle Hidden -Wait
```

编辑器在 `Builds/QA-3DGS/` 生成五张 1280×720 离屏截图，检查眼、嘴、头部区域的像素变化、绿幕、缺资产回退和资源清理。检查日志 `XR_3DGS_QA_OK`。在上述预览构建上可再跑独立的 Player 离屏检查：

```powershell
$project = (Get-Location).Path
Start-Process "$project\Builds\3DGS-Preview\Windows\XReality.exe" -ArgumentList "-batchmode -force-d3d11 -avatar-3dgs --xr-offscreen `"$project\Builds\QA-3DGS-Player`" -logFile `"$project\Builds\unity-3dgs-player.log`"" -WindowStyle Hidden -Wait
```

检查 `XR_PLAYER_3DGS_OK` 和 `XR_PLAYER_OFFSCREEN_OK`。Player 用合成 `FacePose` 走实际表情映射、绿幕切换及中性恢复，不启动摄像头或 OBS。两种检查都不能作为 1080p/30 fps 或音画同步的证据。

## 本机验证结果（2026-11-25）

- Unity 编辑器离屏通过：7 个部件、46,615 个高斯点；眨眼、张嘴、微笑、转头均产生可测像素变化。
- Windows 构建离屏通过：合成眨眼、张嘴和转头可见，中性恢复后对照区域无变化；绿幕切换通过，未启用摄像头。不开 3DGS 的默认网格回归也通过。
- 尚未验收：实机摄像头＋OBS 同时运行的帧率、音画同步与抠绿质量。当前烘焙样例的眼周和边缘偏软，不能当作真人重建效果。

## 资产和下一阶段

资产记录原网格、三角形顶点索引、重心坐标、颜色、不透明度与尺度。播放时用既有蒙皮和混合形状定位高斯中心及局部轴，按相机深度排序，在 URP 中投影成椭圆并混合。预览使用烘焙颜色，无球谐视角颜色、训练器、真人模型转换、全身动捕或动态光照；CPU 蒙皮、排序和上传需要用目标直播机实测。

预演通过后，再采集获授权的真人并选择可商用的可驱动重建方案。设计转换器时必须保留对应的变形网格、表情映射及高斯的三角形绑定，重新验收侧脸、眨眼、牙齿和绿幕；不要只导入一份静态点云。
