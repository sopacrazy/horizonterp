using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StairSetup
{
    [MenuItem("Teste/Suavizar escadas e compilar Windows")]
    public static void ApplyAndBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        var buildings=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t=>t.name.Contains("residencial")&&!t.name.Contains("volume")).ToArray();
        if(buildings.Length!=2)throw new Exception("Esperadas duas escadas residenciais");
        foreach(var building in buildings)ConfigureBuilding(building.gameObject);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
        Verify();PrototypeSetup.BuildWindows();
    }
    public static void ConfigureBuilding(GameObject building)
    {
        var filter=building.GetComponent<MeshFilter>();
        if(!filter)throw new Exception("Malha residencial não encontrada");
        var source=filter.sharedMesh;var vertices=source.vertices;var triangles=source.triangles;
        var noses=new Dictionary<float,float>();float width=0;
        for(int i=0;i<triangles.Length;i+=3)
        {
            Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
            var center=(a+b+c)/3;float span=Mathf.Max(a.x,b.x,c.x)-Mathf.Min(a.x,b.x,c.x);
            if(Vector3.Cross(b-a,c-a).normalized.y<.99f||center.y<.15f||center.y>.9f||Mathf.Abs(center.x)>.6f||center.z<0||center.z>2.5f||span<1.5f)continue;
            float level=Mathf.Round(center.y*1000)/1000;
            float nose=Mathf.Max(a.z,b.z,c.z);
            noses[level]=noses.ContainsKey(level)?Mathf.Max(noses[level],nose):nose;width=Mathf.Max(width,span);
        }
        if(noses.Count!=4)throw new Exception("Não foi possível identificar os quatro degraus: "+noses.Count);
        float meanZ=noses.Values.Average(),meanY=noses.Keys.Average(),covariance=0,variance=0;
        foreach(var pair in noses){covariance+=(pair.Value-meanZ)*(pair.Key-meanY);variance+=Mathf.Pow(pair.Value-meanZ,2);}
        float slope=covariance/variance,intercept=meanY-slope*meanZ;
        float bottomY=-building.transform.position.y;
        float bottomZ=(bottomY-intercept)/slope;
        float topZ=noses.Values.Min()-.44f;
        float topY=slope*topZ+intercept+.012f;
        string folder="Assets/DowntownCity/Collision";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/DowntownCity","Collision");
        // Preserve railings/walls, removing only tread/riser collision in the central flight.
        var remaining=new List<int>();
        for(int i=0;i<triangles.Length;i+=3)
        {
            var center=(vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3;
            bool flight=Mathf.Abs(center.x)<width*.45f&&center.z>topZ-.025f&&center.z<bottomZ+.025f&&center.y>bottomY-.05f&&center.y<topY+.025f;
            if(!flight)remaining.AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});
        }
        var collision=UnityEngine.Object.Instantiate(source);collision.name="Residential collision without treads";
        collision.subMeshCount=1;collision.triangles=remaining.ToArray();
        SaveMesh(collision,folder+"/ResidentialShell.asset");
        filter.GetComponent<MeshCollider>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/ResidentialShell.asset");
        var old=building.transform.Find("Colisao suave da escada");if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
        float half=width/2;
        var ramp=new Mesh {name="Smooth residential stair ramp"};
        ramp.vertices=new[]{new Vector3(-half,bottomY,bottomZ),new Vector3(half,bottomY,bottomZ),new Vector3(-half,topY,topZ),new Vector3(half,topY,topZ),
            new Vector3(-half,bottomY-.1f,bottomZ),new Vector3(half,bottomY-.1f,bottomZ),new Vector3(-half,bottomY-.1f,topZ),new Vector3(half,bottomY-.1f,topZ)};
        ramp.triangles=new[]{0,1,2,1,3,2, 4,0,6,0,2,6, 1,5,3,5,7,3, 2,3,6,3,7,6, 4,5,0,5,1,0, 6,7,4,7,5,4};
        ramp.RecalculateNormals();ramp.RecalculateBounds();SaveMesh(ramp,folder+"/ResidentialRamp.asset");
        var surface=new GameObject("Colisao suave da escada",typeof(MeshCollider));surface.transform.SetParent(building.transform,false);
        surface.isStatic=true;surface.GetComponent<MeshCollider>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/ResidentialRamp.asset");
        Debug.Log("STAIR_RAMP_READY "+building.name+" bottom="+bottomZ+" top="+topZ+" rise="+(topY-bottomY));
    }
    static void SaveMesh(Mesh mesh,string path)
    {
        var asset=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(asset){EditorUtility.CopySerialized(mesh,asset);UnityEngine.Object.DestroyImmediate(mesh);}
        else AssetDatabase.CreateAsset(mesh,path);
    }
    static void Verify()
    {
        Physics.SyncTransforms();int ramps=0;
        foreach(var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.name=="Colisao suave da escada"))
        {
            ramps++;var v=collider.sharedMesh.vertices;
            float previous=0;
            for(int i=1;i<=40;i++)
            {
                var point=collider.transform.TransformPoint(Vector3.Lerp((v[0]+v[1])/2,(v[2]+v[3])/2,i/41f));
                if(!Physics.Raycast(point+Vector3.up*3,Vector3.down,out var hit,4))throw new Exception("Rampa sem colisão");
                if(hit.collider!=collider||Mathf.Abs(hit.point.y-point.y)>.015f)throw new Exception("Colisão de degrau ainda sobrepõe a rampa: "+hit.collider.name);
                if(i>1&&Mathf.Abs(hit.point.y-previous)>.04f)throw new Exception("Descontinuidade vertical na rampa");previous=hit.point.y;
            }
        }
        if(ramps!=2)throw new Exception("Rampa residencial ausente");
        Debug.Log("STAIRS_VERIFIED: 80 surface samples, continuous rise and no tread collisions");
        var template=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>().GetComponent<CharacterController>();
        foreach(var ramp in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.name=="Colisao suave da escada"))
        foreach(float speed in new[]{1.65f,4.5f})foreach(bool descending in new[]{false,true})
        {
            var vertices=ramp.sharedMesh.vertices;
            var bottom=ramp.transform.TransformPoint((vertices[0]+vertices[1])/2);
            var top=ramp.transform.TransformPoint((vertices[2]+vertices[3])/2);
            var start=Vector3.Lerp(bottom,top,descending?.7f:.1f);
            var end=Vector3.Lerp(bottom,top,descending?.1f:.7f);
            var probe=new GameObject("Verificacao de movimento na escada");
            try
            {
                var controller=probe.AddComponent<CharacterController>();controller.height=template.height;controller.center=template.center;
                controller.radius=template.radius;controller.stepOffset=template.stepOffset;controller.skinWidth=template.skinWidth;controller.slopeLimit=template.slopeLimit;
                probe.transform.position=start+Vector3.up*(.12f-template.center.y+template.height/2);Physics.SyncTransforms();
                for(int i=0;i<30;i++)controller.Move(Vector3.down*.04f);
                var direction=Vector3.ProjectOnPlane(end-start,Vector3.up).normalized;
                float length=Vector3.ProjectOnPlane(end-start,Vector3.up).magnitude,maxRise=0,vertical=-2;
                for(float travelled=0;travelled<length;travelled+=speed/60)
                {
                    var before=probe.transform.position;
                    if(controller.isGrounded)vertical=-2;
                    vertical-=22f/60;
                    controller.Move((direction*speed+Vector3.up*vertical)/60);
                    maxRise=Mathf.Max(maxRise,Mathf.Abs(probe.transform.position.y-before.y));
                }
                if(maxRise>.08f)throw new Exception("Tranco no controlador sobre a rampa: "+maxRise);
                if(Vector3.ProjectOnPlane(probe.transform.position-start,Vector3.up).magnitude<length*.85f)throw new Exception("Colisão bloqueando escada");
                Debug.Log("STAIR_MOTION_OK speed="+speed+" descending="+descending+" maximum_vertical_step="+maxRise);
            }
            finally{UnityEngine.Object.DestroyImmediate(probe);}
        }
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        var lines=new List<string>();
        foreach(var building in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.Contains("residencial")&&!t.name.Contains("volume")))
        {
            lines.Add("BUILDING "+building.name+" "+building.position);
            foreach(var filter in building.GetComponentsInChildren<MeshFilter>())
            {
                lines.Add("MESH "+filter.name+" "+filter.GetComponent<Renderer>().bounds);
                var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
                var surfaces=new Dictionary<float,List<Vector3>>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(vertices[triangles[i]]);
                    var b=filter.transform.TransformPoint(vertices[triangles[i+1]]);
                    var c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                    var center=(a+b+c)/3;
                    if(Vector3.Cross(b-a,c-a).normalized.y<.95f||center.y<-.1f||center.y>2.5f||Mathf.Abs(center.z)>6.5f)continue;
                    float y=Mathf.Round(center.y*1000)/1000;
                    if(!surfaces.ContainsKey(y))surfaces[y]=new List<Vector3>();
                    surfaces[y].AddRange(new[]{a,b,c});
                }
                foreach(var pair in surfaces.OrderBy(p=>p.Key))
                {
                    var bounds=new Bounds(pair.Value[0],Vector3.zero);foreach(var v in pair.Value)bounds.Encapsulate(v);
                    lines.Add("LEVEL "+pair.Key+" min="+bounds.min.ToString("F4")+" max="+bounds.max.ToString("F4")+" triangles="+pair.Value.Count/3);
                }
            }
        }
        Directory.CreateDirectory("../tools/stair-review");File.WriteAllLines("../tools/stair-review/inspection.txt",lines);
        Debug.Log("STAIRS_INSPECTED");
    }
}
