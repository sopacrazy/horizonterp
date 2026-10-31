using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class GtaNeighborhoodCatalog
{
    const string Review="../tools/gta-neighborhood-review";
    [Serializable] public class Item { public string name,group,mesh;public Vector3 position,scale;public Quaternion rotation;public Bounds bounds;public int vertices,submeshes; }
    [Serializable] public class Group { public string name;public Bounds bounds;public int objects; }
    [Serializable] public class Report { public Item[] items;public Group[] groups; }
    public static void Inspect()
    {
        Directory.CreateDirectory(Review);
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GTAMapaTeste.unity");
        var root=scene.GetRootGameObjects().Single(g=>g.name=="Mapa GTA recuperado");
        GtaCityEnvironment.ConfigureSurfaces(root.transform);Physics.SyncTransforms();
        var items=root.GetComponentsInChildren<MeshRenderer>().Select(r=>new Item {
            name=r.name,group=Regex.Match(r.name,@"^sc1_[^_]+").Value,mesh=r.GetComponent<MeshFilter>().sharedMesh.name,
            position=r.transform.position,scale=r.transform.lossyScale,rotation=r.transform.rotation,bounds=r.bounds,
            vertices=r.GetComponent<MeshFilter>().sharedMesh.vertexCount,submeshes=r.GetComponent<MeshFilter>().sharedMesh.subMeshCount
        }).ToArray();
        var groups=items.GroupBy(i=>i.group).Select(g=>{var b=g.First().bounds;foreach(var i in g)b.Encapsulate(i.bounds);return new Group{name=g.Key,bounds=b,objects=g.Count()};}).ToArray();
        File.WriteAllText(Review+"/catalog.json",JsonUtility.ToJson(new Report{items=items,groups=groups},true));
        var camera=new GameObject("Camera temporaria de catalogo",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;
        camera.transform.SetPositionAndRotation(new Vector3(-253,1200,264),Quaternion.Euler(90,0,0));camera.orthographic=true;camera.orthographicSize=620;camera.farClipPlane=1800;camera.nearClipPlane=.3f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.23f,.25f);
        bool fog=RenderSettings.fog;RenderSettings.fog=false;var rt=new RenderTexture(2048,2048,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var texture=new Texture2D(2048,2048,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,2048,2048),0,0);texture.Apply();File.WriteAllBytes(Review+"/source-overview.png",texture.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);RenderSettings.fog=fog;
        Debug.Log("GTA_NEIGHBORHOOD_CATALOG_OK items="+items.Length);
    }
}
