using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Isolated import of the user-provided beach club. No GTA parser ships in the game.
public static class SakelandTestSetup
{
    const string Root="Assets/SakelandTest";
    const string Scene="Assets/Scenes/SakelandTeste.unity";
    const string Inputs="../tools/sakeland/processed";
    [Serializable] class Manifest {public Model[] models;public Placement[] placements;public Collision[] collision;public float[] origin;}
    [Serializable] class Model {public string name;public ShaderInfo[] shaders;public Geometry[] geometries;}
    [Serializable] class ShaderInfo {public string diffuse,normal;public int bucket;}
    [Serializable] class Geometry {public string mesh;public int shader,vertices,triangles;}
    [Serializable] class Placement {public string name;public float[] position,rotation;public float scaleXY,scaleZ;}
    [Serializable] class Collision {public string mesh;public int triangles;}
    static readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
    static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
    static Vector3 origin;
    const float SandHeight=-.65f;
    static Vector3 Swap(float[] p)=>new Vector3(p[0],p[2],p[1]);

    [MenuItem("Horizonte/Sakeland/Montar trecho de teste separado")]
    public static void Create()
    {
        if(!File.Exists(Inputs+"/manifest.json"))throw new Exception("Converta o pacote com tools/convert_sakeland.py primeiro.");
        Directory.CreateDirectory(Root+"/Meshes");Directory.CreateDirectory(Root+"/Materials");
        Directory.CreateDirectory("../tools/sakeland/review");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        meshes.Clear();materials.Clear();
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Inputs+"/manifest.json"));origin=Swap(manifest.origin);
        var normalNames=new HashSet<string>(manifest.models.SelectMany(m=>m.shaders).Where(s=>!string.IsNullOrEmpty(s.normal)).Select(s=>s.normal));
        foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{Root+"/Textures"}).Select(AssetDatabase.GUIDToAssetPath))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=normalNames.Contains(Path.GetFileNameWithoutExtension(path))?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.maxTextureSize=1024;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;
            importer.alphaIsTransparency=importer.textureType!=TextureImporterType.NormalMap;
            importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        }
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Sakeland - Trecho de teste");
        var map=root.AddComponent<NeighborhoodMap>();map.halfExtent=55;map.groundLevel=-.65f;
        map.worldCode="sakeland_local_test";map.displayName="Sakeland · Prévia local";
        var modelRoot=new GameObject("Modelos convertidos do pacote");modelRoot.transform.SetParent(root.transform,false);
        var mainDeck=new GameObject("Clube e estruturas conectadas");mainDeck.transform.SetParent(modelRoot.transform,false);
        var djDeck=new GameObject("Palco DJ e estruturas conectadas");djDeck.transform.SetParent(modelRoot.transform,false);
        var looseObjects=new List<GameObject>();
        var mainParts=new HashSet<string>{"beach_tiki_bar_01","beach_tiki_bar_02","beach_tiki_bar_01_lights","beach_tiki_bar_roof",
            "beach_tiki_bar_bamboo","beach_tiki_bar_fence","beach_tiki_bar_sun_shades_a","beach_tiki_bar_sun_shades_b"};
        var definitions=manifest.models.ToDictionary(m=>m.name);
        int triangles=0,instances=0;
        foreach(var placement in manifest.placements)
        {
            // Terrain-blend panels require GTA's terrain shader and do not belong in this first slice.
            if(placement.name.Contains("view_blends")||placement.name.EndsWith("decals"))continue;
            var definition=definitions[placement.name];
            var obj=new GameObject(placement.name);obj.transform.SetParent(modelRoot.transform,false);obj.isStatic=true;
            obj.transform.position=Swap(placement.position)-origin;
            var q=placement.rotation;obj.transform.rotation=new Quaternion(q[0],q[1],q[2],q[3]);
            obj.transform.localScale=new Vector3(placement.scaleXY,placement.scaleZ,placement.scaleXY);
            if(placement.name=="beach_tiki_bar_bamboo_fence")
                obj.transform.rotation=Quaternion.Euler(0,obj.transform.eulerAngles.y,0);
            foreach(var geometry in definition.geometries)
            {
                var child=new GameObject(geometry.mesh,typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(obj.transform,false);child.isStatic=true;
                var mesh=MeshFromInput(geometry.mesh);child.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=child.GetComponent<MeshRenderer>();renderer.sharedMaterial=MaterialFromInput(definition.shaders[geometry.shader]);
                renderer.shadowCastingMode=placement.name.Contains("lights")||placement.name.Contains("rugwool")?ShadowCastingMode.Off:ShadowCastingMode.On;
                if(placement.name=="beach_tiki_bar_dj_stand"||placement.name=="beach_tiki_bar_rock_pool")
                    child.AddComponent<MeshCollider>().sharedMesh=mesh;
                triangles+=mesh.triangles.Length/3;
            }
            // Preserve the playable floor and walls from the supplied collision, avoiding render-mesh colliders.
            if(placement.name.Contains("palm"))
            {
                var capsule=obj.AddComponent<CapsuleCollider>();capsule.radius=.19f;capsule.height=2;capsule.center=new Vector3(0,1,0);
            }
            if(placement.name.Contains("bench"))
            {
                var box=obj.AddComponent<BoxCollider>();box.center=new Vector3(0,.48f,0);box.size=new Vector3(1.8f,.96f,.65f);
            }
            if(placement.name=="beach_tiki_bar_bamboo_fence")
            {
                var filters=obj.GetComponentsInChildren<MeshFilter>();var bounds=filters[0].sharedMesh.bounds;
                foreach(var filter in filters)bounds.Encapsulate(filter.sharedMesh.bounds);
                var box=obj.AddComponent<BoxCollider>();box.center=bounds.center;box.size=Vector3.Max(bounds.size,new Vector3(.08f,.08f,.08f));
            }
            var lod=obj.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.012f,obj.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();
            if(mainParts.Contains(placement.name))obj.transform.SetParent(mainDeck.transform,true);
            else if(placement.name.StartsWith("beach_tiki_bar_dj_"))obj.transform.SetParent(djDeck.transform,true);
            else looseObjects.Add(obj);
            instances++;
        }
        var collisionRoot=new GameObject("Colisao original do clube");collisionRoot.transform.SetParent(mainDeck.transform,false);collisionRoot.transform.position=-origin;
        foreach(var collision in manifest.collision)
        {
            var obj=new GameObject(collision.mesh,typeof(MeshCollider));obj.transform.SetParent(collisionRoot.transform,false);obj.isStatic=true;
            obj.GetComponent<MeshCollider>().sharedMesh=MeshFromInput(collision.mesh);
        }
        var sand=Plain("Areia",new Color(1,.94f,.8f),.025f);
        sand.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/cs_islx_bchsand_5.png");
        sand.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/cs_islx_bchsand_5_n.png"));
        if(!sand.mainTexture||!sand.GetTexture("_BumpMap"))throw new Exception("Texturas de areia do pacote ausentes.");
        sand.mainTextureScale=new Vector2(55,55);sand.SetTextureScale("_BumpMap",new Vector2(55,55));
        sand.EnableKeyword("_NORMALMAP");sand.SetFloat("_BumpScale",.65f);EditorUtility.SetDirty(sand);
        var beach=Box("Praia de apoio para o teste",root.transform,new Vector3(-10,SandHeight-.4f,-10),new Vector3(110,.8f,110),sand);
        var supports=new List<Transform>();
        GroundObject(mainDeck,true,supports);GroundObject(djDeck,true,supports);
        foreach(var obj in looseObjects.OrderBy(o=>o.name.Contains("rugwool")?1:0))GroundObject(obj,false,supports);
        var water=Plain("Agua de teste",new Color(.07f,.29f,.36f),.7f);
        Box("Mar cenografico",root.transform,new Vector3(0,-.72f,0),new Vector3(2000,.05f,2000),water,false);
        var boundary=Plain("Limite discreto",new Color(.45f,.33f,.2f),.05f);
        foreach(float sign in new[]{-1f,1f})
        {
            Box("Limite da praia",root.transform,new Vector3(-10+sign*54.5f,-.05f,-10),new Vector3(.35f,1.2f,109),boundary);
            Box("Limite da praia",root.transform,new Vector3(-10,-.05f,-10+sign*54.5f),new Vector3(109,1.2f,.35f),boundary);
        }
        ConfigureLighting(root);
        var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"));
        player.name="Jogador - Teste Sakeland";
        var movement=player.GetComponent<ThirdPersonPlayer>();var controller=player.GetComponent<CharacterController>();
        // The same 1.8 m character and animation controller used in the city.
        controller.enabled=false;movement.spawnPosition=new Vector3(0,-.6f,-17);movement.spawnYaw=0;
        player.transform.SetPositionAndRotation(movement.spawnPosition,Quaternion.identity);controller.enabled=true;
        var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener),typeof(ThirdPersonCamera)).GetComponent<Camera>();camera.tag="MainCamera";
        camera.clearFlags=CameraClearFlags.Skybox;camera.nearClipPlane=.15f;camera.farClipPlane=220;camera.fieldOfView=55;
        var orbit=camera.GetComponent<ThirdPersonCamera>();orbit.target=player.transform;orbit.distance=4.2f;orbit.pitch=16;orbit.yaw=0;movement.cameraTransform=camera.transform;
        var preview=root.AddComponent<SakelandPreview>();preview.startAtNight=true;
        preview.sandRenderer=beach.GetComponent<Renderer>();
        Physics.SyncTransforms();
        // Pick a clear short path on the imported deck for the actual walking regression.
        var walkPath=FindWalkPath(controller);
        preview.walkCheckpoints=walkPath.Select((p,i)=>Point(root.transform,"Percurso "+i,p)).ToArray();
        var entry=FindEntryRoute();
        preview.entryCheckpoints=entry.Select((p,i)=>Point(root.transform,"Entrada pela areia "+i,p)).ToArray();
        movement.spawnPosition=walkPath[0];controller.enabled=false;player.transform.position=walkPath[0];controller.enabled=true;
        float footAdjustment=FitFeet(movement,controller);
        var obstacle=Box("Banco de apoio - colisao de teste",root.transform,new Vector3(12,-.15f,-18),new Vector3(2,1,.65f),Plain("Madeira apoio",new Color(.32f,.16f,.08f),.1f));
        preview.collisionObstacle=obstacle.GetComponent<Collider>();
        supports.Add(Point(obstacle.transform,"Apoio no chao",new Vector3(12,SandHeight,-18)));
        preview.supportAnchors=supports.ToArray();
        preview.reviewCameras=new[]{ReviewCamera("Panorama",new Vector3(40,38,-54),new Vector3(-8,1,-9)),
            ReviewCamera("Clube",new Vector3(12,3,-17),new Vector3(-1,1.7f,-2)),
            ReviewCamera("Deck",new Vector3(-6,2.5f,-12),new Vector3(1,1.4f,0)),
            ReviewCamera("Areia e apoios",new Vector3(-14,1.6f,-19),new Vector3(-23,.3f,-4)),
            ReviewCamera("Bancos e baleia",new Vector3(-18,1.8f,-11),new Vector3(-25,.3f,-3))};
        QualitySettings.pixelLightCount=4;QualitySettings.shadowDistance=35;QualitySettings.antiAliasing=2;
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,Scene);AssetDatabase.SaveAssets();
        File.WriteAllText("../tools/sakeland/review/setup.txt","Sakeland isolated test\nInstances="+instances+"\nInstanced triangles="+triangles+"\nSource collision triangles="+manifest.collision.Sum(c=>c.triangles)+"\nGrounded objects/groups="+supports.Count+"\nFoot adjustment="+footAdjustment+"\nSpawn="+movement.spawnPosition+"\nNo login, no backend, no APK. Original city scene preserved.\n");
        Debug.Log("SAKELAND_SETUP_OK instances="+instances+" triangles="+triangles+" spawn="+movement.spawnPosition);
    }

    static Vector3 LowestPoint(GameObject obj)
    {
        var lowest=new Vector3(0,float.PositiveInfinity,0);
        foreach(var filter in obj.GetComponentsInChildren<MeshFilter>())
            foreach(var vertex in filter.sharedMesh.vertices)
            {var p=filter.transform.TransformPoint(vertex);if(p.y<lowest.y)lowest=p;}
        if(float.IsInfinity(lowest.y))throw new Exception("Objeto sem base visual: "+obj.name);
        return lowest;
    }

    static Vector3 SupportPoint(GameObject obj)
    {
        // Long canopy poles and buried foundations must not lift their platforms off the beach.
        if(obj.name=="Palco DJ e estruturas conectadas")
            return LowestPoint(obj.transform.Cast<Transform>().First(t=>t.name=="beach_tiki_bar_dj_stand").gameObject);
        if(obj.name=="beach_tiki_bar_rock_pool")
        {
            var deck=obj.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name=="beach_tiki_bar_rock_pool_02");
            return deck.sharedMesh.vertices.Select(deck.transform.TransformPoint).OrderBy(p=>p.y).First();
        }
        return LowestPoint(obj);
    }

    static void GroundObject(GameObject obj,bool assembly,List<Transform> supports)
    {
        var foot=SupportPoint(obj);float surface=SandHeight;
        Physics.SyncTransforms();
        if(!assembly)
            foreach(var hit in Physics.RaycastAll(new Vector3(foot.x,5,foot.z),Vector3.down,8,1).OrderBy(h=>h.distance))
            {
                if(hit.transform.IsChildOf(obj.transform)||hit.normal.y<.7f||hit.point.y>1.1f)continue;
                // Only structural floors may support scenery; avoid snapping onto another prop.
                if(hit.collider.name=="Praia de apoio para o teste"||hit.collider.name.StartsWith("club_collision")||hit.collider.name.StartsWith("beach_tiki_bar_dj_stand_"))
                {surface=hit.point.y;break;}
            }
        // The lowest collision triangles are thin trim below the first usable tread.
        // Bury the foundation 32 cm to leave that tread within the 25 cm controller step.
        float embed=obj.name=="Clube e estruturas conectadas"?.32f:0;
        obj.transform.position+=Vector3.up*(surface-foot.y-embed);
        supports.Add(Point(obj.transform,"Apoio no chao",SupportPoint(obj)+Vector3.up*embed));
        Physics.SyncTransforms();
    }

    static float FitFeet(ThirdPersonPlayer player,CharacterController controller)
    {
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim");
        if(!idle)throw new Exception("Pose parada ausente para conferir apoio dos pes.");
        idle.SampleAnimation(player.animator.gameObject,0);
        float min=player.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled).Min(s=>CharacterGeometry.GetWorldBounds(s).min.y);
        float capsuleFeet=player.transform.position.y+controller.center.y-controller.height*.5f;
        // CharacterController rests above the contact plane by its skin margin.
        // Compensate in this scene's visual child so soles meet the actual sand/deck.
        float delta=capsuleFeet+.005f-controller.skinWidth-min;
        player.animator.transform.position+=Vector3.up*delta;
        return delta;
    }

    static Vector3[] FindEntryRoute()
    {
        var directions=new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left,
            new Vector3(1,0,1).normalized,new Vector3(-1,0,1).normalized,new Vector3(1,0,-1).normalized,new Vector3(-1,0,-1).normalized};
        for(float z=-15;z<=8;z+=.75f)for(float x=-13;x<=13;x+=.75f)
        {
            var start=new Vector3(x,0,z);
            if(!WalkSurface(start,out var first)||first.collider.name!="Praia de apoio para o teste")continue;
            foreach(var direction in directions)
            {
                float previous=first.point.y;var route=new List<Vector3>{first.point+Vector3.up*.04f};
                bool clear=true;RaycastHit last=first;
                for(int i=1;i<=24;i++)
                {
                    if(!WalkSurface(start+direction*(i*.2f),out last)||Mathf.Abs(last.point.y-previous)>.225f)
                    {clear=false;break;}
                    if(Physics.CheckCapsule(last.point+Vector3.up*.55f,last.point+Vector3.up*1.55f,.22f,1,QueryTriggerInteraction.Ignore))
                    {clear=false;break;}
                    previous=last.point.y;
                    if(i%3==0)route.Add(last.point+Vector3.up*.04f);
                    // Stop on the landing; extending a straight path farther can run into the bar counter.
                    if(i>=8&&last.collider.name.StartsWith("club_collision")&&last.point.y>.15f&&last.point.y-first.point.y>.75f)
                    {
                        if(i%3!=0)route.Add(last.point+Vector3.up*.04f);
                        return route.ToArray();
                    }
                }
            }
        }
        throw new Exception("Nao foi encontrado acesso caminhavel da areia aos degraus do clube.");
    }

    static bool WalkSurface(Vector3 point,out RaycastHit floor)
    {
        foreach(var hit in Physics.RaycastAll(new Vector3(point.x,3,point.z),Vector3.down,5,1).OrderBy(h=>h.distance))
            if(hit.normal.y>.8f&&hit.point.y<.9f&&(hit.collider.name=="Praia de apoio para o teste"||hit.collider.name.StartsWith("club_collision")))
            {floor=hit;return true;}
        floor=default;return false;
    }

    static Vector3[] FindWalkPath(CharacterController controller)
    {
        Physics.SyncTransforms();
        for(float z=-9;z<=5;z+=1.2f)for(float x=-7;x<=7;x+=1.2f)
        {
            var from=new Vector3(x,10,z);var to=new Vector3(x,10,z+2);
            if(!ImportedFloor(from,out var a)||!ImportedFloor(to,out var b))continue;
            if(Mathf.Abs(a.point.y-b.point.y)>.12f)continue;
            var start=a.point+Vector3.up*.04f;var end=b.point+Vector3.up*.04f;
            bool clear=true;
            for(int i=0;i<=8;i++)
            {
                var foot=Vector3.Lerp(start,end,i/8f);
                if(Physics.CheckCapsule(foot+Vector3.up*.3f,foot+Vector3.up*1.55f,.25f,1,QueryTriggerInteraction.Ignore)){clear=false;break;}
            }
            if(clear)return new[]{start,end,new Vector3(9,-.6f,-18),new Vector3(-25,-.6f,-30)};
        }
        throw new Exception("Nao foi encontrado percurso livre no deck importado.");
    }

    static bool ImportedFloor(Vector3 from,out RaycastHit floor)
    {
        foreach(var hit in Physics.RaycastAll(from,Vector3.down,15,1).OrderBy(h=>h.distance))
            if(hit.collider.name.StartsWith("club_collision")&&hit.normal.y>.9f&&hit.point.y<.9f&&hit.point.y>-.5f)
            {floor=hit;return true;}
        floor=default;return false;
    }

    static Transform Point(Transform parent,string name,Vector3 position)
    {var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.position=position;return obj.transform;}
    static Camera ReviewCamera(string name,Vector3 position,Vector3 target)
    {var camera=new GameObject("QA "+name,typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=55;camera.farClipPlane=250;camera.nearClipPlane=.1f;camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));return camera;}

    static Mesh MeshFromInput(string name)
    {
        if(meshes.TryGetValue(name,out var existing))return existing;
        string input=Inputs+"/"+name+".meshbin";
        string optimized="../tools/sakeland/optimized/"+name+".meshbin";if(File.Exists(optimized))input=optimized;
        using(var reader=new BinaryReader(File.OpenRead(input)))
        {
            int vertexCount=reader.ReadInt32(),indexCount=reader.ReadInt32();
            var vertices=new Vector3[vertexCount];var normals=new Vector3[vertexCount];var uv=new Vector2[vertexCount];
            for(int i=0;i<vertexCount;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
            var indices=new int[indexCount];for(int i=0;i<indexCount;i++)indices[i]=reader.ReadInt32();
            var mesh=new Mesh{name=name,indexFormat=vertexCount>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();mesh.RecalculateTangents();
            string path=Root+"/Meshes/"+name+".asset";existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);}else{existing=mesh;AssetDatabase.CreateAsset(mesh,path);}
            meshes.Add(name,existing);return existing;
        }
    }
    static Material MaterialFromInput(ShaderInfo info)
    {
        string key=(string.IsNullOrEmpty(info.diffuse)?"Solid":info.diffuse)+"_"+info.bucket;
        if(materials.TryGetValue(key,out var known))return known;
        var material=Plain(key,Color.white,.16f);
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+info.diffuse+".png");
        material.mainTexture=texture;
        var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+info.normal+".png");
        if(normal){material.SetTexture("_BumpMap",normal);material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",.65f);}
        bool foliage=info.diffuse!=null&&(info.diffuse.Contains("palm")||info.diffuse.Contains("leaf")||info.diffuse.Contains("leaves")||info.diffuse.Contains("ravenala")||info.diffuse.Contains("cycas"));
        if(foliage||info.bucket==1||info.bucket==3)
        {
            material.SetFloat("_Mode",1);material.SetFloat("_Cutoff",.4f);material.SetInt("_SrcBlend",(int)BlendMode.One);material.SetInt("_DstBlend",(int)BlendMode.Zero);material.SetInt("_ZWrite",1);
            material.EnableKeyword("_ALPHATEST_ON");material.SetOverrideTag("RenderType","TransparentCutout");material.renderQueue=2450;
        }
        if(info.diffuse!=null&&(info.diffuse.StartsWith("nexus_lightprop")||info.diffuse=="fairy_lights_d"))
        {
            // Reuse the city light-cycle convention, including texture-masked emission.
            material.name="City_Lamp_Sakeland_"+key;material.SetTexture("_EmissionMap",texture);
            material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(3.2f,2.35f,1.25f));
        }
        material.doubleSidedGI=true;material.enableInstancing=true;EditorUtility.SetDirty(material);materials.Add(key,material);return material;
    }
    static Material Plain(string name,Color color,float smoothness)
    {string path=Root+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}material.color=color;material.SetFloat("_Glossiness",smoothness);material.enableInstancing=true;EditorUtility.SetDirty(material);return material;}
    static GameObject Box(string name,Transform parent,Vector3 position,Vector3 size,Material material,bool collision=true)
    {var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.position=position;obj.transform.localScale=size;obj.GetComponent<Renderer>().sharedMaterial=material;obj.isStatic=true;if(!collision)Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;}

    static void ConfigureLighting(GameObject root)
    {
        var sun=new GameObject("Sol da praia",typeof(Light)).GetComponent<Light>();sun.transform.rotation=Quaternion.Euler(52,-35,0);sun.type=LightType.Directional;sun.intensity=1.12f;sun.color=new Color(1,.95f,.86f);sun.shadows=LightShadows.Soft;
        sun.shadowBias=.01f;sun.shadowNormalBias=.025f;
        var moon=new GameObject("Lua da praia",typeof(Light)).GetComponent<Light>();moon.transform.SetParent(root.transform,false);moon.type=LightType.Directional;moon.transform.rotation=Quaternion.Euler(32,-65,0);moon.shadows=LightShadows.Soft;
        moon.shadowBias=.01f;moon.shadowNormalBias=.025f;
        var cycle=root.AddComponent<CityDayNight>();cycle.sun=sun;cycle.moon=moon;cycle.startAtNight=true;
        cycle.daySky=AssetDatabase.LoadAssetAtPath<Material>("Assets/DowntownCity/Materials/City_Sky.mat");cycle.nightSky=AssetDatabase.LoadAssetAtPath<Material>("Assets/DowntownCity/Materials/City_NightSky.mat");
        if(!cycle.daySky||!cycle.nightSky)throw new Exception("Ceus do bairro ausentes.");
        var lights=new List<Light>();
        foreach(var p in new[]{new Vector3(0,4.3f,0),new Vector3(-23,4,-21),new Vector3(-10,4,-12),new Vector3(8,4,-9)})
        {var light=new GameObject("Luz quente do clube",typeof(Light)).GetComponent<Light>();light.transform.SetParent(root.transform,false);light.transform.position=p;light.type=LightType.Point;light.range=14;light.intensity=1.4f;light.color=new Color(1,.72f,.43f);light.shadows=LightShadows.None;lights.Add(light);}
        cycle.streetLights=lights.ToArray();cycle.SetNight(true);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=100;RenderSettings.fogEndDistance=210;
    }
    [MenuItem("Horizonte/Sakeland/Gerar previa Windows separada")]
    public static void CreateAndBuildWindows()
    {
        Create();
        string path="Builds/Sakeland/TesteSakeland.exe";Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=path,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Falha ao compilar previa Sakeland: "+report.summary.result);
        Debug.Log("SAKELAND_BUILD_OK");EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
    }
}
