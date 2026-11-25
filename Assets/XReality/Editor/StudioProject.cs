using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace XReality.Editor
{
    public static class StudioProject
    {
        const string Version = "0.3.35";
        internal const string Dir = "Assets/XReality/Generated/StorybookV19";
        static readonly string[] Names =
        {
            "Home",
            "PetRoom",
            "LiveStudio"
        };
        internal static string Asset(string name) => Dir + "/" + name;
        static string ScenePath(string name) => Asset(name + ".unity");
        [InitializeOnLoadMethod]
        static void OnImport()
        {
            if (!Application.isBatchMode)
                EditorApplication.delayCall += FirstOpen;
        }

        static void FirstOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Names.All(n => File.Exists(ScenePath(n))))
                return;
            var previous = SceneManager.GetActiveScene();
            bool empty = string.IsNullOrEmpty(previous.path) && !previous.isDirty;
            if (string.IsNullOrEmpty(previous.path) && previous.isDirty) return; // Never discard unsaved user work.
            Prepare();
            if (empty)
                EditorSceneManager.OpenScene(ScenePath("Home"));
        }

        [MenuItem("XReality/Prepare project")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Asset("StudioURP.asset"));
            if (!pipeline)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, Asset("Renderer.asset"));
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.msaaSampleCount = 4;
                pipeline.supportsHDR = false;
                AssetDatabase.CreateAsset(pipeline, Asset("StudioURP.asset"));
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            foreach (var name in Names)
            {
                if (File.Exists(ScenePath(name)))
                    continue;
                var previous = SceneManager.GetActiveScene();
                // A fresh editor session has an untitled scene and cannot add another one.
                bool replaceBlank = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previous.path);
                if (replaceBlank && previous.isDirty)
                    throw new InvalidOperationException("Save the current untitled scene before preparing XReality.");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, replaceBlank ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                try
                {
                    StudioArt.Compose(name);
                    if (!EditorSceneManager.SaveScene(scene, ScenePath(name)))
                        throw new IOException("Scene save failed: " + name);
                }
                finally
                {
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                    if (!replaceBlank) EditorSceneManager.CloseScene(scene, true);
                }
            }

            EditorBuildSettings.scenes = Names.Select(n => new EditorBuildSettingsScene(ScenePath(n), true)).ToArray();
            PlayerSettings.companyName = "XReality";
            PlayerSettings.productName = "XReality";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("XReality/Build Windows x64")]
        public static void BuildWindows()
        {
            if (Application.unityVersion != "6000.3.10f1")
                throw new InvalidOperationException("Build requires Unity 6000.3.10f1; found " + Application.unityVersion);
            Prepare();
            if (File.Exists("Assets/StreamingAssets/face_landmarker.task"))
                throw new InvalidOperationException("Do not bundle the Face Landmarker model; remove it from StreamingAssets first.");
            string output = Path.GetFullPath($"Builds/Windows-{Version}/XReality.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(Names.Select(ScenePath).ToArray(), output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("Windows player: " + output);
        }

        internal static AnimatorController CatController()
        {
            var path = Asset("Cat.controller");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller)
                return controller;
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var graph = controller.layers[0].stateMachine;
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
            {
                float period = mood == Mood.Chase ? .38f : mood == Mood.EarFlick ? .55f : mood == Mood.Nuzzle ? .9f : 2f;
                bool sleep = mood == Mood.Sleep || mood == Mood.EarFlick;
                var clip = new AnimationClip
                {
                    name = mood.ToString(),
                    frameRate = 30
                };
                void Curve(string bone, string prop, float a, float b)
                {
                    var c = new AnimationCurve(new Keyframe(0, a), new Keyframe(period * .5f, b), new Keyframe(period, a));
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(bone, typeof(Transform), prop), c);
                }

                bool poised = mood == Mood.Notice;
                bool sprint = mood == Mood.Chase;
                bool nuzzle = mood == Mood.Nuzzle;
                // Reset every animated property in every state so a cross-fade out of
                // sleep or play cannot leave a crouched body or closed eyes behind.
                Curve("Body", "m_LocalPosition.y", 0, sprint ? .13f : sleep ? .008f : .02f);
                Curve("Body", "m_LocalScale.y", sleep ? .66f : poised ? .84f : sprint ? .92f : 1,
                    sleep ? .68f : poised ? .76f : sprint ? 1.035f : 1);
                Curve("Body", "m_LocalScale.x", sleep ? 1.08f : 1, sleep ? 1.09f : 1);
                Curve("Body", "m_LocalScale.z", sleep ? 1.1f : poised ? 1.06f : 1,
                    sleep ? 1.1f : poised ? 1.13f : sprint ? 1.08f : 1);
                // Unity samples transform channels together. Preserve the head pivot's
                // height while animating the forward reach, including in edit-mode samples.
                Curve("Body/Head", "m_LocalPosition.x", 0, 0);
                Curve("Body/Head", "m_LocalPosition.y", .93f, .93f);
                Curve("Body/Head", "m_LocalPosition.z", -.38f,
                    sleep ? -.49f : poised ? -.45f : nuzzle ? -.52f : -.38f);
                Curve("Body/Head", "localEulerAnglesRaw.x", sleep ? -13 : poised ? -9 : 0,
                    sleep ? -16 : poised ? -18 : sprint ? -6 : nuzzle ? 9 : 0);
                Curve("Body/Head", "localEulerAnglesRaw.z", sleep ? 18 : 0,
                    sleep ? 20 : nuzzle ? -25 : poised ? -3 : 3);
                Curve("Body/Head/Ear.L", "localEulerAnglesRaw.z", sleep ? -8 : poised ? -7 : 0,
                    mood == Mood.EarFlick ? 33 : sleep ? -8 : poised ? -12 : 4);
                Curve("Body/Head/Ear.R", "localEulerAnglesRaw.z", sleep ? 8 : poised ? 7 : 0,
                    sleep ? 8 : poised ? 12 : -4);
                Curve("Body/Tail", "localEulerAnglesRaw.z", sleep ? -38 : poised ? 32 : -8,
                    sleep ? -38 : poised ? 51 : sprint ? 29 : 16);
                foreach (string eye in new[] { "Eye.L", "Eye.R" })
                    Curve("Body/Head/" + eye, "m_LocalScale.y", sleep ? .05f : 1, sleep ? .05f : poised ? 1.12f : 1);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.AddObjectToAsset(clip, controller);
                var state = graph.AddState(mood.ToString());
                state.motion = clip;
                if (mood == Mood.Watch)
                    graph.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }
    }
}
