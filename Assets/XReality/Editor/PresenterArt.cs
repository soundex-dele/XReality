using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XReality.Editor
{
    internal static partial class StudioArt
    {
        const string PresenterAssets = "Assets/XReality/Presenter/";
        static readonly Vector3 PresenterPivot = V(0, 1.0065f, .0183f);
        static readonly string[] SkinShapes = { "BlinkLeft", "BlinkRight", "JawOpen", "SmileLeft", "SmileRight", "BrowLeft", "BrowRight", "LipRound", "LipPress" };
        static readonly string[] ToothShapes = { "JawOpen" };
        static readonly string[] BrowShapes = { "BrowLeft", "BrowRight" };

        // The two scenes share the authored bust; only the live scene owns a tracking controller.
        static void Presenter(LiveStudio live, Transform root, Vector3 origin)
        {
            var torso = Joint("Torso", root, origin);
            var head = Joint("Neck pivot", torso, PresenterPivot);
            var skin = Rigged("Skin", torso, head, .27f, SkinShapes);
            var teeth = Rigged("Teeth", torso, head, .32f, ToothShapes);
            Rigid(live ? "Jacket" : "JacketShowcase", torso, Vector3.zero, .18f, materialName: "Jacket");
            Rigid("Hair", head, -PresenterPivot, .32f, true);
            var brows = Rigged("Brows", torso, head, .22f, BrowShapes, cutout: true);
            if (!live)
            {
                Rigid("Eyes", head, -PresenterPivot, .54f);
                return;
            }
            live.eyeLeft = PresenterEye(head, true);
            live.eyeRight = PresenterEye(head, false);
            live.torso = torso;
            live.head = head;
            live.faceSurface = skin;
            live.teeth = teeth;
            live.brows = brows;
        }

        static Transform PresenterEye(Transform head, bool left)
        {
            string name = left ? "EyeLeft" : "EyeRight";
            // Sphere fit to the baked low-poly eye surface, not the bounds of its visible cap.
            var pivot = V(left ? .17891f : -.17891f, 1.56707f, -.30785f);
            var path = StudioProject.Asset("Presenter-" + name + ".asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                var source = PresenterMesh("Eyes");
                var verts = source.vertices;
                var norms = source.normals;
                var uv = source.uv;
                var tris = source.triangles;
                var remap = new Dictionary<int, int>();
                var outVerts = new List<Vector3>();
                var outNormals = new List<Vector3>();
                var outUv = new List<Vector2>();
                var outIndices = new List<int>();
                for (int i = 0; i < tris.Length; i++)
                {
                    int j = tris[i];
                    bool inEye = verts[j].x > 0;
                    if (inEye != (verts[tris[i - i % 3]].x > 0))
                        throw new InvalidDataException("Presenter eyes have joined geometry.");
                    if (inEye != left) continue;
                    if (!remap.TryGetValue(j, out int index))
                    {
                        remap.Add(j, index = outVerts.Count);
                        outVerts.Add(verts[j] - pivot);
                        outNormals.Add(norms[j]);
                        outUv.Add(uv[j]);
                    }
                    outIndices.Add(index);
                }
                if (outIndices.Count < 3) throw new InvalidDataException("Missing " + name + " surface.");
                mesh = new Mesh { name = "Presenter " + name };
                mesh.SetVertices(outVerts);
                mesh.SetNormals(outNormals);
                mesh.SetUVs(0, outUv);
                mesh.SetTriangles(outIndices, 0);
                AssetDatabase.CreateAsset(mesh, path);
            }
            var node = Joint(name, head, pivot - PresenterPivot);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = PresenterMaterial("Eyes", .54f);
            return node;
        }

        static Mesh PresenterMesh(string name, string[] shapeNames = null)
        {
            bool rigged = shapeNames != null;
            var path = StudioProject.Asset("Presenter-" + name + ".asset");
            var cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (cached) return cached;
            var payload = AssetDatabase.LoadAssetAtPath<TextAsset>(PresenterAssets + name + ".bytes");
            if (!payload) throw new IOException("Missing baked presenter mesh: " + name);
            using var reader = new BinaryReader(new MemoryStream(payload.bytes, false));
            if (reader.ReadUInt32() != 0x31505258) throw new InvalidDataException("Invalid presenter mesh format: " + name);
            int count = reader.ReadInt32(), indices = reader.ReadInt32(), shapes = reader.ReadInt32();
            if (count < 1 || count > 65535 || indices < 3 || indices > 300000 || indices % 3 != 0 ||
                shapes != (shapeNames?.Length ?? 0) ||
                reader.BaseStream.Length != 16L + count * (36L + 24L * shapes) + 4L * indices)
                throw new InvalidDataException("Invalid presenter mesh dimensions: " + name);
            Vector3 Read3() => V(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var weights = rigged ? new BoneWeight[count] : null;
            for (int i = 0; i < count; i++)
            {
                vertices[i] = Read3();
                normals[i] = Read3();
                uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                float head = reader.ReadSingle();
                if (rigged)
                    weights[i] = head >= .5f
                        ? new BoneWeight { boneIndex0 = 1, weight0 = head, boneIndex1 = 0, weight1 = 1 - head }
                        : new BoneWeight { boneIndex0 = 0, weight0 = 1 - head, boneIndex1 = 1, weight1 = head };
            }
            var triangles = new int[indices];
            for (int i = 0; i < indices; i++) triangles[i] = reader.ReadInt32();
            var mesh = new Mesh { name = "Presenter " + name, vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            if (rigged)
            {
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(-PresenterPivot) };
                mesh.boneWeights = weights;
            }
            for (int shape = 0; shape < shapes; shape++)
            {
                var displacement = new Vector3[count];
                var shading = new Vector3[count];
                for (int i = 0; i < count; i++) { displacement[i] = Read3(); shading[i] = Read3(); }
                mesh.AddBlendShapeFrame(shapeNames[shape], 100, displacement, shading, null);
            }
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material PresenterMaterial(string name, float shine, bool cutout = false)
        {
            var path = StudioProject.Asset("Presenter-" + name + ".mat");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PresenterAssets + name + ".png");
            if (!texture) throw new IOException("Missing presenter texture: " + name);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Presenter " + name };
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", shine);
            if (name == "Skin") material.SetColor("_BaseColor", C("F4F0EC"));
            if (name == "Teeth") material.SetColor("_BaseColor", C("E9DBCF"));
            if (cutout)
            {
                material.SetFloat("_AlphaClip", 1);
                material.SetFloat("_Cutoff", name == "Brows" ? .29f : .39f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.SetFloat("_AlphaToMask", 1);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void Rigid(string name, Transform parent, Vector3 offset, float shine, bool cutout = false, string materialName = null)
        {
            var node = Joint(name, parent, offset);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = PresenterMesh(name);
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = PresenterMaterial(materialName ?? name, shine, cutout);
        }

        static SkinnedMeshRenderer Rigged(string name, Transform torso, Transform head, float shine, string[] shapes, bool cutout = false)
        {
            var renderer = Joint(name, torso, Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = PresenterMesh(name, shapes);
            renderer.sharedMaterial = PresenterMaterial(name, shine, cutout);
            renderer.bones = new[] { torso, head };
            renderer.rootBone = torso;
            var bounds = renderer.sharedMesh.bounds;
            bounds.Expand(.7f);
            renderer.localBounds = bounds;
            return renderer;
        }
    }
}
