using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.LocalPreview
{
    public sealed class Frozen306 : MonoBehaviour
    {
        public Camera stage;
        public TextAsset payload;
        public Shader shader;
        public Vector3 target = new Vector3(0, .015f, 0);
        public float distance = .58f;
        [StructLayout(LayoutKind.Sequential)] struct Splat { public Vector4 center, x, y, z; }
        Splat[] geometry;
        GraphicsBuffer splats, coeffs, order;
        Material material;
        Mesh mesh;
        float[] depths;
        int[] sequence;
        float yaw, pitch;
        bool green, failed;
        Vector3 mouse;
        public int Count => geometry?.Length ?? 0;
        IEnumerator Start()
        {
            bool smoke = Application.isBatchMode && Array.IndexOf(Environment.GetCommandLineArgs(), "-ga306-smoke") >= 0;
            try
            {
                Initialize(); View(0, 0, false);
                Debug.Log($"XR_GA_306_PLAYER_READY: {Count} splats");
            }
            catch (Exception e) { failed = true; Debug.LogException(e); }
            if (!smoke) yield break;
            if (failed) { Application.Quit(1); yield break; }
            yield return null;
            try
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Unity-QA"));
                Directory.CreateDirectory(folder);
                OffscreenFrame.Save(stage, folder, "306-player-front");
                View(35, 0, true);
                OffscreenFrame.Save(stage, folder, "306-player-green-35deg", true);
                Debug.Log("XR_GA_306_PLAYER_OK");
                Application.Quit(0);
            }
            catch (Exception e) { Debug.LogException(e); Application.Quit(1); }
        }

        public void Initialize()
        {
            if (splats != null) return;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !shader || !shader.isSupported || !payload || !stage)
                throw new InvalidOperationException("306 preview requires Direct3D11 and all scene assets.");
            byte[] bytes = payload.bytes;
            using var reader = new BinaryReader(new MemoryStream(bytes, false));
            if (reader.ReadInt32() != 0x36303347) throw new InvalidDataException("Frozen preview header");
            int n = reader.ReadInt32();
            if (n < 1 || n > 120000 || bytes.Length != 8 + n * 320) throw new InvalidDataException("Frozen preview size");
            geometry = new Splat[n];
            var colors = new Vector4[n * 16];
            Vector4 Read() => new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            for (int i = 0; i < n; i++) geometry[i] = new Splat { center = Read(), x = Read(), y = Read(), z = Read() };
            for (int i = 0; i < colors.Length; i++) colors[i] = Read();
            depths = new float[n];
            sequence = new int[n];
            splats = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 64);
            coeffs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, colors.Length, 16);
            order = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 4);
            splats.SetData(geometry);
            coeffs.SetData(colors);
            material = new Material(shader);
            material.SetBuffer("_Geometry", splats);
            material.SetBuffer("_Coeffs", coeffs);
            material.SetBuffer("_Order", order);
            mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = new Vector3[n * 4] };
            var indices = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int v = i * 4, k = i * 6;
                indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                indices[k + 3] = v + 2; indices[k + 4] = v + 1; indices[k + 5] = v + 3;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 8);
            mesh.UploadMeshData(true);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var draw = GetComponent<MeshRenderer>();
            draw.sharedMaterial = material;
            draw.shadowCastingMode = ShadowCastingMode.Off;
            draw.receiveShadows = false;
        }

        public void View(float angle, float elevation, bool chroma)
        {
            yaw = angle; pitch = elevation; green = chroma;
            var arm = Quaternion.Euler(pitch, yaw, 0) * (Vector3.forward * distance);
            stage.transform.position = target + arm;
            stage.transform.LookAt(target);
            stage.backgroundColor = green ? Color.green : new Color(.26f,.28f,.31f);
            Vector3 direction = stage.transform.forward;
            for (int i = 0; i < geometry.Length; i++)
            {
                depths[i] = -Vector3.Dot(direction, geometry[i].center);
                sequence[i] = i;
            }
            Array.Sort(depths, sequence);
            order.SetData(sequence);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            if (failed || order == null) return;
            Vector3 delta = Input.mousePosition - mouse;
            mouse = Input.mousePosition;
            float wheel = Input.mouseScrollDelta.y;
            if (Input.GetKeyDown(KeyCode.R)) { distance = .58f; View(0, 0, green); return; }
            if (Input.GetKeyDown(KeyCode.G)) { View(yaw, pitch, !green); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { View(yaw - 15, pitch, green); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { View(yaw + 15, pitch, green); return; }
            if (wheel != 0) { distance = Mathf.Clamp(distance * Mathf.Pow(.9f, wheel), .28f, 1.3f); View(yaw,pitch,green); }
            else if (Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0) && delta.sqrMagnitude > 0)
                View(yaw - delta.x * .3f, Mathf.Clamp(pitch + delta.y * .3f, -45, 45), green);
        }

        void OnDestroy() => Release();

        public void Release()
        {
            if (mesh) GetComponent<MeshFilter>().sharedMesh = null;
            if (material) GetComponent<MeshRenderer>().sharedMaterial = null;
            splats?.Dispose(); coeffs?.Dispose(); order?.Dispose();
            splats = coeffs = order = null;
            if (Application.isPlaying) { Destroy(mesh); Destroy(material); }
            else { DestroyImmediate(mesh); DestroyImmediate(material); }
            mesh = null; material = null;
        }
    }
}
