#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XReality
{
    public static class GaussianAvatarImportQA
    {
        // No tracked likeness or third-party model is required for this test.
        public static void Run()
        {
            Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11, "D3D11 required");
            string dir = Path.GetFullPath("Builds/QA-XRG-Import");
            Directory.CreateDirectory(dir);
            string asset = Path.Combine(dir, "synthetic.xrg");
            WriteFixture(asset, 8);
            string truncated = Path.Combine(dir, "truncated.xrg"), bad = Path.Combine(dir, "bad-binding.xrg");
            File.WriteAllBytes(truncated, File.ReadAllBytes(asset).SkipLast(1).ToArray());
            WriteFixture(bad, 3, true);
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = torso.GetComponent<Renderer>();
            var cameraObject = new GameObject("XRG QA camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.32f, -3);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.green;
            camera.fieldOfView = 35;
            var rt = RenderTexture.GetTemporary(320, 180, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pixels = new Texture2D(320, 180, TextureFormat.RGBA32, false, true);
            bool Load(string path, out Gaussian306Live avatar) => Gaussian306Live.TryStart(
                new[] { Gaussian306Live.Flag, "-ga306-asset", path }, torso.transform, camera, out avatar);
            try
            {
                Require(!Load(truncated, out _) && !renderer.forceRenderingOff, "truncation changed fallback");
                Require(!Load(bad, out _) && !renderer.forceRenderingOff, "invalid binding changed fallback");
                var shortAsset = Path.Combine(dir, "three-splats.xrg");
                WriteFixture(shortAsset, 3);
                Require(Load(shortAsset, out var small), "second variable-count XRG did not load");
                small.Dispose();
                Require(!renderer.forceRenderingOff, "source renderer was not restored");
                string legacy = Path.GetFullPath("Builds/GaussianAvatars-306/Live306.xrg");
                if (File.Exists(legacy))
                {
                    Require(Load(legacy, out var v1), "local legacy XRG did not load");
                    v1.Dispose();
                    Debug.Log("XR_XRG_LEGACY_QA_OK");
                }
                Require(Load(asset, out var v2) && renderer.forceRenderingOff, "v2 XRG did not replace mesh");
                using (v2)
                {
                    Color32[] Render(string name, Vector4 controls, Quaternion rotation)
                    {
                        v2.Refresh(rotation, controls);
                        OffscreenFrame.Render(camera, rt, false);
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = rt;
                            pixels.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                            var colors = pixels.GetPixels32();
                            File.WriteAllBytes(Path.Combine(dir, name + ".png"), pixels.EncodeToPNG());
                            return colors;
                        }
                        finally { RenderTexture.active = previous; }
                    }
                    var neutral = Render("neutral", Vector4.zero, Quaternion.identity);
                    Require(neutral[0].g >= 250 && neutral[0].r <= 3 && neutral[0].b <= 3 &&
                        neutral.Any(c => c.r > 100), "expected visible fixture on a green background");
                    string[] names = { "left-blink", "right-blink", "jaw", "smile" };
                    for (int i = 0; i < 4; i++)
                    {
                        var pose = Vector4.zero;
                        pose[i] = 1;
                        Require(Changed(neutral, Render(names[i], pose, Quaternion.identity)) > 40, names[i]);
                    }
                    Require(Changed(neutral, Render("turn", Vector4.zero, Quaternion.Euler(5, 22, 0))) > 40, "head turn");
                    Require(Changed(neutral, Render("reset", Vector4.zero, Quaternion.identity)) == 0, "neutral restore");
                }
                Require(!renderer.forceRenderingOff, "disposing v2 did not restore mesh");
                Debug.Log("XR_XRG_V2_QA_OK: synthetic fixture, not trained data");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(torso);
            }
        }

        static int Changed(Color32[] a, Color32[] b) => a.Where((p, i) =>
            Math.Abs(p.r - b[i].r) + Math.Abs(p.g - b[i].g) + Math.Abs(p.b - b[i].b) > 16).Count();

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("XRG QA: " + message);
        }

        static void WriteFixture(string path, int count, bool badBinding = false)
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(0x4c363033); w.Write(2); w.Write(count); w.Write(count * 3); w.Write(count);
            void Vec(float x, float y, float z) { w.Write(x); w.Write(y); w.Write(z); }
            Vec(0, -.10f, 0);
            for (int i = 0; i < count; i++)
            {
                float x = (i % 2 - .5f) * .10f, y = (.9f - i / 2) * .055f;
                Vec(x - .02f, y - .02f, .02f); Vec(x + .02f, y - .02f, .02f); Vec(x, y + .02f, .02f);
            }
            for (int _ = 0; _ < count * 3; _++) w.Write(1f);
            for (int channel = 0; channel < 4; channel++)
                for (int i = 0; i < count; i++)
                    for (int j = 0; j < 3; j++) Vec(0, i % 4 == channel ? .032f : 0, 0);
            for (int i = 0; i < count * 3; i++) w.Write(i);
            for (int i = 0; i < count; i++) w.Write(badBinding ? count : i);
            for (int i = 0; i < count; i++)
            {
                Vec(0, 0, 0); w.Write(.95f);
                Vec(.45f, 0, 0); w.Write(0f); Vec(0, .45f, 0); w.Write(0f); Vec(0, 0, .45f); w.Write(0f);
            }
            for (int i = 0; i < count; i++)
                for (int j = 0; j < 16; j++) { Vec(j == 0 ? 1f : 0, 0, 0); w.Write(0f); }
        }
    }
}
#endif
