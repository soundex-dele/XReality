using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XReality
{
    // The same retained controls are drawn on-screen and by the offscreen camera.
    internal sealed class LiveDeskView : IDisposable
    {
        static readonly Color Ink = new Color32(18, 30, 38, 255);
        static readonly Color Card = new Color32(29, 47, 57, 255);
        static readonly Color TextColor = new Color32(246, 249, 246, 255);
        static readonly Color Muted = new Color32(187, 206, 210, 255);
        static readonly Color Accent = new Color32(165, 230, 207, 255);
        readonly Canvas canvas;
        readonly Camera camera;
        readonly bool interactive;
        readonly Font font;
        readonly RectTransform panel;
        readonly GameObject eventRoot;
        readonly Text status, device, section, notice, downloadTitle;
        readonly Button download, browser, folder, refresh, cancel, cycle, enable, calibrate, stop, report, clean, home;
        readonly List<Button> navigation = new List<Button>();

        internal string DeviceLabel => device.text;
        internal string StatusLabel => status.text;
        internal string DownloadTitle => downloadTitle.text;
        internal bool OffersDownload => download.gameObject.activeInHierarchy && download.interactable;
        internal bool OffersCancel => cancel.gameObject.activeInHierarchy && cancel.interactable;
        internal void ClickDownload() { if (OffersDownload) download.onClick.Invoke(); }
        internal void ClickCancel() { if (OffersCancel) cancel.onClick.Invoke(); }
        internal bool OffersCamera => enable.gameObject.activeSelf;
        internal bool CanStartCamera => OffersCamera && enable.interactable;
        internal bool Visible { get => canvas.gameObject.activeSelf; set => canvas.gameObject.SetActive(value); }

        internal LiveDeskView(Camera camera, Transform owner, bool interactive,
            Action startDownload, Action openDownload, Action openFolder, Action refreshModel, Action cancelDownload,
            Action cycleDevice, Action enableCamera, Action calibrateFace, Action stopCamera, Action exportReport,
            Action cleanOutput, Action goHome)
        {
            this.camera = camera;
            this.interactive = interactive;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun", "Segoe UI" }, 22);
            var root = new GameObject("Live controls", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(owner, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .18f;
            canvas.sortingOrder = 10;
            if (interactive)
            {
                root.AddComponent<GraphicRaycaster>();
                if (!EventSystem.current)
                {
                    eventRoot = new GameObject("Live control input", typeof(EventSystem), typeof(StandaloneInputModule));
                    eventRoot.transform.SetParent(owner, false);
                }
            }
            panel = Box("Control panel", (RectTransform)root.transform, 24, 24, 352, 672);
            panel.gameObject.AddComponent<Image>().color = Ink;
            ImageAt(panel, "Accent rule", 0, 0, 352, 3, Accent);
            Label(panel, "XREALITY   /   02  ·  LIVE STUDIO", 22, 22, 308, 22, 13, Accent, FontStyle.Bold);
            Label(panel, "化身直播间", 22, 55, 308, 43, 33, TextColor, FontStyle.Bold);
            Label(panel, "摄像头需手动开启。画面仅在本机处理，不保存，也不上传。", 22, 108, 308, 51, 17, Muted);
            ImageAt(panel, "Status card", 22, 174, 308, 103, Card);
            Label(panel, "设备状态", 34, 181, 284, 20, 12, Accent, FontStyle.Bold);
            status = Label(panel, "", 34, 205, 284, 40, 16, TextColor);
            device = Label(panel, "", 34, 248, 284, 18, 13, Muted);
            device.horizontalOverflow = HorizontalWrapMode.Wrap;
            section = Label(panel, "", 22, 288, 308, 24, 15, Muted, FontStyle.Bold);

            download = Control("从 Google 下载模型", 22, 323, 308, startDownload, true);
            downloadTitle = download.GetComponentInChildren<Text>(true);
            browser = Control("浏览器下载", 22, 374, 149, openDownload);
            folder = Control("打开模型目录", 181, 374, 149, openFolder);
            refresh = Control("检查手动安装的模型", 22, 425, 308, refreshModel);
            cancel = Control("取消下载", 22, 323, 308, cancelDownload);
            cycle = Control("切换 / 刷新设备", 22, 323, 308, cycleDevice);
            enable = Control("启用摄像头", 22, 374, 149, enableCamera, true);
            stop = Control("关闭摄像头 · F9", 181, 374, 149, stopCamera);
            calibrate = Control("正视镜头 · 校准", 22, 425, 308, calibrateFace);
            report = Control("导出本次性能报告", 22, 476, 308, exportReport);
            notice = Label(panel, "", 22, 476, 308, 49, 15, Muted);
            ImageAt(panel, "Footer rule", 22, 535, 308, 1, Card);
            clean = Control("进入纯绿幕 · F8", 22, 551, 308, cleanOutput);
            home = Control("返回首页", 22, 603, 308, goHome);
            Label(panel, "Esc 退出绿幕 · F9 关闭相机 · Tab 切换", 22, 648, 308, 15, 12, Muted);
            Canvas.preWillRenderCanvases += Fit;
            Fit();
        }

        void Fit()
        {
            var target = camera.targetTexture;
            float scale = Mathf.Max(.1f, Mathf.Min((target ? target.width : Screen.width) / 1280f,
                (target ? target.height : Screen.height) / 720f));
            if (!Mathf.Approximately(canvas.scaleFactor, scale)) canvas.scaleFactor = scale;
        }

        internal void Update(bool ready, bool busy, bool canResume, string message, string deviceName,
            bool active, bool canStart, bool canCalibrate, bool canReport, string exportMessage)
        {
            Set(status, message);
            Set(device, ready ? deviceName : "模型设置 · 安装后可离线使用");
            Set(section, ready ? "摄像头控制" : busy ? "模型下载" : "首次设置");
            Set(downloadTitle, canResume ? "继续下载模型" : "从 Google 下载模型");
            Show(download, !ready && !busy);
            Show(browser, !ready && !busy);
            Show(folder, !ready && !busy);
            Show(refresh, !ready && !busy);
            Show(cancel, busy);
            Show(cycle, ready, !active);
            Show(enable, ready, canStart);
            Show(stop, ready, active);
            Show(calibrate, ready, canCalibrate);
            Show(report, ready && canReport && string.IsNullOrEmpty(exportMessage));
            string details = exportMessage;
            if (!ready)
                details = busy ? "可取消；下载完成后会校验本地文件。" : canResume
                    ? "返回首页会清除本次临时下载。" : "手动安装后，点击「检查手动安装的模型」。";
            else if (string.IsNullOrEmpty(details) && !canReport && !active)
                details = canStart ? "启用后，请正视镜头完成校准。" : "连接摄像头后，点击「切换 / 刷新设备」。";
            Set(notice, details);
        }

        internal void Tab(bool reverse)
        {
            var es = EventSystem.current;
            if (!es || !Visible) return;
            int current = navigation.FindIndex(b => b.gameObject == es.currentSelectedGameObject);
            if (current < 0 && reverse) current = 0;
            for (int i = 1; i <= navigation.Count; i++)
            {
                var button = navigation[(current + (reverse ? -i : i) + navigation.Count * 2) % navigation.Count];
                if (!button.gameObject.activeInHierarchy || !button.interactable) continue;
                es.SetSelectedGameObject(button.gameObject);
                return;
            }
        }

        static void Show(Button button, bool show, bool interactable = true)
        {
            if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
            if (button.interactable != interactable) button.interactable = interactable;
        }

        static void Set(Text label, string value)
        {
            value ??= "";
            if (label.text != value) label.text = value;
        }

        static RectTransform Box(string name, RectTransform parent, float x, float y, float width, float height)
        {
            var node = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            node.SetParent(parent, false);
            node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1);
            node.anchoredPosition = new Vector2(x, -y);
            node.sizeDelta = new Vector2(width, height);
            return node;
        }

        static Image ImageAt(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Box(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text Label(RectTransform parent, string text, float x, float y, float w, float h, int size, Color color,
            FontStyle style = FontStyle.Normal)
        {
            var label = Box("Label", parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        Button Control(string text, float x, float y, float w, Action action, bool primary = false)
        {
            var image = ImageAt(panel, text, x, y, w, 43, Color.white);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = primary ? Accent : Card;
            colors.highlightedColor = primary ? new Color32(193, 250, 228, 255) : new Color32(56, 85, 95, 255);
            colors.pressedColor = primary ? new Color32(119, 202, 179, 255) : new Color32(43, 65, 78, 255);
            colors.selectedColor = primary ? new Color32(218, 254, 238, 255) : new Color32(74, 102, 113, 255);
            colors.disabledColor = primary ? new Color32(111, 144, 139, 255) : new Color32(41, 54, 61, 255);
            if (!interactive) colors.fadeDuration = 0; // Batch visual assertions sample button states immediately.
            button.colors = colors;
            var label = Label((RectTransform)image.transform, text, 6, 0, w - 12, 43, 16, primary ? Ink : TextColor);
            label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => action());
            navigation.Add(button);
            return button;
        }

        public void Dispose()
        {
            Canvas.preWillRenderCanvases -= Fit;
            if (eventRoot) UnityEngine.Object.Destroy(eventRoot);
            if (canvas) UnityEngine.Object.Destroy(canvas.gameObject);
            if (font) UnityEngine.Object.Destroy(font);
        }
    }
}
