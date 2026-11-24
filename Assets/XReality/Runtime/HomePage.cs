using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace XReality
{
    public sealed class HomePage : DeskPanel
    {
        static readonly Color Paper = new Color32(244, 247, 241, 255);
        static readonly Color Muted = new Color32(184, 206, 210, 255);
        static readonly Color Ink = new Color32(18, 30, 38, 255);
        static readonly Color Card = new Color32(36, 57, 66, 255);
        static readonly Color Leaf = new Color32(165, 230, 207, 255);
        static readonly Color Peach = new Color32(241, 188, 148, 255);
        Canvas canvas;
        Font font;
        Button pet, live;
        Slider audioLevel;
        AsyncOperation opening;
        internal AsyncOperation Opening => opening;

        void Start() => ShowDesk(true);

        internal void ShowDesk(bool interactive)
        {
            if (canvas) return;
            var root = new GameObject("Home controls", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.worldCamera = Camera.main;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = canvas.worldCamera.nearClipPlane + .18f;
            canvas.sortingOrder = 10;
            if (interactive)
            {
                root.AddComponent<GraphicRaycaster>();
                if (!EventSystem.current)
                    new GameObject("Home control input", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);
            }
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun", "Segoe UI" }, 22);
            var panel = Block(root.transform, "Home panel", 28, 27, 450, 666, Ink).rectTransform;
            Block(panel, "Rule", 0,0,450,3,Leaf);
            Label(panel,"XREALITY   /   DESKTOP STUDIO", 27,26,396,24,14,Leaf,FontStyle.Bold);
            Label(panel,"今天去哪里？",27,76,396,60,42,Paper,FontStyle.Bold);
            Label(panel,"两种体验独立运行，可以随时回到这里。",27,146,396,43,18,Muted);
            pet = Choice(panel, 219, "01", "进入宠物房间", "陪猫待一会儿 · 不需要摄像头", Peach, () => Open("PetRoom"));
            live = Choice(panel, 345, "02", "开启化身直播", "实时面捕 · 摄像头由你手动开启", Leaf, () => Open("LiveStudio"));
            Block(panel,"Settings rule",27,485,396,1,Card);
            Label(panel,"应用音量",27,505,180,24,17,Paper,FontStyle.Bold);
            var percentage = Label(panel,"",295,505,128,24,17,Muted);
            percentage.alignment = TextAnchor.MiddleRight;
            audioLevel = Volume(panel);
            audioLevel.SetValueWithoutNotify(AudioListener.volume);
            void DisplayVolume(float value) => percentage.text = $"{Mathf.RoundToInt(value * 100)}%";
            DisplayVolume(audioLevel.value);
            audioLevel.onValueChanged.AddListener(value =>
            {
                AudioListener.volume = value;
                PlayerPrefs.SetFloat("xr.volume", value);
                PlayerPrefs.Save();
                DisplayVolume(value);
            });
            Label(panel,"本地运行 · 无需账号 · 摄像头默认关闭",27,612,396,30,15,Muted);
            Canvas.preWillRenderCanvases += Fit;
            Fit();
        }

        void Fit()
        {
            var target = canvas.worldCamera.targetTexture;
            float scale = Mathf.Max(.1f, Mathf.Min((target ? target.width : Screen.width) / 1280f,
                (target ? target.height : Screen.height) / 720f));
            if (!Mathf.Approximately(canvas.scaleFactor, scale)) canvas.scaleFactor = scale;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) Open("PetRoom");
            else if (Input.GetKeyDown(KeyCode.Alpha2)) Open("LiveStudio");
            else if (Input.GetKeyDown(KeyCode.Tab) && EventSystem.current)
            {
                var es = EventSystem.current;
                var order = new Selectable[] { pet, live, audioLevel };
                int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                int index = Array.FindIndex(order, control => control.gameObject == es.currentSelectedGameObject);
                if (index < 0) index = step > 0 ? 2 : 0;
                es.SetSelectedGameObject(order[(index + step + 3) % 3].gameObject);
            }
        }

        void Open(string scene)
        {
            if (opening != null) return;
            opening = SceneManager.LoadSceneAsync(scene);
            pet.interactable = live.interactable = false;
        }

        internal void SelectPet() => pet.onClick.Invoke();
        internal void SelectLive() => live.onClick.Invoke();

        Button Choice(Transform parent, float y, string number, string title, string subtitle, Color accent, Action onClick)
        {
            var background = Block(parent,title,27,y,396,106,Color.white);
            background.raycastTarget = true;
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.fadeDuration = 0; // Do not flash the default white graphic before the first frame.
            colors.normalColor = Card;
            colors.highlightedColor = new Color32(54, 80, 89, 255);
            colors.selectedColor = new Color32(67, 91, 102, 255);
            colors.pressedColor = new Color32(48, 71, 82, 255);
            colors.disabledColor = Ink;
            button.colors = colors;
            Block(background.rectTransform,"Mode accent",0,0,4,106,accent);
            Label(background.rectTransform,number,18,20,37,26,16,accent,FontStyle.Bold);
            Label(background.rectTransform,title,62,16,272,32,25,Paper,FontStyle.Bold);
            Label(background.rectTransform,subtitle,62,55,310,30,17,Muted);
            Label(background.rectTransform,number == "01" ? "[1]" : "[2]",343,21,41,26,15,accent);
            button.onClick.AddListener(() => onClick());
            return button;
        }

        static Slider Volume(Transform panel)
        {
            var track = Block(panel,"Application volume",27,539,396,48,Card);
            track.raycastTarget = true;
            var slider = track.gameObject.AddComponent<Slider>();
            var channel = Block(track.rectTransform,"Volume channel",14,20,368,8,Ink);
            var area = Rect(channel.rectTransform,"Handle travel",0,-8,368,24);
            var handle = Block(area,"Volume handle",0,0,20,24,Leaf);
            handle.rectTransform.anchorMin = new Vector2(0, 0);
            handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.sizeDelta = new Vector2(20, 0);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            var colors = slider.colors;
            colors.selectedColor = new Color32(255,255,255,255);
            colors.highlightedColor = new Color32(225,244,237,255);
            colors.normalColor = new Color32(180,224,210,255);
            slider.colors = colors;
            return slider;
        }

        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var result = (RectTransform)new GameObject(name,typeof(RectTransform)).transform;
            result.SetParent(parent,false);
            result.anchorMin = result.anchorMax = result.pivot = new Vector2(0,1);
            result.anchoredPosition = new Vector2(x,-y);
            result.sizeDelta = new Vector2(w,h);
            return result;
        }

        static Image Block(Transform parent,string name,float x,float y,float w,float h,Color color)
        {
            var image = Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text Label(Transform parent,string text,float x,float y,float w,float h,int size,Color color,FontStyle style=FontStyle.Normal)
        {
            var label = Rect(parent,text,x,y,w,h).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        protected override void OnDestroy()
        {
            Canvas.preWillRenderCanvases -= Fit;
            if (font) Destroy(font);
            base.OnDestroy();
        }
    }
}
