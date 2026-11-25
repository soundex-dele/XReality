using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.Editor
{
    // Scene geometry and the baked presenter are serialized to editable scenes; no runtime avatar import.
    internal static partial class StudioArt
    {
        const string Fur = "D88857", Cream = "F7DCB3", Dark = "352C3B", Pink = "D89288", Teal = "3D8180", Wood = "A77354", Wall = "D3C2A4", Blue = "577C9B";
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Material Paint(string hex)
        {
            var path = StudioProject.Asset("Mat-" + hex + ".mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m)
                return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", C(hex));
            m.SetFloat("_Smoothness", .22f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Transform Joint(string n, Transform parent, Vector3 p)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        static Transform Part(string n, Transform parent, Vector3 p, Vector3 s, string color, PrimitiveType shape = PrimitiveType.Sphere, float tilt = 0)
        {
            var obj = GameObject.CreatePrimitive(shape);
            obj.name = n;
            var t = obj.transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            t.localScale = s;
            t.localRotation = Quaternion.Euler(0, 0, tilt);
            obj.GetComponent<Renderer>().sharedMaterial = Paint(color);
            return t;
        }

        static Transform Box(string n, Transform parent, Vector3 p, Vector3 s, string color) => Part(n, parent, p, s, color, PrimitiveType.Cube);
        static Camera Stage(Vector3 pos, Vector3 look, string background, float size)
        {
            var go = new GameObject("Stage camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = C(background);
            cam.allowHDR = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = C("777A83");
            var lamp = new GameObject("Soft key").AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.intensity = .83f;
            lamp.transform.rotation = Quaternion.Euler(40, 24, 0);
            return cam;
        }

        internal static void Compose(string name)
        {
            if (name == "LiveStudio")
                Live();
            else if (name == "PetRoom")
                Pet();
            else
                Home();
        }

        static void Home()
        {
            Stage(V(0, 2.22f, -7), V(0, 1.61f, 0), "344E5C", 2.3f);
            new GameObject("Welcome").AddComponent<HomePage>();
            var display = Joint("Home character showcase", null, Vector3.zero);

            var petStage = Joint("Cat preview", display, V(.03f, 0, -.24f));
            Part("Cat plinth", petStage, V(0, .29f, 0), V(1.92f, .25f, 1.84f), "B27055", PrimitiveType.Cylinder);
            Part("Cat plinth rim", petStage, V(0, .52f, 0), V(1.96f, .045f, 1.88f), "DDA879", PrimitiveType.Cylinder);
            var cat = Joint("Showcase cat", petStage, V(-.11f, .565f, .09f));
            cat.localRotation = Quaternion.Euler(0, -17, -2);
            cat.localScale = Vector3.one * 1.09f;
            StorybookCat(null, cat);
            var yarn = Part("Showcase yarn", petStage, V(.52f, .696f, -.47f), V(.30f, .30f, .30f), Pink);
            YarnStrands(yarn);

            var hostStage = Joint("Avatar preview", display, V(2.16f, 0, .27f));
            Part("Avatar plinth", hostStage, V(0, .39f, 0), V(2.14f, .35f, 1.74f), "2B686F", PrimitiveType.Cylinder);
            Part("Avatar plinth rim", hostStage, V(0, .72f, 0), V(2.18f, .045f, 1.78f), "72AEAC", PrimitiveType.Cylinder);
            var host = Joint("Showcase avatar", hostStage, V(0, .609f, 0));
            host.localRotation = Quaternion.Euler(0, 6, 0);
            host.localScale = Vector3.one * .66f;
            Presenter(null, host, Vector3.zero);

            // The previews share meshes/materials, not either mode's runtime components or input.
            foreach (var collider in display.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);
        }

        static void Live()
        {
            var cam = Stage(V(0, 1.34f, -6), V(0, 1.34f, 0), "405768", 1.31f);
            var root = Joint("Live desk", null, Vector3.zero);
            var live = root.gameObject.AddComponent<LiveStudio>();
            live.stageCamera = cam;
            live.capture = root.gameObject.AddComponent<FaceCapture>();
            Presenter(live, root, V(.78f,.08f,0));
            var fill = new GameObject("Presenter fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .24f;
            fill.color = C("C9DDFE");
            fill.transform.rotation = Quaternion.Euler(10, -36, 0);
        }

        static void Pet()
        {
            var cam = Stage(V(0, 2.9f, -6.4f), V(.27f, .92f, 0), "BDB19D", 1.91f);
            var room = Joint("Sunroom", null, Vector3.zero);
            var p = room.gameObject.AddComponent<PetRoom>();
            p.roomCamera = cam;
            p.chirp = room.gameObject.AddComponent<AudioSource>();
            p.chirp.playOnAwake = false;
            Sunroom(room);
            p.toy = Part("Yarn ball", room, V(2.08f, .23f, -.63f), V(.43f, .43f, .43f), Pink);
            YarnStrands(p.toy);
            var cat = p.cat = Joint("Cat", room, V(.9f, .015f, .1f));
            p.pose = cat.gameObject.AddComponent<Animator>();
            p.pose.runtimeAnimatorController = StudioProject.CatController();
            p.pose.applyRootMotion = false;
            p.pose.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            p.contact = Joint("Soft contact shadow", room, cat.position);
            var shadow = p.contact.gameObject.AddComponent<MeshRenderer>();
            p.contact.gameObject.AddComponent<MeshFilter>().sharedMesh = ContactMesh();
            var shadowPath = StudioProject.Asset("Contact.mat");
            var shadowMaterial = AssetDatabase.LoadAssetAtPath<Material>(shadowPath);
            if (!shadowMaterial)
            {
                shadowMaterial = new Material(Shader.Find("XReality/Soft Contact"));
                AssetDatabase.CreateAsset(shadowMaterial, shadowPath);
            }
            shadow.sharedMaterial = shadowMaterial;
            StorybookCat(p, cat);
        }
    }
}
