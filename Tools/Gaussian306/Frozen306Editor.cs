using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.LocalPreview
{
    public static class Frozen306Editor
    {
        const string Dir = "Assets/XReality/Generated/Gaussian306Preview";
        const string ScenePath = Dir + "/Gaussian306Preview.unity";
        const string Output = "Builds/GaussianAvatars-306";

        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("Use Unity with -force-d3d11.");
            if (!GraphicsSettings.defaultRenderPipeline)
                throw new InvalidOperationException("XReality URP project is not prepared.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("306 Orbit Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 42;
            cam.nearClipPlane = .01f;
            cam.farClipPlane = 4;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.transform.position = new Vector3(0, .015f, .58f);
            cam.transform.rotation = Quaternion.Euler(0, 180, 0);
            var go = new GameObject("GaussianAvatar 306 – frozen frame 0");
            go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>();
            var view = go.AddComponent<Frozen306>();
            view.stage = cam;
            view.payload = AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "/Frame306.bytes");
            view.shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "/Frozen306.shader");
            if (!view.payload || !view.shader || !EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save the isolated 306 preview scene.");
            string folder = Path.GetFullPath(Output + "/Unity-QA");
            Directory.CreateDirectory(folder);
            try
            {
                view.Initialize();
                int count = view.Count;
                view.View(0, 0, false);
                var front = OffscreenFrame.Save(cam, folder, "306-front");
                view.View(35, 0, false);
                var side = OffscreenFrame.Save(cam, folder, "306-side-35deg");
                view.View(-20, 0, true);
                OffscreenFrame.Save(cam, folder, "306-green", true);
                int diff = 0;
                for (int i = 0; i < front.Length; i++)
                    if (Math.Abs(front[i].r - side[i].r) + Math.Abs(front[i].g - side[i].g) + Math.Abs(front[i].b - side[i].b) > 45) diff++;
                if (count != 89021 || diff < 20000)
                    throw new InvalidOperationException($"Unexpected frame validation: {count} points, only {diff} view-dependent pixels.");
                Debug.Log($"XR_GA_306_UNITY_OK: 89021 splats, {diff} changed pixels on orbit");
            }
            finally { view.Release(); }
            // Reload the serialized scene without the editor's transient GPU buffers before building.
            EditorSceneManager.OpenScene(ScenePath);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ga306-build") < 0) return;
            string exe = Path.GetFullPath(Output + "/Unity-Windows/Gaussian306.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            var result = BuildPipeline.BuildPlayer(new[] { ScenePath }, exe, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Standalone 306 preview build failed: " + result.summary.result);
            Debug.Log("XR_GA_306_BUILD_OK: " + exe);
        }
    }
}
