using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XReality
{
    // Local evaluation only: no licensed model data or Python/FLAME runtime is built into XReality.
    internal sealed class Gaussian306Live : IDisposable
    {
        internal const string Flag = "-avatar-ga306";
        const string AssetFlag = "-ga306-asset";
        const int SplatCount = 89021, VertexCount = 5143, FaceCount = 10144, ChannelCount = 4;
        const int DepthBuckets = 65536;
        const int Size = 20 + VertexCount * 64 + FaceCount * 12 + SplatCount * 324;
        static readonly Vector3 Neck = new Vector3(0, -.081f, -.052f);

        [StructLayout(LayoutKind.Sequential)]
        struct Frame
        {
            public Vector4 center, x, y, z;
        }

        readonly Camera stage;
        readonly Renderer[] sources;
        readonly bool[] wasHidden;
        readonly Vector3[] rest = new Vector3[VertexCount], vertices = new Vector3[VertexCount];
        readonly float[] influence = new float[VertexCount];
        readonly Vector3[][] deltas = new Vector3[ChannelCount][];
        readonly int[] triangles = new int[FaceCount * 3], bindings = new int[SplatCount];
        readonly Vector3[] localCenters = new Vector3[SplatCount];
        readonly Frame[] frames = new Frame[FaceCount];
        readonly int[] order = new int[SplatCount];
        readonly float[] depth = new float[SplatCount];
        readonly ushort[] depthKeys = new ushort[SplatCount];
        readonly int[] depthCounts = new int[DepthBuckets];
        GraphicsBuffer frameBuffer, points, coefficients, bindingBuffer, orderBuffer;
        GameObject node;
        Mesh mesh;
        Material material;
        bool hidden, initialized, disposed;
        Quaternion previousHead;
        Vector4 previousControls;
        Vector3 previousForward;

        Gaussian306Live(Transform torso, Camera stage)
        {
            this.stage = stage;
            sources = torso.GetComponentsInChildren<Renderer>();
            wasHidden = new bool[sources.Length];
            for (int i = 0; i < sources.Length; i++) wasHidden[i] = sources[i].forceRenderingOff;
        }

        internal static bool Requested(string[] args) => Array.IndexOf(args, Flag) >= 0;
        internal static bool TryStart(string[] args, Transform torso, Camera stage, out Gaussian306Live result)
        {
            result = null;
            int flag = Array.IndexOf(args, AssetFlag);
            if (flag < 0 || flag + 1 >= args.Length || !Path.IsPathFullyQualified(args[flag + 1]) ||
                !torso || !stage || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                return false;
            var shader = Resources.Load<Shader>("Gaussian306Live");
            if (!shader || !shader.isSupported) return false;
            var candidate = new Gaussian306Live(torso, stage);
            try
            {
                candidate.Initialize(args[flag + 1], shader, torso);
                result = candidate;
                return true;
            }
            catch (Exception error)
            {
                candidate.Dispose();
                Debug.LogWarning("Local 306 asset could not start: " + error.GetType().Name);
                return false;
            }
        }

        void Initialize(string path, Shader shader, Transform torso)
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (file.Length != Size || reader.ReadInt32() != 0x4c363033 || reader.ReadInt32() != 1 ||
                reader.ReadInt32() != SplatCount || reader.ReadInt32() != VertexCount || reader.ReadInt32() != FaceCount)
                throw new InvalidDataException("Unsupported local 306 rig.");
            for (int i = 0; i < rest.Length; i++) rest[i] = ReadVector(reader, 1);
            for (int i = 0; i < influence.Length; i++) influence[i] = ReadFloat(reader, 0, 1);
            for (int c = 0; c < ChannelCount; c++)
            {
                deltas[c] = new Vector3[VertexCount];
                for (int i = 0; i < VertexCount; i++) deltas[c][i] = ReadVector(reader, .25f);
            }
            void ReadIndices(int[] a, int max)
            {
                for (int i = 0; i < a.Length; i++)
                {
                    a[i] = reader.ReadInt32();
                    if ((uint)a[i] >= (uint)max) throw new InvalidDataException("Out of range 306 binding.");
                }
            }
            ReadIndices(triangles, VertexCount);
            ReadIndices(bindings, FaceCount);
            var data = new Frame[SplatCount];
            var colors = new Vector4[SplatCount * 16];
            for (int i = 0; i < data.Length; i++)
            {
                data[i].center = Read4(reader, 1000, 0, 1);
                localCenters[i] = data[i].center;
                data[i].x = Read4(reader, 1000, 0, 0);
                data[i].y = Read4(reader, 1000, 0, 0);
                data[i].z = Read4(reader, 1000, 0, 0);
            }
            for (int i = 0; i < colors.Length; i++) colors[i] = Read4(reader, 100, 0, 0);

            frameBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, FaceCount, 64);
            points = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatCount, 64);
            coefficients = new GraphicsBuffer(GraphicsBuffer.Target.Structured, colors.Length, 16);
            bindingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatCount, 4);
            orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SplatCount, 4);
            points.SetData(data); coefficients.SetData(colors); bindingBuffer.SetData(bindings);
            material = new Material(shader);
            material.SetBuffer("_Geometry", points); material.SetBuffer("_Coeffs", coefficients);
            material.SetBuffer("_Bindings", bindingBuffer); material.SetBuffer("_Faces", frameBuffer); material.SetBuffer("_Order", orderBuffer);

            mesh = new Mesh { name = "Local 306 splats", indexFormat = IndexFormat.UInt32, vertices = new Vector3[SplatCount * 4] };
            var indices = new int[SplatCount * 6];
            for (int i = 0; i < SplatCount; i++)
            {
                int v = i * 4, k = i * 6;
                indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                indices[k + 3] = v + 2; indices[k + 4] = v + 1; indices[k + 5] = v + 3;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            mesh.UploadMeshData(true);
            node = new GameObject("Local 306 (research preview)") { layer = torso.gameObject.layer };
            node.transform.SetParent(torso, false);
            node.transform.localPosition = new Vector3(0, 1.32f, 0);
            // FLAME native +Z faces the camera; the studio camera is at -Z.
            node.transform.localScale = new Vector3(5.7f, 5.7f, -5.7f);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var draw = node.AddComponent<MeshRenderer>();
            draw.sharedMaterial = material;
            draw.shadowCastingMode = ShadowCastingMode.Off;
            draw.receiveShadows = false;
            draw.lightProbeUsage = LightProbeUsage.Off;
            draw.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Refresh(Quaternion.identity, Vector4.zero);
            hidden = true;
            foreach (var r in sources) r.forceRenderingOff = true;
        }

        internal void Refresh(Quaternion stageHead, Vector4 controls)
        {
            Vector3 forward = node.transform.localToWorldMatrix.transpose.MultiplyVector(stage.transform.forward).normalized;
            if (initialized && (controls - previousControls).sqrMagnitude < .000001f &&
                Quaternion.Angle(stageHead, previousHead) < .1f && (forward - previousForward).sqrMagnitude < .000001f)
                return;
            initialized = true;
            previousControls = controls; previousHead = stageHead; previousForward = forward;
            var rotation = new Quaternion(-stageHead.x, -stageHead.y, stageHead.z, stageHead.w);
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = rest[i] + deltas[0][i]*controls.x + deltas[1][i]*controls.y + deltas[2][i]*controls.z + deltas[3][i]*controls.w;
                vertices[i] = Vector3.LerpUnclamped(v, Neck + rotation * (v - Neck), influence[i]);
            }
            for (int i = 0; i < frames.Length; i++)
            {
                int k = i * 3;
                var a = vertices[triangles[k]]; var b = vertices[triangles[k + 1]]; var c = vertices[triangles[k + 2]];
                var ab = b - a; var ac = c - a;
                var x = Normalize(ab); var y = Normalize(Vector3.Cross(x, ac)); var z = Vector3.Cross(x, y);
                var p = (a + b + c) / 3;
                var scale = (ab.magnitude + Mathf.Abs(Vector3.Dot(z, ac))) * .5f;
                frames[i] = new Frame { center = new Vector4(p.x,p.y,p.z,scale), x=x, y=y, z=z };
            }
            // Project each triangle frame onto the depth axis once, then sort by each splat's center.
            for (int i = 0; i < frames.Length; i++)
            {
                ref Frame f = ref frames[i];
                f.x.w = -Vector3.Dot(forward, f.x) * f.center.w;
                f.y.w = -Vector3.Dot(forward, f.y) * f.center.w;
                f.z.w = -Vector3.Dot(forward, f.z) * f.center.w;
            }
            for (int i = 0; i < frames.Length; i++) faceDepth[i] = -Vector3.Dot(forward, frames[i].center);
            float near = float.PositiveInfinity, far = float.NegativeInfinity;
            for (int i = 0; i < SplatCount; i++)
            {
                int id = bindings[i];
                ref Frame f = ref frames[id];
                var local = localCenters[i];
                float d = depth[i] = faceDepth[id] + local.x*f.x.w + local.y*f.y.w + local.z*f.z.w;
                near = Mathf.Min(near, d); far = Mathf.Max(far, d);
            }
            // Stable 16-bit depth buckets, back to front. Avoid comparison-sorting
            // 89k splats every time the tracking pose changes.
            Array.Clear(depthCounts, 0, depthCounts.Length);
            float multiplier = (DepthBuckets - 1) / Mathf.Max(far - near, .00001f);
            for (int i = 0; i < SplatCount; i++)
            {
                int key = Mathf.Clamp((int)((depth[i] - near) * multiplier), 0, DepthBuckets - 1);
                depthKeys[i] = (ushort)key;
                depthCounts[key]++;
            }
            int cursor = 0;
            for (int i = 0; i < depthCounts.Length; i++)
            {
                int amount = depthCounts[i]; depthCounts[i] = cursor; cursor += amount;
            }
            for (int i = 0; i < SplatCount; i++) order[depthCounts[depthKeys[i]]++] = i;
            frameBuffer.SetData(frames); orderBuffer.SetData(order);
        }

        readonly float[] faceDepth = new float[FaceCount];
        static Vector3 Normalize(Vector3 v) => v / Mathf.Sqrt(Mathf.Max(v.sqrMagnitude, 1e-20f));
        static float ReadFloat(BinaryReader reader, float min, float max)
        {
            float v = reader.ReadSingle();
            if (!(v >= min && v <= max)) throw new InvalidDataException("Invalid 306 rig number.");
            return v;
        }
        static Vector3 ReadVector(BinaryReader reader, float bound) => new Vector3(ReadFloat(reader,-bound,bound), ReadFloat(reader,-bound,bound), ReadFloat(reader,-bound,bound));
        static Vector4 Read4(BinaryReader reader, float bound, float minW, float maxW) => new Vector4(ReadFloat(reader,-bound,bound), ReadFloat(reader,-bound,bound), ReadFloat(reader,-bound,bound), ReadFloat(reader,minW,maxW));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (node) node.SetActive(false);
            if (hidden)
                for (int i = 0; i < sources.Length; i++) if (sources[i]) sources[i].forceRenderingOff = wasHidden[i];
            frameBuffer?.Dispose(); points?.Dispose(); coefficients?.Dispose(); bindingBuffer?.Dispose(); orderBuffer?.Dispose();
            Destroy(node); Destroy(mesh); Destroy(material);
        }

        static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
