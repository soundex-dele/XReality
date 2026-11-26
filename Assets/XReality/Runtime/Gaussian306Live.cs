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
        const int LegacySplats = 89021, LegacyVertices = 5143, LegacyFaces = 10144, ChannelCount = 4;
        const int MaxSplats = 200000, MaxVertices = 20000, MaxFaces = 40000;
        int SplatCount => bindings.Length;
        int FaceCount => frames.Length;
        const int DepthBuckets = 65536;
        Vector3 neck = new Vector3(0, -.081f, -.052f);

        [StructLayout(LayoutKind.Sequential)]
        struct Frame
        {
            public Vector4 center, x, y, z;
        }

        readonly Camera stage;
        readonly Renderer[] sources;
        readonly bool[] wasHidden;
        Vector3[] rest, vertices, localCenters;
        float[] influence, depth, faceDepth;
        readonly Vector3[][] deltas = new Vector3[ChannelCount][];
        int[] triangles, bindings, order;
        Frame[] frames;
        ushort[] depthKeys;
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
            if (reader.ReadInt32() != 0x4c363033)
                throw new InvalidDataException("Not an XRG avatar.");
            int version = reader.ReadInt32();
            int splats = reader.ReadInt32(), verts = reader.ReadInt32(), faces = reader.ReadInt32();
            if ((version != 1 && version != 2) || splats < 1 || splats > MaxSplats ||
                verts < 3 || verts > MaxVertices || faces < 1 || faces > MaxFaces ||
                (version == 1 && (splats != LegacySplats || verts != LegacyVertices || faces != LegacyFaces)) ||
                file.Length != (version == 2 ? 32L : 20L) + verts * 64L + faces * 12L + splats * 324L)
                throw new InvalidDataException("Unsupported XRG avatar dimensions or version.");
            if (version == 2) neck = ReadVector(reader, 1);
            void ReadIndices(int count, int max, int[] destination)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = reader.ReadInt32();
                    if ((uint)index >= (uint)max) throw new InvalidDataException("Out of range XRG binding.");
                    if (destination != null) destination[i] = index;
                }
            }
            void ReadPayload(Frame[] data, Vector4[] colors)
            {
                bool populate = data != null;
                for (int i = 0; i < verts; i++)
                {
                    var value = ReadVector(reader, 1);
                    if (populate) rest[i] = value;
                }
                for (int i = 0; i < verts; i++)
                {
                    float value = ReadFloat(reader, 0, 1);
                    if (populate) influence[i] = value;
                }
                for (int c = 0; c < ChannelCount; c++)
                    for (int i = 0; i < verts; i++)
                    {
                        var value = ReadVector(reader, .25f);
                        if (populate) deltas[c][i] = value;
                    }
                ReadIndices(faces * 3, verts, populate ? triangles : null);
                ReadIndices(splats, faces, populate ? bindings : null);
                for (int i = 0; i < splats; i++)
                {
                    var value = new Frame {
                        center = Read4(reader, 1000, 0, 1), x = Read4(reader, 1000, 0, 0),
                        y = Read4(reader, 1000, 0, 0), z = Read4(reader, 1000, 0, 0) };
                    if (!populate) continue;
                    data[i] = value;
                    localCenters[i] = value.center;
                }
                for (int i = 0; i < splats * 16; i++)
                {
                    var value = Read4(reader, 100, 0, 0);
                    if (populate) colors[i] = value;
                }
            }
            // Validate the bounded on-disk payload before allocating model arrays or GPU buffers.
            long payload = file.Position;
            ReadPayload(null, null);
            file.Position = payload;
            rest = new Vector3[verts]; vertices = new Vector3[verts]; influence = new float[verts];
            for (int c = 0; c < ChannelCount; c++) deltas[c] = new Vector3[verts];
            triangles = new int[faces * 3]; frames = new Frame[faces]; faceDepth = new float[faces];
            bindings = new int[splats]; localCenters = new Vector3[splats]; order = new int[splats];
            depth = new float[splats]; depthKeys = new ushort[splats];
            var data = new Frame[splats];
            var colors = new Vector4[splats * 16];
            ReadPayload(data, colors);

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
            Debug.Log($"XR_XRG_LOADED: version {version}, {splats} splats, {verts} vertices, {faces} faces");
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
                vertices[i] = Vector3.LerpUnclamped(v, neck + rotation * (v - neck), influence[i]);
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
