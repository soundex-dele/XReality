using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XReality
{
    // Reuses the existing presenter rig as deformation input, but not as visible geometry.
    public sealed class GaussianPresenter : IDisposable
    {
        sealed class Part
        {
            public Renderer renderer;
            public SkinnedMeshRenderer skinned;
            public Mesh snapshot;
            public readonly List<Vector3> vertices = new();
            public GaussianPresenterAsset.Splat[] splats;
            public bool wasHidden;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GpuSplat
        {
            public Vector4 centerOpacity, tangent, bitangent, color;
        }

        struct Order : IComparable<Order>
        {
            public int index;
            public float depth;
            public int CompareTo(Order other)
            {
                int result = other.depth.CompareTo(depth);
                return result != 0 ? result : index.CompareTo(other.index);
            }
        }

        readonly List<Part> parts = new();
        readonly Transform torso;
        readonly Camera camera;
        GpuSplat[] data, sorted;
        Order[] order;
        Mesh mesh;
        Material material;
        GameObject node;
        GraphicsBuffer buffer;
        bool replaced, disposed;
        public int Count => data?.Length ?? 0;

        GaussianPresenter(Transform torso, Camera camera)
        {
            this.torso = torso;
            this.camera = camera;
        }

        public static Mesh SourceMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skinned
            ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;

        public static bool TryCreate(GaussianPresenterAsset asset, Transform torso, Camera camera, out GaussianPresenter presenter)
        {
            presenter = null;
            if (!asset || !torso || !camera || !asset.shader || !asset.shader.isSupported ||
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 ||
                asset.version != GaussianPresenterAsset.FormatVersion || asset.parts == null || asset.parts.Length == 0)
                return false;

            var candidate = new GaussianPresenter(torso, camera);
            try
            {
                candidate.Initialize(asset);
                presenter = candidate;
                return true;
            }
            catch (Exception error)
            {
                candidate.Dispose();
                Debug.LogWarning("3DGS sample could not start: " + error.GetType().Name);
                return false;
            }
        }

        void Initialize(GaussianPresenterAsset asset)
        {
            var renderers = torso.GetComponentsInChildren<Renderer>();
            var seen = new HashSet<Renderer>();
            int count = 0;
            foreach (var source in asset.parts)
            {
                if (source == null || !source.mesh || source.splats == null || source.splats.Length == 0 ||
                    source.splats.Length > GaussianPresenterAsset.MaxSplats - count)
                    throw new ArgumentException("Invalid sample parts.");
                var renderer = Array.Find(renderers, r => r.name == source.rendererName && SourceMesh(r) == source.mesh);
                if (!renderer || !seen.Add(renderer)) throw new ArgumentException("Sample does not match the rig.");
                int vertexCount = source.mesh.vertexCount;
                foreach (var s in source.splats)
                    if ((uint)s.a >= (uint)vertexCount || (uint)s.b >= (uint)vertexCount || (uint)s.c >= (uint)vertexCount ||
                        !(s.barycentric.x >= 0 && s.barycentric.y >= 0 && s.barycentric.x + s.barycentric.y <= 1) ||
                        !(s.sigma > 0 && s.sigma <= .1f && s.opacity > 0 && s.opacity <= 1))
                        throw new ArgumentException("Invalid Gaussian binding.");
                var part = new Part { renderer = renderer, skinned = renderer as SkinnedMeshRenderer,
                    splats = source.splats, wasHidden = renderer.forceRenderingOff };
                parts.Add(part);
                if (part.skinned) part.snapshot = new Mesh();
                else source.mesh.GetVertices(part.vertices);
                count += source.splats.Length;
            }
            if (count == 0 || seen.Count != renderers.Length) throw new ArgumentException("Incomplete sample rig.");

            data = new GpuSplat[count];
            sorted = new GpuSplat[count];
            order = new Order[count];
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<GpuSplat>());
            material = new Material(asset.shader);
            material.SetBuffer("_AvatarSplats", buffer);
            // Static indexed quads: SV_VertexID selects a sorted Gaussian and one of its four corners.
            mesh = new Mesh { name = "Gaussian presenter quads", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = new Vector3[count * 4];
            var indices = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v + 2; indices[t + 4] = v + 1; indices[t + 5] = v + 3;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            // Node is rooted at the torso and the existing rig stays within this conservative bound.
            mesh.bounds = new Bounds(new Vector3(0, .9f, 0), new Vector3(3, 3, 3));
            mesh.UploadMeshData(true);
            node = new GameObject("3DGS presenter") { layer = parts[0].renderer.gameObject.layer };
            node.transform.SetParent(torso, false);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var draw = node.AddComponent<MeshRenderer>();
            draw.sharedMaterial = material;
            draw.shadowCastingMode = ShadowCastingMode.Off;
            draw.receiveShadows = false;
            draw.lightProbeUsage = LightProbeUsage.Off;
            draw.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Refresh();
            replaced = true;
            foreach (var part in parts) part.renderer.forceRenderingOff = true;
        }

        public void Refresh()
        {
            var cameraForward = camera.transform.forward;
            // Depth differs only by a constant if we omit the camera position.
            var worldToTorso = torso.worldToLocalMatrix;
            var localDepth = torso.localToWorldMatrix.transpose.MultiplyVector(cameraForward);
            int index = 0;
            foreach (var p in parts)
            {
                if (p.skinned)
                {
                    p.skinned.BakeMesh(p.snapshot);
                    p.snapshot.GetVertices(p.vertices);
                }
                var matrix = worldToTorso * p.renderer.localToWorldMatrix;
                foreach (var s in p.splats)
                {
                    Vector3 a = p.vertices[s.a], u = p.vertices[s.b] - a, v = p.vertices[s.c] - a;
                    var center = matrix.MultiplyPoint3x4(a + u * s.barycentric.x + v * s.barycentric.y);
                    var tangent = matrix.MultiplyVector(u).normalized;
                    var normal = Vector3.Cross(tangent, matrix.MultiplyVector(v).normalized).normalized;
                    var bitangent = Vector3.Cross(normal, tangent);
                    data[index] = new GpuSplat
                    {
                        centerOpacity = new Vector4(center.x, center.y, center.z, s.opacity),
                        tangent = tangent * s.sigma, bitangent = bitangent * s.sigma, color = s.color
                    };
                    order[index] = new Order { index = index, depth = Vector3.Dot(center, localDepth) };
                    index++;
                }
            }
            Array.Sort(order);
            for (int i = 0; i < order.Length; i++) sorted[i] = data[order[i].index];
            buffer.SetData(sorted);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (node) node.SetActive(false);
            if (replaced)
                foreach (var part in parts)
                    if (part.renderer) part.renderer.forceRenderingOff = part.wasHidden;
            buffer?.Dispose();
            Destroy(node);
            Destroy(mesh);
            Destroy(material);
            foreach (var part in parts) Destroy(part.snapshot);
        }

        static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
