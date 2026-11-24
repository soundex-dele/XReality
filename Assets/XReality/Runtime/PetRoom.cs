using UnityEngine;

namespace XReality
{
    public sealed class PetRoom : DeskPanel
    {
        public Transform cat, toy, head, contact;
        public Animator pose;
        public AudioSource chirp;
        public Camera roomCamera;
        readonly PetBrain brain = new PetBrain();
        Mood shown = Mood.Watch;
        Mood lastVisual = Mood.Watch;
        string catName, draft;
        int bond;
        bool dragging;
        bool visitPlanned;
        Vector3? visitTarget;
        PlayMemory memory;
        internal static string QaProfile;
        internal bool SuppressInput { get; set; }
        static string Key(string name) => "xr.cat." + (QaProfile == null ? "" : "qa." + QaProfile + ".") + name;
        internal static void DeleteQaProfile()
        {
            if(QaProfile == null) return;
            foreach(string key in new[]{"name","bond","play.rug","play.window"}) PlayerPrefs.DeleteKey(Key(key));
            PlayerPrefs.Save();
        }
        internal string CatName => catName;
        internal int Bond => bond;
        double Now => Time.timeSinceLevelLoadAsDouble;

        public void OfferToy(float x, float z) => MoveToy(new Vector3(x,.23f,z));
        internal PlayMemory Memory => memory;
        internal Mood CurrentMood => brain.State;
        void Start()
        {
            catName = draft = PlayerPrefs.GetString(Key("name"), "栗子");
            bond = PlayerPrefs.GetInt(Key("bond"), 0);
            memory = new PlayMemory(PlayerPrefs.GetInt(Key("play.rug")), PlayerPrefs.GetInt(Key("play.window")));
            // Show remembered preference through behavior, rather than granting an offline bonus.
            visitPlanned = memory.Favorite != PlayMemory.Zone.None;
            var clip = AudioClip.Create("Chirp", 6400, 1, 32000, false);
            var samples = new float[6400];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / 32000f;
                samples[i] = .075f * Mathf.Sin(2 * Mathf.PI * (420 * t + 300 * t * t)) * Mathf.Sin(Mathf.PI * i / samples.Length);
            }

            clip.SetData(samples, 0);
            chirp.clip = clip;
        }

        internal void Stroke()
        {
            if (!brain.Stroke(Now))
                return;
            visitPlanned = false;
            visitTarget = null;
            bond = Mathf.Min(9999, bond + 1);
            PlayerPrefs.SetInt(Key("bond"), bond);
            PlayerPrefs.Save();
            chirp.Play();
        }

        void MoveToy(Vector3 p)
        {
            visitPlanned = false;
            visitTarget = null;
            toy.position = new Vector3(Mathf.Clamp(p.x, -.8f, 3.0f), .23f, Mathf.Clamp(p.z, -1.1f, 1.3f));
            if (Vector3.Distance(cat.position, toy.position) > .6f)
                brain.OfferToy(Now);
        }

        void PollPointer()
        {
            var ray = roomCamera.ScreenPointToRay(Input.mousePosition);
            if (!OverPanel && Input.GetMouseButtonDown(0) && Physics.Raycast(ray, out var hit))
            {
                if (hit.transform == toy || hit.transform.IsChildOf(toy))
                    dragging = true;
                else if (hit.transform == cat || hit.transform.IsChildOf(cat))
                    Stroke();
            }

            if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                dragging = false;
            if (dragging && !OverPanel && new Plane(Vector3.up, new Vector3(0, .23f, 0)).Raycast(ray, out var d))
                MoveToy(ray.GetPoint(d));
        }

