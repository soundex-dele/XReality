using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XReality
{
    // Rendered by the room camera so the shipped UI can be verified without desktop pixels.
    internal sealed class PetDeskView : IDisposable
    {
        static readonly Color32 Ink = new Color32(24, 34, 36, 255);
        static readonly Color32 Card = new Color32(44, 60, 59, 255);
        static readonly Color32 Paper = new Color32(250, 247, 238, 255);
        static readonly Color32 Muted = new Color32(204, 218, 208, 255);
        static readonly Color32 Accent = new Color32(232, 199, 143, 255);
        readonly Camera camera;
        readonly Canvas canvas;
        readonly RectTransform panel;
        readonly Font font;
        readonly Text heading, activity, bond, favorite, feedback;
        readonly InputField nameField;
        readonly Button save;
        readonly Selectable[] navigation;

        internal string Feedback => feedback.text;
        internal string Activity => activity.text;
        internal string Favorite => favorite.text;
        internal bool NameSelected => EventSystem.current && EventSystem.current.currentSelectedGameObject == nameField.gameObject;

        internal PetDeskView(Camera camera, Transform owner, bool interactive, string currentName,
            Action stroke, Action roll, Action<string> rename, Action home)
        {
            this.camera = camera;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun", "Segoe UI" }, 20);
            var root = new GameObject("Pet controls", typeof(RectTransform), typeof(Canvas));
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
                    var events = new GameObject("Pet control input", typeof(EventSystem), typeof(StandaloneInputModule));
                    events.transform.SetParent(root.transform, false);
                }
            }
            panel = Box(root.transform, "Pet room panel", 24, 24, 320, 672);
            Paint(panel, Ink, true);
            Paint(Box(panel, "Top accent", 0, 0, 320, 3), Accent);
            Label(panel, "XREALITY   /   01  ·  PET ROOM", 20, 20, 280, 24, 13, Accent, true);
            heading = Label(panel, currentName + "的小窝", 20, 52, 280, 62, 29, Paper, true);
            heading.resizeTextForBestFit = true;
            heading.resizeTextMinSize = 16;
            heading.resizeTextMaxSize = 29;
            Label(panel, "点猫咪摸摸它，拖动毛线球逗它玩。", 20, 118, 280, 46, 16, Muted);

            var status = Box(panel, "Pet activity card", 20, 173, 280, 113);
            Paint(status, Card);
            Label(status, "此刻", 12, 9, 130, 20, 13, Accent, true);
            bond = Label(status, "", 140, 9, 128, 20, 13, Accent, true);
            bond.alignment = TextAnchor.MiddleRight;
            activity = Label(status, "", 12, 35, 256, 35, 17, Paper);
            favorite = Label(status, "", 12, 75, 256, 27, 14, Muted);

            var touch = Button("摸摸它 · P", 302, stroke, true);
            var toy = Button("滚动毛线球 · T", 352, roll);
            Label(panel, "猫咪名字", 20, 410, 280, 24, 14, Accent, true);
            var nameBox = Box(panel, "Pet name input", 20, 441, 280, 44);
            var nameBackground = Paint(nameBox, Card, true);
            nameField = nameBox.gameObject.AddComponent<InputField>();
            nameField.targetGraphic = nameBackground;
            nameField.textComponent = Label(nameBox, "", 11, 0, 258, 44, 17, Paper);
            nameField.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameField.characterLimit = 24;
            nameField.lineType = InputField.LineType.SingleLine;
            nameField.customCaretColor = true;
            nameField.caretColor = Paper;
            nameField.selectionColor = new Color(.50f, .44f, .26f, .78f);
            nameField.text = currentName;
            SetColors(nameField, false);
            feedback = Label(panel, "名字与亲密度仅保存在本机。", 20, 535, 280, 38, 14, Muted);
            nameField.onValueChanged.AddListener(_ => feedback.text = "点击保存名字，提交修改。");
            save = Button("保存名字", 491, () => rename(nameField.text));
            Paint(Box(panel, "Footer rule", 20, 582, 280, 1), Card);
            var back = Button("返回首页", 591, home);
            Label(panel, "Esc 返回 / 退出编辑 · Tab 切换控件", 20, 643, 280, 17, 12, Muted);
            navigation = new Selectable[] { touch, toy, nameField, save, back };
            Canvas.preWillRenderCanvases += Fit;
            Fit();
        }

        internal void Render(Mood mood, int points, PlayMemory.Zone zone)
        {
            Set(activity, mood switch
            {
                Mood.Sleep => "趴在地毯上打盹。",
                Mood.EarFlick => "动了动耳朵，还在打盹。",
                Mood.Nuzzle => "蹭了蹭你的手。",
                Mood.Notice => "盯住了毛线球，准备追。",
                Mood.Chase => "正在追毛线球！",
                _ => "正看着你。"
            });
            Set(bond, "亲密度  " + points);
            Set(favorite, zone switch
            {
                PlayMemory.Zone.Window => "喜欢在窗边玩",
                PlayMemory.Zone.Rug => "喜欢在地毯上玩",
                _ => "多陪它追球，看看它偏爱哪里。"
            });
        }

        internal void NameResult(string name, bool saved)
        {
            if (saved)
            {
                nameField.SetTextWithoutNotify(name);
                heading.text = name + "的小窝";
            }
            feedback.text = saved ? "已保存名字。" : "请输入一个非空名字。";
        }

        internal void SubmitName(string text)
        {
            nameField.text = text;
            save.onClick.Invoke();
        }

        internal void LeaveName()
        {
            nameField.DeactivateInputField();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        internal void Tab(bool reverse)
        {
            var parser = EventSystem.current;
            if (!parser) return;
            int selected = Array.FindIndex(navigation, selectable => selectable.gameObject == parser.currentSelectedGameObject);
            int next = selected < 0 ? reverse ? navigation.Length - 1 : 0 :
                (selected + (reverse ? -1 : 1) + navigation.Length) % navigation.Length;
            parser.SetSelectedGameObject(navigation[next].gameObject);
            if (navigation[next] == nameField) nameField.ActivateInputField();
        }

        internal bool Contains(Vector2 position) => RectTransformUtility.RectangleContainsScreenPoint(panel, position, camera);

        void Fit()
        {
            var target = camera.targetTexture;
            float scale = Mathf.Max(.1f, Mathf.Min((target ? target.width : Screen.width) / 1280f,
                (target ? target.height : Screen.height) / 720f));
            if (!Mathf.Approximately(canvas.scaleFactor, scale)) canvas.scaleFactor = scale;
        }

        Button Button(string text, float y, Action action, bool primary = false)
        {
            var rect = Box(panel, text, 20, y, 280, 44);
            var image = Paint(rect, Color.white, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            SetColors(button, primary);
            var label = Label(rect, text, 10, 0, 260, 44, 16, primary ? Ink : Paper, true);
            label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => action());
            return button;
        }

        static void SetColors(Selectable control, bool primary)
        {
            var colors = control.colors;
            colors.normalColor = primary ? Accent : Card;
            colors.highlightedColor = primary ? new Color32(249, 219, 165, 255) : new Color32(63, 82, 79, 255);
            colors.selectedColor = primary ? new Color32(255, 228, 182, 255) : new Color32(73, 96, 92, 255);
            colors.pressedColor = primary ? new Color32(221, 184, 123, 255) : new Color32(58, 75, 73, 255);
            colors.fadeDuration = .08f;
            control.colors = colors;
            control.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        static void Set(Text label, string value)
        {
            if (label.text != value) label.text = value;
        }

        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        static Image Paint(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color, bool bold = false)
        {
            var label = Box(parent, "Label", x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        public void Dispose()
        {
            Canvas.preWillRenderCanvases -= Fit;
            if (canvas) UnityEngine.Object.Destroy(canvas.gameObject);
            if (font) UnityEngine.Object.Destroy(font);
        }
    }
}
