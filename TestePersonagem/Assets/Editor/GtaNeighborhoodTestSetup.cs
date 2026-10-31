using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// A compact reference assembly built from the recovered scenery, never from new building modules.
public static class GtaNeighborhoodTestSetup
{
    const string ScenePath="Assets/Scenes/GTABairroProntoTeste.unity";
    const string Generated="Assets/GTABairroProntoTeste";
    const string Review="../tools/gta-neighborhood-review";
    static readonly Rect SourceArea=Rect.MinMaxRect(-310,-180,125,215);
    static readonly string[] RoadNames={"sc1_rd_r1_01","sc1_rd_r1_02","sc1_rd_r1_03","sc1_rd_r1_04","sc1_rd_r1_04x","sc1_rd_r1_07","sc1_rd_r2_03","sc1_rd_r2_10","sc1_rd_r2_12"};
    static readonly Dictionary<string,GameObject> groups=new Dictionary<string,GameObject>();
    static readonly Dictionary<Vector2Int,Vector3> nodes=new Dictionary<Vector2Int,Vector3>();
    static readonly Dictionary<Vector2Int,Vector2Int> parents=new Dictionary<Vector2Int,Vector2Int>();
    static readonly List<Vector2Int> connected=new List<Vector2Int>();
    static readonly Vector3 Rebase=new Vector3(92.5f,0,-17.5f);
    static Transform city;
    static string[] protectedPaths={"Assets/Scenes/TestePersonagem.unity","Assets/Scenes/GTAMapaTeste.unity","Assets/GTARecoveredMap/GTARecoveredGeometry.unity","Assets/Prefabs/Jogador.prefab","ProjectSettings/EditorBuildSettings.asset","ProjectSettings/QualitySettings.asset"};