        void Update()
        {
            if (!SuppressInput) PollPointer();
            var target = toy.position;
            target.y = cat.position.y;
            bool arrived = Vector3.Distance(cat.position, target) < .46f;
            if (brain.State == Mood.Chase && arrived && !dragging)
            {
                memory.Record(toy.position.x);
                bond = Mathf.Min(9999, bond + 1);
                PlayerPrefs.SetInt(Key("bond"), bond);
                PlayerPrefs.SetInt(Key("play.rug"), memory.Rug);
                PlayerPrefs.SetInt(Key("play.window"), memory.Window);
                PlayerPrefs.Save();
            }
            brain.Tick(Now, arrived && !dragging);
            if (visitPlanned && Now >= 2.6 && brain.State == Mood.Watch)
            {
                visitPlanned = false;
                visitTarget = memory.Favorite == PlayMemory.Zone.Window
                    ? new Vector3(2.07f,cat.position.y,.64f) : new Vector3(-.13f,cat.position.y,.72f);
            }
            if (visitTarget.HasValue && Vector3.Distance(cat.position, visitTarget.Value) < .09f)
                visitTarget = null;
            shown = brain.State;

            var visual = visitTarget.HasValue ? Mood.Chase : shown;
            if (visual != lastVisual)
            {
                lastVisual = visual;
                pose.CrossFadeInFixedTime("Base Layer." + visual, .17f);
            }
            bool playing = shown == Mood.Notice || shown == Mood.Chase;
            Vector3 look = (visitTarget ?? (playing ? target : roomCamera.transform.position)) - cat.position;
            look.y = 0;
            if (look.sqrMagnitude > .01f && shown != Mood.Sleep && shown != Mood.EarFlick)
            {
                var orient = Quaternion.LookRotation(-look);
                if (!playing && !visitTarget.HasValue) orient *= Quaternion.Euler(0,-19,0);
                cat.rotation = Quaternion.RotateTowards(cat.rotation, orient, Time.deltaTime * 235);
            }
            if (visitTarget.HasValue)
                cat.position = Vector3.MoveTowards(cat.position, visitTarget.Value, .69f * Time.deltaTime);
            else if (shown == Mood.Chase && !arrived)
                cat.position = Vector3.MoveTowards(cat.position, target, 1.6f * Time.deltaTime);
            if (contact) contact.SetPositionAndRotation(cat.position, cat.rotation);
        }

        void RollToy() => MoveToy(new Vector3(cat.position.x > 1 ? -.75f : 2.9f, 0, cat.position.z > .1f ? -.9f : 1.1f));

        void OnGUI()
        {
            if (SuppressInput) return;
            Begin("01   /   PET ROOM", catName + "的小窝");
            Text("点一下猫咪，或把毛线球拖到地毯另一边。");
            GUILayout.Space(12);
            Text("亲密度   " + bond);
            Text(shown == Mood.Sleep || shown == Mood.EarFlick ? "在打盹，耳朵还听得见。" : shown == Mood.Chase || shown == Mood.Notice ? "盯上毛线球了。" : "小屋很安静，慢慢来就好。");
            if (Action("摸摸它 · P"))
                Stroke();
            if (Action("滚动毛线球 · T")) RollToy();
            GUILayout.Space(12);
            Text("猫咪名字");
            GUI.SetNextControlName("CatName");
            draft = Field(draft);
            if (Action("保存名字")) Rename(draft);

            GUILayout.FlexibleSpace();
            if (Action("返回首页"))
                Home();
            var input = Event.current;
            if(input.type==EventType.KeyDown && !input.control && !input.alt && !input.command)
            {
                if (input.keyCode==KeyCode.Escape)
                {
                    if(GUI.GetNameOfFocusedControl()=="CatName") GUI.FocusControl(null);
                    else Home();
                    input.Use();
                }
                else if (GUI.GetNameOfFocusedControl()!="CatName" && input.keyCode==KeyCode.P) { Stroke(); input.Use(); }
                else if (GUI.GetNameOfFocusedControl()!="CatName" && input.keyCode==KeyCode.T) { RollToy(); input.Use(); }
            }
            End();
        }

        internal void Rename(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            catName = draft = name.Trim();
            if (catName.Length > 24) catName = draft = catName.Substring(0,24);
            PlayerPrefs.SetString(Key("name"), catName);
            PlayerPrefs.Save();
        }

        protected override void OnDestroy()
        {
            if (chirp && chirp.clip)
                Destroy(chirp.clip);
            base.OnDestroy();
        }
    }
}
