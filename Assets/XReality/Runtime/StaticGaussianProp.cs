using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XReality
{
    internal sealed class StaticGaussianProp : IDisposable
    {
        internal const string Flag = "-splat-object";
        const int HeaderBytes = 52, RowBytes = 256, MaxCount = 250000, Bins = 4096;
        [StructLayout(LayoutKind.Sequential)]
        struct Splat { public Vector4 center, ax, ay, az; }
        GraphicsBuffer geometry, coefficients, orderBuffer;
        Mesh mesh;
        Material material;
        GameObject node;
        Camera stage;
        Vector3[] centers;
        int[] order, keys;
        readonly int[] bins = new int[Bins];
        Vector3 sortedForward;
        float yaw, zoom = 1;
        bool visible = true;
        internal int Count => centers?.Length ?? 0;

        internal static bool TryLoad(string path, Camera stage, int layer, out StaticGaussianProp prop)
        {
            prop = null;
            if (!stage || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 || !SystemInfo.supportsComputeShaders)
                return false;
            var shader = Resources.Load<Shader>("StaticGaussianProp");
            if (!shader || !shader.isSupported) return false;
            var candidate = new StaticGaussianProp { stage = stage };
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                    !string.Equals(Path.GetExtension(path), ".xrs", StringComparison.OrdinalIgnoreCase)) return false;
                candidate.Load(path, shader, layer);
                prop = candidate;
                Debug.Log($"XR_XRS_LOADED: {prop.Count} static splats");
                return true;
            }
            catch (Exception error)
            {
                candidate.Dispose();
                Debug.LogWarning("XR_XRS_REJECTED: " + error.GetType().Name + ": " + error.Message);
                return false;
            }
        }

        void Load(string path, Shader shader, int layer)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < HeaderBytes || reader.ReadUInt32() != 0x31535258 || reader.ReadUInt32() != 1)
                throw new InvalidDataException("Invalid XRS header or version");
            int count = reader.ReadInt32();
            if (count < 1 || count > MaxCount || reader.ReadInt32() != 3 || stream.Length != HeaderBytes + (long)RowBytes * count)
                throw new InvalidDataException("Invalid XRS count, SH degree or length");
            var basis = new Vector3[3];
            for (int i = 0; i < 3; ++i)
            {
                basis[i] = ReadVector(reader, 1.001f);
                for (int j = 0; j <= i; ++j)
                    if (Mathf.Abs(Vector3.Dot(basis[i], basis[j]) - (i == j ? 1 : 0)) >= .002f)
                        throw new InvalidDataException("Invalid XRS orientation basis");
            }
            var points = new Splat[count];
            var colors = new Vector3[count * 16];
            centers = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = ReadVector(reader, 1.001f);
                float opacity = ReadFloat(reader, 0, 1);
                if (opacity == 0) throw new InvalidDataException("Invalid XRS opacity");
                points[i] = new Splat { center = new Vector4(pos.x, pos.y, pos.z, opacity),
                    ax = ReadAxis(reader), ay = ReadAxis(reader), az = ReadAxis(reader) };
                for (int j = 0; j < 16; j++) colors[16 * i + j] = ReadVector(reader, 64);
                centers[i] = pos;
            }
            geometry = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 64);
            coefficients = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 16, 12);
            orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            geometry.SetData(points); coefficients.SetData(colors);
            order = new int[count]; keys = new int[count];
            material = new Material(shader);
            material.SetBuffer("_Geometry", geometry); material.SetBuffer("_Coeffs", coefficients); material.SetBuffer("_Order", orderBuffer);
            for (int i = 0; i < 3; i++) material.SetVector("_Basis" + i, basis[i]);

            mesh = new Mesh { name = "XRS static splats", indexFormat = IndexFormat.UInt32, vertices = new Vector3[count * 4] };
            var indices = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4, k = i * 6;
                indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                indices[k + 3] = v + 2; indices[k + 4] = v + 1; indices[k + 5] = v + 3;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 9);
            mesh.UploadMeshData(true);
            node = new GameObject("Static 3DGS prop") { layer = layer };
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Place();
        }

        internal void Control(float rotateDegrees, float logZoom, bool reset, bool toggle)
        {
            if (reset) { yaw = 0; zoom = 1; }
            else { yaw = Mathf.Repeat(yaw + rotateDegrees, 360); zoom = Mathf.Clamp(zoom * Mathf.Exp(logZoom), .45f, 2f); }
            if (toggle) { visible = !visible; node.SetActive(visible); }
        }

        internal void Place()
        {
            if (!visible) return;
            // Keep the prop in the presentation area when the studio switches to clean output.
            float depth = Mathf.Clamp(2.5f, stage.nearClipPlane + .1f, stage.farClipPlane - .1f);
            var forward = Vector3.ProjectOnPlane(stage.transform.forward, Vector3.up);
            var front = forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            node.transform.SetPositionAndRotation(stage.ViewportToWorldPoint(new Vector3(.75f, .49f, depth)),
                front * Quaternion.Euler(0, yaw, 0));
            float halfHeight = stage.orthographic ? stage.orthographicSize : depth * Mathf.Tan(stage.fieldOfView * Mathf.Deg2Rad / 2);
            float halfWidth = halfHeight * stage.aspect;
            node.transform.localScale = Vector3.one * (Mathf.Min(.21f * halfHeight, .17f * halfWidth) * zoom);
            Sort(node.transform.InverseTransformDirection(stage.transform.forward));
        }

        void Sort(Vector3 forward)
        {
            if ((forward - sortedForward).sqrMagnitude < 1e-8f) return;
            sortedForward = forward;
            Array.Clear(bins, 0, bins.Length);
            const float bound = 1.75f;
            for (int i = 0; i < Count; ++i)
            {
                int key = Mathf.Clamp((int)((bound - Vector3.Dot(centers[i], forward)) * ((Bins - 1) / (2 * bound))), 0, Bins - 1);
                keys[i] = key; bins[key]++;
            }
            int offset = 0;
            for (int i = 0; i < Bins; ++i) { int n = bins[i]; bins[i] = offset; offset += n; }
            for (int i = 0; i < Count; ++i) order[bins[keys[i]]++] = i;
            orderBuffer.SetData(order);
        }

        static float ReadFloat(BinaryReader reader, float min, float max)
        {
            float value = reader.ReadSingle();
            if (!(value >= min && value <= max)) throw new InvalidDataException("Out-of-range XRS number");
            return value;
        }
        static Vector3 ReadVector(BinaryReader reader, float bound) =>
            new Vector3(ReadFloat(reader, -bound, bound), ReadFloat(reader, -bound, bound), ReadFloat(reader, -bound, bound));
        static Vector4 ReadAxis(BinaryReader reader)
        {
            var v = ReadVector(reader, 1);
            ReadFloat(reader, 0, 0);
            if (!(v.sqrMagnitude > 0 && v.sqrMagnitude <= 1)) throw new InvalidDataException("Invalid XRS axis");
            return new Vector4(v.x, v.y, v.z, 0);
        }

        public void Dispose()
        {
            if (node) node.SetActive(false);
            geometry?.Dispose(); coefficients?.Dispose(); orderBuffer?.Dispose();
            geometry = coefficients = orderBuffer = null;
            Destroy(node); Destroy(mesh); Destroy(material);
        }
        static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
