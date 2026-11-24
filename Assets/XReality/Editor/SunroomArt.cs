using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace XReality.Editor
{
    internal static partial class StudioArt
    {
        // Dressing pieces don't participate in click/drag raycasts.
        static Transform Decor(string name, Transform room, Vector3 p, Vector3 scale, string pigment,
            PrimitiveType shape = PrimitiveType.Cube, float roll = 0)
        {
            var t = Part(name, room, p, scale, pigment, shape, roll);
            UnityEngine.Object.DestroyImmediate(t.GetComponent<Collider>());
            return t;
        }

        static void Sunroom(Transform room)
        {
            Decor("Warm plaster", room, V(0, 2.1f, 3), V(16, 4.5f, .20f), "DFCCB0");
            Decor("Floor foundation", room, V(0, -.21f, 1), V(16, .32f, 10), "9A654C");
            CedarFloor(room);
            Decor("Skirting", room, V(0,.095f,2.865f), V(16,.25f,.10f), "AF8060");
            Decor("Skirting cap", room, V(0,.218f,2.80f), V(16,.045f,.08f), "CCA284");

            // A shallow, layered scene inside the picture window, not a bright competing prop.
            var window = Joint("Picture window", room, V(1.57f,1.53f,2.78f));
            window.localScale = V(1,.76f,1);
            Decor("Outer surround",window,Vector3.zero,V(2.37f,1.47f,.16f),"926342");
            Decor("Window lining",window,V(0,0,-.095f),V(2.25f,1.34f,.045f),"E6BB90");
            Decor("Sky", window,V(0,0,-.125f),V(2.08f,1.18f,.032f),"ADCAC4");
            Decor("Sun",window,V(.59f,.315f,-.143f),V(.25f,.25f,.035f),"F4DB9B",PrimitiveType.Sphere);
            Decor("Cloud",window,V(-.53f,.34f,-.151f),V(.54f,.115f,.04f),"EEE3CC",PrimitiveType.Sphere);
            Decor("Distant hill",window,V(-.45f,-.43f,-.158f),V(1.02f,.32f,.042f),"83A89A",PrimitiveType.Sphere,9);
            Decor("Near hill",window,V(.38f,-.43f,-.184f),V(1.05f,.30f,.04f),"6E9483",PrimitiveType.Sphere,-7);
            Decor("Mullion",window,V(0,0,-.23f),V(.048f,1.27f,.085f),"E9C69C");
            Decor("Crossbar",window,V(0,-.12f,-.23f),V(2.13f,.045f,.085f),"E9C69C");
            Decor("Sill",window,V(0,-.715f,-.16f),V(2.49f,.11f,.41f),"A97553");

            var art = Joint("Botanical print", room, V(-.20f,1.59f,2.77f));
            Decor("Walnut frame", art, Vector3.zero, V(.69f,.88f,.08f), "875B47");
            Decor("Paper", art, V(0,0,-.048f), V(.59f,.77f,.028f), "EDD8B9");
            Stretch("Ink stem",art,V(-.10f,-.252f,-.075f),V(.15f,.256f,-.075f),.018f,"52766B");
            for (int i=0; i<5; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                Decor("Pressed leaf",art,V(-.061f+i*.045f+side*.061f,-.175f+i*.092f,-.079f),
                    V(.085f,.217f,.019f), i%2==0?"789986":"8DA77F", PrimitiveType.Sphere, side*-43);
            }

            var plant = Joint("Rubber plant", room, V(2.64f,0,2.04f));
            Decor("Terracotta saucer",plant,V(0,.010f,0),V(.57f,.027f,.57f),"9D6258",PrimitiveType.Cylinder);
            Decor("Terracotta pot",plant,V(0,.166f,0),V(.46f,.142f,.46f),"BE7E6A",PrimitiveType.Cylinder);
            Decor("Rounded pot rim",plant,V(0,.302f,0),V(.505f,.026f,.505f),"C99279",PrimitiveType.Cylinder);
            Decor("Dark soil",plant,V(0,.331f,0),V(.415f,.009f,.415f),"5C453B",PrimitiveType.Cylinder);
            for (int i=0;i<7;i++)
            {
                float a=i*2.39996f;
                var f=V(Mathf.Cos(a),0,Mathf.Sin(a));
                var tip=f*.28f+Vector3.up*(.48f+i*.073f);
                Stretch("Leaf stem",plant,V(0,.318f,0),tip,.021f,"4F7157");
                var leaf=Decor("Rubber plant leaf",plant,tip,V(.225f,.38f,.085f),i%2==0?"4C7D67":"759B79",PrimitiveType.Sphere);
                leaf.up=(f*.8f+Vector3.up).normalized;
            }

            Decor("Rug binding", room, V(.9f, -.012f, 0), V(4.71f, .033f, 3.61f), "42665E", PrimitiveType.Cylinder);
            Decor("Braided edge", room, V(.9f, -.003f, 0), V(4.60f, .028f, 3.50f), "96ACA0", PrimitiveType.Cylinder);
            Decor("Woven rug", room, V(.9f, .007f, 0), V(4.46f, .021f, 3.36f), "628D83", PrimitiveType.Cylinder);
        }

        static void CedarFloor(Transform room)
        {
            string[] cedar = { "BB8665", "B17C5D", "B58364", "B78462", "BF8D6A" };
            var seed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cube = seed.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(seed);
            for (int tint = 0; tint < cedar.Length; tint++)
            {
                string path = StudioProject.Asset($"Cedar-{tint}.asset");
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!mesh)
                {
                    var pieces = new List<CombineInstance>();
                    for (int row = 0; row < 13; row++)
                        for (int col = -3; col <= 3; col++)
                        {
                            if ((row * 3 + col + 5) % cedar.Length != tint) continue;
                            float x = col * 2.45f + (row % 2 == 0 ? 0 : 1.225f);
                            pieces.Add(new CombineInstance
                            {
                                mesh = cube,
                                transform = Matrix4x4.TRS(V(x, -.056f, 2.71f - row * .54f), Quaternion.identity,
                                    V(2.432f, .052f, .522f))
                            });
                        }
                    mesh = new Mesh { name = $"Cedar tint {tint}" };
                    mesh.CombineMeshes(pieces.ToArray());
                    AssetDatabase.CreateAsset(mesh, path);
                }
                var floor = Joint("Combined cedar floorboards", room, Vector3.zero);
                floor.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                floor.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Paint(cedar[tint]);
            }
        }

        static void YarnStrands(Transform toy)
        {
            // Curves follow the outside of the sphere rather than intersecting it in flat rings.
            const int points = 116;
            for (int strand=0; strand<4; strand++)
            {
                var curve = new GameObject("Wound yarn strand").AddComponent<LineRenderer>();
                curve.transform.SetParent(toy, false);
                curve.useWorldSpace = false;
                curve.sharedMaterial = Paint(strand % 2 == 0 ? "AA635D" : "ECB397");
                curve.widthMultiplier = .009f;
                curve.numCapVertices = 3;
                curve.positionCount = points;
                for (int i=0; i<points; i++)
                {
                    float t = i / (points - 1f);
                    float latitude = Mathf.Lerp(-1.37f,1.37f,t);
                    float azimuth = t * Mathf.PI * 6 + strand * Mathf.PI * .5f;
                    float r = .505f * Mathf.Cos(latitude);
                    curve.SetPosition(i, V(r * Mathf.Cos(azimuth), .505f * Mathf.Sin(latitude), r * Mathf.Sin(azimuth)));
                }
                curve.transform.localRotation = Quaternion.Euler(23,0,31);
            }
        }
    }
}