    [MenuItem("Horizonte/Mapa GTA recuperado/Criar bairro com pecas prontas - separado")]
    public static void Create()
    {
        Directory.CreateDirectory(Generated+"/Meshes");Directory.CreateDirectory(Generated+"/Materials");Directory.CreateDirectory(Generated+"/Assemblies");Directory.CreateDirectory(Review);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var before=protectedPaths.ToDictionary(p=>p,Hash);
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/GTAMapaTeste.unity");
        var source=scene.GetRootGameObjects().Single(o=>o.name=="Mapa GTA recuperado");
        city=new GameObject("Bairro GTA - conjuntos prontos - TESTE SEPARADO").transform;
        groups.Clear();
        foreach(string name in new[]{"02","03","11","12","Ruas"})
        {var group=new GameObject(name=="Ruas"?"Ruas originais conectadas":"Quadra GTA "+name);group.transform.SetParent(city,false);groups[name]=group;}
        int recovered=0,clipped=0,duplicates=0;
        foreach(var renderer in source.GetComponentsInChildren<MeshRenderer>().ToArray())
        {
            string group=Group(renderer.name);
            bool road=RoadNames.Contains(renderer.name)||renderer.name.StartsWith("sc1_rd_decals",StringComparison.OrdinalIgnoreCase);
            if(group==null&&!road)continue;
            var obj=renderer.gameObject;var filter=obj.GetComponent<MeshFilter>();
            if(road)
            {
                var rect=Rect.MinMaxRect(renderer.bounds.min.x,renderer.bounds.min.z,renderer.bounds.max.x,renderer.bounds.max.z);
                if(!rect.Overlaps(SourceArea))continue;
                bool inside=SourceArea.Contains(rect.min)&&SourceArea.Contains(rect.max);
                if(!inside)
                {
                    var mesh=GtaNeighborhoodMeshClip.Clip(filter.sharedMesh,obj.transform,SourceArea,renderer.name+"_limite_teste");
                    if(!mesh)continue;
                    string path=Generated+"/Meshes/"+renderer.name+"_test.asset";
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
                    obj.transform.SetParent(null,true);obj.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);obj.transform.localScale=Vector3.one;
                    filter.sharedMesh=mesh;var col=obj.GetComponent<MeshCollider>();if(col)col.sharedMesh=mesh;clipped++;
                }
                group="Ruas";
            }
            obj.transform.SetParent(groups[group].transform,true);recovered++;
        }
        // Three alias copies in the source repeat identical geometry within centimetres/metres.
        foreach(var group in groups.Values)
        {
            var list=group.GetComponentsInChildren<MeshFilter>().OrderBy(f=>f.name.StartsWith("SC",StringComparison.Ordinal)?0:1).ToArray();
            for(int i=0;i<list.Length;i++)for(int j=i+1;j<list.Length;j++)
            {
                if(!list[i]||!list[j]||list[i].sharedMesh.name!=list[j].sharedMesh.name||list[i].sharedMesh.vertexCount!=list[j].sharedMesh.vertexCount)continue;
                var a=list[i].GetComponent<Renderer>();var b=list[j].GetComponent<Renderer>();
                if(Vector3.Distance(a.bounds.center,b.bounds.center)<1.5f&&(a.bounds.size-b.bounds.size).magnitude<.1f&&Quaternion.Angle(a.transform.rotation,b.transform.rotation)<2)
                {Object.DestroyImmediate(list[j].gameObject);duplicates++;}
            }
        }
        foreach(var old in scene.GetRootGameObjects())if(old!=city.gameObject)Object.DestroyImmediate(old);
        Physics.SyncTransforms();
        BuildNavigation();
        if(connected.Count<150)throw new Exception("Pouca area acessivel conectada: "+connected.Count);
        var spawn=nodes[connected.OrderBy(k=>nodes[k].sqrMagnitude).First()]+Vector3.up*.06f;
        var checkpoints=ChooseCheckpoints(44);
        var destinations=new List<GtaNeighborhoodPreview.Destination>();
        destinations.Add(new GtaNeighborhoodPreview.Destination{name="Rua principal",position=spawn+Rebase,yaw=45});
        foreach(var pair in groups.Where(p=>p.Key!="Ruas"))
        {
            var renderers=pair.Value.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            Vector3 desired=bounds.center;
            var nearest=connected.OrderBy(k=>XZ(nodes[k]-desired)).First();
            if(XZ(nodes[nearest]-desired)>120*120)throw new Exception("Quadra sem acesso ao percurso conectado: "+pair.Key);
            destinations.Add(new GtaNeighborhoodPreview.Destination{name=pair.Key=="02"?"Oficina e comercio":pair.Key=="03"?"Rua residencial":pair.Key=="11"?"Residencias, lojas e garagem":"Igreja e quadra norte",position=nodes[nearest]+Rebase+Vector3.up*.06f,yaw=45});
        }
        var walk=FindShortWalk();
        var supportMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GTARecoveredMap/Restored/Materials/City_Grass_0.mat");
        GtaNeighborhoodSupportTerrain.Create(city,SourceArea,nodes.Values.ToArray(),p=>Floor(p,out var floor)?(float?)floor.point.y:null,supportMaterial,Generated+"/Meshes");
        Physics.SyncTransforms();
        var boundary=CreateBoundaryMaterial();BuildBoundary(boundary);
        foreach(var corner in new[]{new Vector3(-300,0,-170),new Vector3(115,0,-170),new Vector3(-300,0,205),new Vector3(115,0,205)})
            if(Floor(corner,out var floor))checkpoints.Add(floor.point);
        foreach(var pair in groups.Where(p=>p.Key!="Ruas"))CenterPivot(pair.Value);
        foreach(var pair in groups.Where(p=>p.Key!="Ruas"))
            PrefabUtility.SaveAsPrefabAsset(pair.Value,Generated+"/Assemblies/QuadraGTA_"+pair.Key+".prefab");
        city.position=Rebase;
        var map=city.gameObject.AddComponent<NeighborhoodMap>();map.halfExtent=235;map.minY=-25;map.maxY=100;map.groundLevel=spawn.y-.06f;
        map.spawnPosition=spawn+Rebase;map.isOfficialCity=false;map.mapCameraHeight=160;map.displayName="Bairro GTA · Teste de conjuntos prontos";map.worldCode="gta_bairro_pronto_local";
        var playerObject=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"));
        playerObject.name="Jogador - Bairro GTA de teste";
        var player=playerObject.GetComponent<ThirdPersonPlayer>();var controller=playerObject.GetComponent<CharacterController>();controller.enabled=false;
        player.spawnPosition=map.spawnPosition;player.spawnYaw=45;playerObject.transform.SetPositionAndRotation(player.spawnPosition,Quaternion.Euler(0,45,0));controller.enabled=true;
        var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener),typeof(ThirdPersonCamera)).GetComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.12f;camera.farClipPlane=750;camera.fieldOfView=58;
        var orbit=camera.GetComponent<ThirdPersonCamera>();orbit.target=player.transform;orbit.yaw=45;orbit.pitch=16;player.cameraTransform=camera.transform;
        ConfigureLighting();
        var preview=city.gameObject.AddComponent<GtaNeighborhoodPreview>();preview.player=player;preview.reviewDirectory=Path.GetFullPath(Review);
        preview.originalAssemblies=groups.Values.ToArray();preview.sourceNames=new[]{"sc1_02 + terreno/detalhes","sc1_03 + terreno/detalhes","sc1_11/SC11 + terreno/detalhes","sc1_12/SC12 + terreno/detalhes","9 trechos de rua GTA + decalques existentes"};
        preview.floorCheckpoints=checkpoints.Select((p,i)=>Point(city,"Piso conectado "+i,p+Rebase+Vector3.up*.06f)).ToArray();
        preview.walkCheckpoints=walk.Select((p,i)=>Point(city,"Caminhada "+i,p+Rebase+Vector3.up*.06f)).ToArray();
        preview.destinations=destinations.ToArray();preview.districtBounds=new Bounds(new Vector3(0,15,0),new Vector3(SourceArea.width,80,SourceArea.height));
        preview.reviewCameras=ReviewCameras(destinations);
        GtaCityEnvironment.ConfigureSurfaces(city);
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        foreach(var pair in before)if(Hash(pair.Key)!=pair.Value)throw new Exception("Arquivo protegido alterado: "+pair.Key);
        File.WriteAllText(Review+"/setup.json",JsonUtility.ToJson(new SetupReport {
            scene=ScenePath,sourceScene="Assets/Scenes/GTAMapaTeste.unity",sourceObjects=recovered-duplicates,duplicatesRemoved=duplicates,roadMeshesTrimmed=clipped,
            readyBlocks=4,renderers=city.GetComponentsInChildren<MeshRenderer>().Length,colliders=city.GetComponentsInChildren<Collider>().Length,
            walkableGridSamples=nodes.Count,connectedGridSamples=connected.Count,gridSpacing=4,checkedFloors=checkpoints.Count,
            playableMetres=new Vector2(SourceArea.width,SourceArea.height),spawn=player.spawnPosition,officialUnchanged=true,
            notes="Predios, ruas, terrenos e detalhes recuperados. Arranjo interno das quatro quadras preservado para verificar reutilizacao e alinhamento. Terreno de apoio, limites de teste e recorte das pontas das ruas complementam o recorte. Sem novos predios procedurais."
        },true));
        Debug.Log("GTA_READY_NEIGHBORHOOD_OK objects="+(recovered-duplicates)+" connectedSamples="+connected.Count+" trimmedRoads="+clipped);
    }
    [Serializable] class SetupReport
    {public string scene,sourceScene,notes;public int sourceObjects,duplicatesRemoved,roadMeshesTrimmed,readyBlocks,renderers,colliders,walkableGridSamples,connectedGridSamples,checkedFloors;public float gridSpacing;public Vector2 playableMetres;public Vector3 spawn;public bool officialUnchanged;}
    static string Group(string name)
    {
        name=name.ToLowerInvariant();foreach(string code in new[]{"02","03","11","12"})
            if(name.StartsWith("sc1_"+code+"_")||name.StartsWith("sc"+code+"_"))return code;return null;
    }
    static bool Floor(Vector3 point,out RaycastHit floor)
    {
        floor=default;float highest=-100;
        foreach(var hit in Physics.RaycastAll(new Vector3(point.x,18,point.z),Vector3.down,50,1,QueryTriggerInteraction.Ignore))
        {
            string name=hit.collider.name.ToLowerInvariant();bool ground=name.StartsWith("sc1_rd_r")||name.Contains("ground")||name.Contains("grnd")||name.Contains("gnd");
            if(!ground||hit.normal.y<.82f||hit.point.y>8||hit.point.y< -18||hit.point.y<=highest)continue;
            if(Physics.CheckCapsule(hit.point+Vector3.up*.36f,hit.point+Vector3.up*1.54f,.26f,1,QueryTriggerInteraction.Ignore))continue;
            highest=hit.point.y;floor=hit;
        }
        return highest> -99;
    }
    static void BuildNavigation()
    {
        nodes.Clear();parents.Clear();connected.Clear();
        for(int x=0;x<Mathf.FloorToInt(SourceArea.width/4);x++)for(int z=0;z<Mathf.FloorToInt(SourceArea.height/4);z++)
        {var p=new Vector3(SourceArea.xMin+2+x*4,0,SourceArea.yMin+2+z*4);if(Floor(p,out var hit))nodes.Add(new Vector2Int(x,z),hit.point);}
        var seed=nodes.OrderBy(p=>p.Value.sqrMagnitude).First().Key;var queue=new Queue<Vector2Int>();queue.Enqueue(seed);parents[seed]=seed;
        var directions=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down,new Vector2Int(1,1),new Vector2Int(1,-1),new Vector2Int(-1,1),new Vector2Int(-1,-1)};
        while(queue.Count>0)
        {
            var key=queue.Dequeue();connected.Add(key);
            foreach(var d in directions)
            {
                var next=key+d;if(parents.ContainsKey(next)||!nodes.ContainsKey(next))continue;
                Vector3 a=nodes[key],b=nodes[next];if(Mathf.Abs(a.y-b.y)>.27f)continue;
                var delta=b-a;float length=delta.magnitude;
                if(Physics.CapsuleCast(a+Vector3.up*.58f,a+Vector3.up*1.65f,.25f,delta.normalized,length,1,QueryTriggerInteraction.Ignore))continue;
                bool supported=true;for(int i=1;i<=4;i++)if(!Floor(Vector3.Lerp(a,b,i/4f),out var f)||Mathf.Abs(f.point.y-Mathf.Lerp(a.y,b.y,i/4f))>.24f){supported=false;break;}
                if(!supported)continue;parents[next]=key;queue.Enqueue(next);
            }
        }
    }
    static List<Vector3> ChooseCheckpoints(int maximum)
    {
        var selected=new List<Vector3>{nodes[connected[0]]};
        while(selected.Count<maximum)
        {
            var point=connected.Select(k=>nodes[k]).OrderByDescending(p=>selected.Min(s=>XZ(p-s))).First();
            if(selected.Min(p=>XZ(p-point))<12*12)break;selected.Add(point);
        }
        return selected;
    }
    static Vector3[] FindShortWalk()
    {
        foreach(var key in connected.OrderBy(k=>nodes[k].sqrMagnitude))
        {
            var a=nodes[key];foreach(var dir in new[]{Vector3.right,Vector3.forward,Vector3.left,Vector3.back})
            {
                Vector3 b=a+dir*3;bool clear=true;
                for(int i=0;i<=12;i++)if(!Floor(Vector3.Lerp(a,b,i/12f),out var f)||Mathf.Abs(f.point.y-a.y)>.06f){clear=false;break;}
                if(clear&&!Physics.CapsuleCast(a+Vector3.up*.36f,a+Vector3.up*1.54f,.26f,dir,3,1,QueryTriggerInteraction.Ignore))return new[]{a,b};
            }
        }
        throw new Exception("Percurso curto livre ausente.");
    }
    static float XZ(Vector3 p)=>p.x*p.x+p.z*p.z;
    static void CenterPivot(GameObject group)
    {
        var renderers=group.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        var children=group.transform.Cast<Transform>().ToArray();foreach(var child in children)child.SetParent(null,true);
        group.transform.position=new Vector3(bounds.center.x,0,bounds.center.z);foreach(var child in children)child.SetParent(group.transform,true);
    }
    static Transform Point(Transform parent,string name,Vector3 p){var go=new GameObject(name);go.transform.SetParent(parent,true);go.transform.position=p;return go.transform;}
    static Material CreateBoundaryMaterial()
    {
        string path=Generated+"/Materials/LimiteTeste.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(mat,path);}mat.color=new Color(.25f,.31f,.27f);mat.SetFloat("_Glossiness",.05f);EditorUtility.SetDirty(mat);return mat;
    }
    static void BuildBoundary(Material material)
    {
        var root=new GameObject("Limites fisicos do bairro de teste");root.transform.SetParent(city,false);
        foreach(bool horizontal in new[]{true,false})foreach(float side in new[]{-1f,1f})
        {
            float start=horizontal?SourceArea.xMin:SourceArea.yMin,end=horizontal?SourceArea.xMax:SourceArea.yMax;
            float fixedValue=horizontal?(side<0?SourceArea.yMin+1.5f:SourceArea.yMax-1.5f):(side<0?SourceArea.xMin+1.5f:SourceArea.xMax-1.5f);
            for(float v=start+1.5f;v<end-1;v+=3)
            {
                Vector3 p=horizontal?new Vector3(v,0,fixedValue):new Vector3(fixedValue,0,v);if(!Floor(p,out var floor))continue;
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Limite de teste - apoiado no piso";go.transform.SetParent(root.transform,false);go.transform.position=floor.point+Vector3.up*.6f;go.transform.localScale=horizontal?new Vector3(3.02f,1.2f,.22f):new Vector3(.22f,1.2f,3.02f);go.GetComponent<Renderer>().sharedMaterial=material;go.isStatic=true;
            }
        }
    }
    static void ConfigureLighting()
    {
        var sun=new GameObject("Sol bairro teste",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.05f;sun.color=new Color(1,.94f,.85f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-32,0);sun.shadowBias=.025f;sun.shadowNormalBias=.18f;RenderSettings.sun=sun;
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.63f,.69f,.77f);RenderSettings.ambientEquatorColor=new Color(.49f,.5f,.52f);RenderSettings.ambientGroundColor=new Color(.3f,.3f,.31f);
        var sky=Resources.Load<Material>("Environment/LateAfternoonSkyMaterial");if(!sky)sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/GTARecoveredMap/Restored/CitySky.mat");RenderSettings.skybox=sky;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=230;RenderSettings.fogEndDistance=660;RenderSettings.fogColor=new Color(.65f,.72f,.78f);
    }
    static Camera[] ReviewCameras(List<GtaNeighborhoodPreview.Destination> destinations)
    {
        var over=CameraAt("Vista aerea do bairro",new Vector3(0,600,0),Vector3.zero);over.orthographic=true;over.orthographicSize=250;
        var views=new List<Camera>{over,CameraAt("Panorama dos conjuntos GTA",new Vector3(265,170,-295),new Vector3(-25,3,10))};
        foreach(var dest in destinations.Skip(1))
        {
            var point=dest.position;Vector3 eye=point+new Vector3(12,8,-18);views.Add(CameraAt(dest.name,eye,point+new Vector3(-4,2,4)));
        }
        // Same street-level view as the recovered source, with its ready shop facade.
        views.Add(CameraAt("Rua e fachadas prontas",Rebase+new Vector3(4,2.2f,-8),Rebase+new Vector3(-12,2.1f,8)));
        return views.ToArray();
    }
    static Camera CameraAt(string name,Vector3 p,Vector3 focus)
    {var camera=new GameObject(name,typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.transform.position=p;camera.transform.LookAt(focus);camera.farClipPlane=1500;camera.nearClipPlane=.15f;camera.fieldOfView=57;return camera;}
    static string Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));}
    public static void CreateAndBuildWindows(){Create();BuildWindows();}
    public static void BuildWindows()
    {
        string output="Builds/GTABairroProntoTeste/BairroGTA.exe";Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Falha na build do bairro GTA separado.");
    }
}
