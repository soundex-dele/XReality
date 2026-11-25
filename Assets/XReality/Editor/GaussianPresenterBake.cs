using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XReality.Editor
{
    public static class GaussianPresenterBake
    {
        const string Sample = StudioProject.Dir + "/Resources/" + GaussianPresenterAsset.ResourcePath + ".asset";

        // Explicit opt-in: the standard build keeps its existing mesh presenter.
        [MenuItem("XReality/Bake 3DGS sample")]
        public static void Run()
        {
            var path = StudioProject.Asset("LiveStudio.unity");
            if (!File.Exists(path)) StudioProject.Prepare();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the LiveStudio scene before baking.");
                Bake(Studio(scene));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("XReality/Build Windows 3DGS preview")]
        public static void BuildWindowsPreview()
        {
            if (Application.unityVersion != "6000.3.10f1")
                throw new InvalidOperationException("Use the project's pinned Unity version.");
            StudioProject.Prepare();
            Run();
            string output = Path.GetFullPath("Builds/3DGS-Preview/Windows/XReality.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("3DGS Windows preview build failed: " + report.summary.result);
            Debug.Log("XR_3DGS_BUILD: " + output);
        }

        internal static LiveStudio Studio(Scene scene) => scene.GetRootGameObjects()
            .Select(root => root.GetComponentInChildren<LiveStudio>()).Single(live => live != null);

        internal static GaussianPresenterAsset Bake(LiveStudio live)
        {
            var shader = Shader.Find("XReality/Gaussian Presenter");
            if (!shader) throw new InvalidOperationException("3DGS shader was not imported.");
            var renderers = live.torso.GetComponentsInChildren<Renderer>().OrderBy(r => r.name, StringComparer.Ordinal);
            var random = new System.Random(0x5852);
            var parts = renderers.Select(r => BakePart(live.torso, r, random)).ToArray();
            int count = parts.Sum(p => p.splats.Length);
            if (count == 0 || count > GaussianPresenterAsset.MaxSplats)
                throw new InvalidOperationException("3DGS sample exceeds its point budget.");

            Directory.CreateDirectory(Path.GetDirectoryName(Sample));
            AssetDatabase.Refresh();
            var sample = AssetDatabase.LoadAssetAtPath<GaussianPresenterAsset>(Sample);
            bool create = !sample;
            if (create) sample = ScriptableObject.CreateInstance<GaussianPresenterAsset>();
            sample.version = GaussianPresenterAsset.FormatVersion;
            sample.shader = shader;
            sample.parts = parts;
            if (create) AssetDatabase.CreateAsset(sample, Sample);
            EditorUtility.SetDirty(sample);
            AssetDatabase.SaveAssets();
            Debug.Log($"XR_3DGS_BAKE: {count} splats in {parts.Length} parts at {Sample}");
            return sample;
        }

        // Area-stratified sampling on the licensed, textured source mesh. This is a
        // synthetic engineering fixture, not an optimized multi-view reconstruction.
        static GaussianPresenterAsset.Part BakePart(Transform torso, Renderer renderer, System.Random random)
        {
            var mesh = GaussianPresenter.SourceMesh(renderer);
            var material = renderer.sharedMaterial;
            if (!mesh || mesh.subMeshCount != 1 || !material || !material.GetTexture("_BaseMap"))
                throw new InvalidOperationException("Unsupported sample mesh or material: " + renderer.name);
            string path = AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"));
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path))) throw new IOException("Cannot load presenter texture: " + path);
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var tri = mesh.triangles;
                var transform = torso.worldToLocalMatrix * renderer.localToWorldMatrix;
                var normalMatrix = transform.inverse.transpose;
                var cumulative = new double[tri.Length / 3];
                var density = new float[cumulative.Length];
                Vector3 leftEye = Vector3.zero, rightEye = Vector3.zero;
                bool face = renderer.name == "Skin";
                if (face)
                {
                    leftEye = torso.InverseTransformPoint(torso.Find("Neck pivot/EyeLeft").position);
                    rightEye = torso.InverseTransformPoint(torso.Find("Neck pivot/EyeRight").position);
                }
                double area = 0;
                for (int t = 0; t < tri.Length; t += 3)
                {
                    var u = transform.MultiplyVector(verts[tri[t + 1]] - verts[tri[t]]);
                    var v = transform.MultiplyVector(verts[tri[t + 2]] - verts[tri[t]]);
                    float weight = 1;
                    if (face)
                    {
                        var center = transform.MultiplyPoint3x4((verts[tri[t]] + verts[tri[t + 1]] + verts[tri[t + 2]]) / 3);
                        float eyeDistance = Mathf.Min((center - leftEye).sqrMagnitude, (center - rightEye).sqrMagnitude);
                        weight += 13 * Mathf.Exp(-eyeDistance / .015f);
                    }
                    density[t / 3] = weight;
                    area += .5 * Vector3.Cross(u, v).magnitude * weight;
                    cumulative[t / 3] = area;
                }
                int budget = renderer.name switch
                {
                    "Skin" => 20500, "Jacket" => 10500, "Hair" => 9000, "Brows" => 2400,
                    "EyeLeft" or "EyeRight" => 1800, "Teeth" => 2500,
                    _ => throw new InvalidOperationException("Unexpected sample rig part: " + renderer.name)
                };
                if (area <= 0) throw new InvalidOperationException("Empty sample surface: " + renderer.name);
                float sigma = .93f * Mathf.Sqrt((float)(area / budget));
                bool cutout = material.IsKeywordEnabled("_ALPHATEST_ON");
                float cutoff = material.GetFloat("_Cutoff");
                Color tint = material.GetColor("_BaseColor").linear;
                Vector3 key = new Vector3(-.36f, .55f, -.75f).normalized;
                Vector3 fill = new Vector3(.65f, .18f, -.72f).normalized;
                var splats = new List<GaussianPresenterAsset.Splat>(budget);
                int triangle = 0;
                for (int i = 0; i < budget; i++)
                {
                    double address = (i + random.NextDouble()) * area / budget;
                    while (triangle + 1 < cumulative.Length && cumulative[triangle] <= address) triangle++;
                    int t = triangle * 3;
                    int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                    float root = Mathf.Sqrt((float)random.NextDouble());
                    float v = root * (float)random.NextDouble(), u = root - v, w = 1 - (u + v);
                    var coords = uv[a] * w + uv[b] * u + uv[c] * v;
                    var sample = image.GetPixelBilinear(Mathf.Repeat(coords.x, 1), Mathf.Repeat(coords.y, 1));
                    if (cutout && sample.a < cutoff) continue;
                    var n = normalMatrix.MultiplyVector(normals[a] * w + normals[b] * u + normals[c] * v).normalized;
                    float shade = .57f + .40f * Mathf.Max(0, Vector3.Dot(n, key)) + .16f * Mathf.Max(0, Vector3.Dot(n, fill));
                    splats.Add(new GaussianPresenterAsset.Splat { a = a, b = b, c = c, barycentric = new Vector2(u, v),
                        sigma = sigma / Mathf.Sqrt(density[triangle]), opacity = cutout ? Mathf.Lerp(.66f, .97f, sample.a) : .97f,
                        color = sample.linear * tint * shade });
                }
                if (splats.Count == 0) throw new InvalidOperationException("No opaque sample points: " + renderer.name);
                return new GaussianPresenterAsset.Part { rendererName = renderer.name, mesh = mesh, splats = splats.ToArray() };
            }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
