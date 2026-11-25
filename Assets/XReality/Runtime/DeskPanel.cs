using UnityEngine;
using UnityEngine.SceneManagement;

namespace XReality
{
    // One compact panel per scene. The capture scene suppresses it entirely in clean mode.
    public abstract class DeskPanel : MonoBehaviour
    {
        internal const int DefaultFrameRate = 30;
        protected const float Width = 302;
        protected float Scale => Mathf.Max(.1f, Mathf.Min(Screen.width / 1050f, Screen.height / 735f));
        protected bool OverPanel => Input.mousePosition.x < (Width + 30) * Scale;

        Font font;
        GUIStyle title, label, eyebrow, button, field;
        static readonly Color Ink = new Color(.09f, .14f, .18f, .97f);
        protected virtual void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = DefaultFrameRate;
            QualitySettings.vSyncCount = 0;
            AudioListener.volume = PlayerPrefs.GetFloat("xr.volume", .65f);
        }

        protected void Begin(string section, string heading)
        {
            if (title == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" }, 18);
                GUIStyle Style(GUIStyle source, int size) => new GUIStyle(source)
                {
                    font = font,
                    fontSize = size,
                    wordWrap = true
                };
                title = Style(GUI.skin.label, 29);
                title.fontStyle = FontStyle.Bold;
                label = Style(GUI.skin.label, 16);
                eyebrow = Style(GUI.skin.label, 13);
                eyebrow.normal.textColor = new Color(.58f, .89f, .81f);
                button = Style(GUI.skin.button, 17);
                field = Style(GUI.skin.textField, 17);
            }

            GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            var height = Screen.height / Scale - 36;
            GUI.color = Ink;
            GUI.DrawTexture(new Rect(18, 18, Width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(38, 35, Width - 40, height - 32));
            GUILayout.Label("XREALITY   /   " + section, eyebrow);
            GUILayout.Space(14);
            GUILayout.Label(heading, title);
            GUILayout.Space(9);
        }

        protected void End()
        {
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        protected void Text(string s) => GUILayout.Label(s, label);
        protected bool Action(string s)
        {
            bool hit = GUILayout.Button(s, button, GUILayout.MinHeight(38));
            GUILayout.Space(4);
            return hit;
        }

        protected string Field(string s) => GUILayout.TextField(s, 24, field, GUILayout.Height(32));
        protected void Home() => SceneManager.LoadScene("Home");
        protected virtual void OnDestroy()
        {
            if (font)
                Destroy(font);
        }
    }
}
