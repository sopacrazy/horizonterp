using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Reviewed replacement models kept an older model's COL. Preserve the
// visible structures; retain the obsolete colliders disabled and editable.
public static class LosSantosPatrolStructureRepair
{
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-patrol";
    const string SourceModels = "Assets/GTASanAndreasModFull/ImportedModels/";
    sealed class Target
    {
        public string model, sector;
        public int id;
        public Vector3 position;
        public float roofMin, roofMax;
        public float minimumProbeNormal=.9f, floorMin, floorMax;
        public bool preferHighestFirst;
    }
    static readonly Target[] Targets = {
        new Target { model="tvstudio01_LAw2", id=6490, sector="GTASASector_p2_m6", position=new Vector3(717.484375f,20.296875f,1357.3046875f), roofMin=13.8f, roofMax=22 },
        // The flat23m surface is internal. Actual pitched roof faces have
        // source normals .771..886 and reach29.5146m. Verify that roof plus
        // five separate .9-normal witnesses on the exposed eastern paving.
        new Target { model="country_law2", id=6522, sector="GTASASector_p2_m5", position=new Vector3(702.8671875f,20.90625f,1276.03125f), roofMin=25, roofMax=29.6f, minimumProbeNormal=.72f, preferHighestFirst=true, floorMin=16.4f, floorMax=16.9f },
        new Target { model="BeachBlok02_LAe2", id=17551, sector="GTASASector_p11_m6", position=new Vector3(2844.2421875f,20.140625f,1531.8828125f), roofMin=20.5f, roofMax=22.5f },
        new Target { model="spray01_LAwN", id=5886, sector="GTASASector_p3_m4", position=new Vector3(1005.25f,36.375f,1011.2109375f), roofMin=41.7f, roofMax=41.9f }
    };
    [Serializable] sealed class Sidecar { public MaterialSlot[] materials; }
    [Serializable] sealed class MaterialSlot { public string material; public string[] textures; public float[] color; }
    [Serializable] sealed class OriginalCollider
    {
        public string hierarchy, type;
        public bool enabledBefore, enabledAfter;
        public Bounds boundsBefore;
    }
    [Serializable] sealed class Probe
    {
        public Vector3 point;
        public float expectedY, beforeTopY, beforeNearestGap, afterTopY, afterNearestGap;
        public float minimumNormalY;
        public bool beforeHit, afterHit, passed;
        public string beforeCollider, afterCollider;
    }
    [Serializable] sealed class GroundProbe
    {
        public Vector3 location;
        public string beforeOtherCollider, afterOtherCollider;
        public bool externalFloorPresent, passed;
        public float beforeOtherY, afterOtherY, beforeWorldTopY, afterWorldTopY;
    }
    [Serializable] sealed class Item
    {
        public string model, sector, objectName, visualBeforeSha256, visualAfterSha256;
        public Vector3 position;
        public Bounds visualBounds;
        public int originalColliders, disabledOriginalColliders, oldVisualColliders, visualColliders, addedVisualColliders;
        public int reviewedMaterialSlots, sourceAlphaSlots, fenceOrGlassSlots;
        public OriginalCollider[] original;
        public Probe[] roof;
        public Probe[] exposedFloor;
        public GroundProbe[] adjacentGround;
        public bool passed;
    }
    [Serializable] sealed class Report
    {
        public string generatedUtc, method, backup;
        public bool passed, idempotent;
        public int disabledOriginalColliders, addedVisualColliders;
        public Item[] firstPass, repeatPass;
        public string[] changedFilesOnRepeat, errors;
    }
    struct Surface
    {
        public Vector3 a,b,c,normal;
        public float area;
        public string material;
    }

    public static bool UsesVisualCollision(string modelName) => Targets.Any(t => string.Equals(t.model, modelName, StringComparison.OrdinalIgnoreCase));

    // Root may call this before reading the obsolete source COL on regeneration.
    public static int AddVisualCollision(GameObject instance)
    {
        int count=0;
        foreach (var filter in VisibleFilters(instance.transform))
        {
            var existing=filter.GetComponents<MeshCollider>();
            if (existing.Length>1 || (existing.Length==1 && existing[0].sharedMesh!=filter.sharedMesh))
                throw new InvalidOperationException("Unexpected existing visual collision: "+instance.name+"/"+filter.name);
            var collider=existing.FirstOrDefault();
            if (!collider) collider=filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh=filter.sharedMesh; collider.convex=false; collider.isTrigger=false; collider.enabled=true;
            count++;
        }
        if(count==0)throw new InvalidOperationException("No visible collision mesh: "+instance.name);
        return count;
    }

    [MenuItem("Horizonte/Los Santos/Patrulha - alinhar colisao de estruturas verificadas")]
    public static void ApplyAndVerify()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        bool oldBackfaces=Physics.queriesHitBackfaces;
        Directory.CreateDirectory(Output);
        var report=new Report { generatedUtc=DateTime.UtcNow.ToString("o"),
            method="Only reviewed tvstudio01_LAw2#6490, country_law2#6522, BeachBlok02_LAe2#17551 and spray01_LAwN#5886; preserve visual geometry/materials/transforms. Disable editable original COL components; attach exact visible meshes. Five exposed source roof samples and four neighbouring external-floor probes per model. Country_law2 pitched roof reaches29.5146m: source normal>=.72 (actual .771..886), unchanged vertical ray+/-0.3m and height error<.12m; additionally five .9-normal exposed crazy_paving floor samples at16.64575m. Other targets retain .9-normal roof selection. Original-source probes temporarily enable original COL components and restore their exact prior states without saving; adjacent-ground baseline is measured after restoration. Repeat with reload and saved scene SHA256 comparison.",
            backup=Path.GetFullPath(Output+"/structure-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")) };
        var errors=new List<string>();
        try
        {
            Directory.CreateDirectory(report.backup);
            foreach(var t in Targets)File.Copy(SectorFolder+t.sector+".unity",Path.Combine(report.backup,t.sector+".unity"),false);
            Physics.queriesHitBackfaces=false;
            var main=EditorSceneManager.OpenScene(GameUpdateSetup.GameScene,OpenSceneMode.Single);
            var stream=main.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var entries=stream.sectors.Where(s=>s!=null).ToArray();
            var firstPass=new List<Item>();report.firstPass=Array.Empty<Item>();
            foreach(var target in Targets)
            {
                firstPass.Add(ApplyTarget(target,entries));report.firstPass=firstPass.ToArray();
                report.disabledOriginalColliders=firstPass.Sum(i=>i.disabledOriginalColliders);
                report.addedVisualColliders=firstPass.Sum(i=>i.addedVisualColliders);
                // Preserve a completed model's native before/after evidence if
                // a later target throws before the whole first pass completes.
                File.WriteAllText(Output+"/structure-collision-repair.json",JsonUtility.ToJson(report,true));
            }
            var hashes=Targets.ToDictionary(t=>t.sector,t=>HashFile(SectorFolder+t.sector+".unity"));
            report.repeatPass=Targets.Select(t=>ApplyTarget(t,entries)).ToArray();
            report.changedFilesOnRepeat=Targets.Where(t=>hashes[t.sector]!=HashFile(SectorFolder+t.sector+".unity")).Select(t=>t.sector).ToArray();
            report.idempotent=report.changedFilesOnRepeat.Length==0 && report.repeatPass.All(i=>i.addedVisualColliders==0 && i.disabledOriginalColliders==0 && i.passed);
            report.disabledOriginalColliders=report.firstPass.Sum(i=>i.disabledOriginalColliders);
            report.addedVisualColliders=report.firstPass.Sum(i=>i.addedVisualColliders);
            if(!report.idempotent)errors.Add("Repeat changed collider counts, active old COL, or saved sector bytes.");
            if(!report.firstPass.All(i=>i.passed))errors.Add("Roof/ground/visual invariants failed.");
        }
        catch(Exception ex){errors.Add(ex.ToString());Debug.LogException(ex);}
        finally
        {
            Physics.queriesHitBackfaces=oldBackfaces;
            try { if(setup.Length>0 && setup.All(s=>!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup); else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); }
            catch(Exception ex){errors.Add("Restore: "+ex.Message);}
            report.errors=errors.ToArray();report.passed=errors.Count==0 && report.idempotent;
            File.WriteAllText(Output+"/structure-collision-repair.json",JsonUtility.ToJson(report,true));
        }
        if(!report.passed)throw new Exception("LOS_SANTOS_PATROL_STRUCTURE_REPAIR_FAILED "+string.Join(" | ",errors));
        Debug.Log("LOS_SANTOS_PATROL_STRUCTURE_REPAIR_OK models="+Targets.Length+" disabled="+report.disabledOriginalColliders+" added="+report.addedVisualColliders+" idempotent=true");
    }

    static Item ApplyTarget(Target target,GtaSaSectorStreaming.Sector[] entries)
    {
        string path=SectorFolder+target.sector+".unity";
        var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        var matches=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
            .Where(t=>string.Equals(t.name,target.model+" #"+target.id,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)throw new Exception("Reviewed structure changed: "+target.model+" count="+matches.Length);
        var placement=matches[0];
        if(Vector3.Distance(placement.position,target.position)>.02f)throw new Exception("Reviewed pose changed: "+target.model+" "+placement.position);
        var filters=VisibleFilters(placement).ToArray();
        var item=new Item { model=target.model,sector=target.sector,objectName=placement.name,position=placement.position,
            visualBeforeSha256=VisualSignature(placement,filters) };
        var renderers=filters.Select(f=>f.GetComponent<MeshRenderer>()).ToArray();
        if(renderers.Length==0)throw new Exception("Visible structure absent: "+target.model);
        item.visualBounds=renderers[0].bounds;
        foreach(var renderer in renderers.Skip(1))item.visualBounds.Encapsulate(renderer.bounds);
        ReviewMaterials(target,item);
        foreach(var entry in entries)
            if(entry.scene!=target.sector && Intersects(entry.bounds,item.visualBounds,6))
                EditorSceneManager.OpenScene(SectorFolder+entry.scene+".unity",OpenSceneMode.Additive);
        Physics.SyncTransforms();
        var surfaces=Surfaces(filters);
        var samples=RoofSamples(surfaces,target);
        var floorSamples=target.floorMax>target.floorMin?ExposedSamples(surfaces,target.model+" exposed floor",target.floorMin,target.floorMax,.9f,false):Array.Empty<Vector3>();
        var original=placement.GetComponentsInChildren<Collider>(true).Where(c=>IsOriginal(c.transform,placement)).ToArray();
        item.originalColliders=original.Length;
        item.original=original.Select(c=>new OriginalCollider { hierarchy=Hierarchy(c.transform,placement),type=c.GetType().Name,enabledBefore=c.enabled,boundsBefore=c.bounds }).ToArray();
        var probes=samples.Select(p=>new Probe { point=p,expectedY=p.y,minimumNormalY=target.minimumProbeNormal }).ToArray();
        var floorProbes=floorSamples.Select(p=>new Probe { point=p,expectedY=p.y,minimumNormalY=.9f }).ToArray();
        var allProbes=probes.Concat(floorProbes).ToArray();
        MeasureOriginal(allProbes,original);
        item.adjacentGround=GroundBefore(placement,item.visualBounds);
        item.oldVisualColliders=filters.Sum(f=>f.GetComponents<MeshCollider>().Length);
        foreach(var collider in original){if(collider.enabled)item.disabledOriginalColliders++;collider.enabled=false;}
        item.visualColliders=AddVisualCollision(placement.gameObject);
        item.addedVisualColliders=item.visualColliders-item.oldVisualColliders;
        Physics.SyncTransforms();
        var active=filters.SelectMany(f=>f.GetComponents<MeshCollider>()).Cast<Collider>().ToArray();
        foreach(var probe in allProbes)
        {
            MeasureOwn(probe,active,false);
            probe.passed=probe.afterHit && probe.afterNearestGap<.12f;
        }
        GroundAfter(placement,item.adjacentGround);
        for(int i=0;i<original.Length;i++)item.original[i].enabledAfter=original[i].enabled;
        item.roof=probes;
        item.exposedFloor=floorProbes;
        item.visualAfterSha256=VisualSignature(placement,filters);
        item.passed=probes.Length==5 && allProbes.All(p=>p.passed) && item.adjacentGround.All(g=>g.passed) &&
            original.All(c=>!c.enabled) && filters.All(f=>f.GetComponents<MeshCollider>().Length==1 && f.GetComponent<MeshCollider>().sharedMesh==f.sharedMesh) &&
            item.visualBeforeSha256==item.visualAfterSha256;
        if(!item.passed)
        {
            File.WriteAllText(Output+"/structure-failed-"+target.model.ToLowerInvariant()+".json",JsonUtility.ToJson(item,true));
            throw new Exception("Structure invariant failed: "+target.model+" roof="+probes.Count(p=>p.passed)+"/"+probes.Length+" ground="+item.adjacentGround.Count(g=>g.passed)+"/"+item.adjacentGround.Length+" visual="+(item.visualBeforeSha256==item.visualAfterSha256));
        }
        if(item.disabledOriginalColliders>0 || item.addedVisualColliders>0)
        { EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed: "+path); }
        return item;
    }

    static IEnumerable<MeshFilter> VisibleFilters(Transform root) => root.GetComponentsInChildren<MeshFilter>(true)
        .Where(f=>f.sharedMesh && f.gameObject.activeInHierarchy && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled && !IsOriginal(f.transform,root));
    static bool IsOriginal(Transform node,Transform root)
    { for(var at=node;at && at!=root;at=at.parent)if(at.name.StartsWith("Colisão original GTA SA",StringComparison.Ordinal))return true;return false; }
    static List<Surface> Surfaces(MeshFilter[] filters)
    {
        var result=new List<Surface>();
        foreach(var filter in filters)
        {
            var mats=filter.GetComponent<MeshRenderer>().sharedMaterials;
            using(var data=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using(var vertices=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                for(int slot=0;slot<data[0].subMeshCount;slot++)
                {
                    var sub=data[0].GetSubMesh(slot);if(sub.topology!=MeshTopology.Triangles)continue;
                    using(var indices=new NativeArray<int>(sub.indexCount,Allocator.Temp))
                    {
                        data[0].GetIndices(indices,slot);
                        for(int i=0;i+2<indices.Length;i+=3)
                        {
                            Vector3 a=filter.transform.TransformPoint(vertices[indices[i]]),b=filter.transform.TransformPoint(vertices[indices[i+1]]),c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                            Vector3 cross=Vector3.Cross(b-a,c-a);if(cross.sqrMagnitude<1e-8f || cross.normalized.y<.01f)continue;
                            var m=slot<mats.Length?mats[slot]:null;
                            result.Add(new Surface {a=a,b=b,c=c,normal=cross.normalized,area=cross.magnitude*.5f,material=m?m.name+"/"+(m.mainTexture?m.mainTexture.name:""):""});
                        }
                    }
                }
            }
        }
        return result;
    }
    static Vector3[] RoofSamples(List<Surface> surfaces,Target target)
        => ExposedSamples(surfaces,target.model,target.roofMin,target.roofMax,target.minimumProbeNormal,target.preferHighestFirst);
    static Vector3[] ExposedSamples(List<Surface> surfaces,string label,float minimumHeight,float maximumHeight,float minimumNormal,bool preferHighestFirst)
    {
        var candidates=new List<Vector3>();
        foreach(var s in surfaces.Where(s=>s.normal.y>=minimumNormal && s.area>=3))
        {
            Vector3 center=(s.a+s.b+s.c)/3;
            foreach(var p in new[]{center,Vector3.Lerp(s.a,center,.35f),Vector3.Lerp(s.b,center,.35f),Vector3.Lerp(s.c,center,.35f)})
            {
                if(p.y<minimumHeight || p.y>maximumHeight)continue;
                float top=float.NegativeInfinity;
                foreach(var other in surfaces)if(TriangleHeight(other,p.x,p.z,out float y))top=Mathf.Max(top,y);
                if(Mathf.Abs(top-p.y)<.08f)candidates.Add(p);
            }
        }
        if(candidates.Count<5)throw new Exception("Five exposed source probes not found: "+label+" count="+candidates.Count);
        var selected=new List<Vector3>();
        var middle=candidates.Aggregate(Vector3.zero,(a,p)=>a+p)/candidates.Count;
        while(selected.Count<5)
        {
            var options=candidates.Where(p=>!selected.Any(q=>Vector3.Distance(p,q)<1)).ToArray();
            if(options.Length==0)throw new Exception("Source probes not spatially distinct: "+label);
            var best=options.OrderByDescending(p=>selected.Count==0?(preferHighestFirst?p.y:-(p-middle).sqrMagnitude):selected.Min(q=>Vector3.ProjectOnPlane(p-q,Vector3.up).sqrMagnitude)).First();
            selected.Add(best);
        }
        return selected.ToArray();
    }
    static bool TriangleHeight(Surface s,float x,float z,out float y)
    {
        double denominator=(s.b.z-s.c.z)*(double)(s.a.x-s.c.x)+(s.c.x-s.b.x)*(double)(s.a.z-s.c.z);
        if(Math.Abs(denominator)<1e-10){y=0;return false;}
        double u=((s.b.z-s.c.z)*(double)(x-s.c.x)+(s.c.x-s.b.x)*(double)(z-s.c.z))/denominator;
        double v=((s.c.z-s.a.z)*(double)(x-s.c.x)+(s.a.x-s.c.x)*(double)(z-s.c.z))/denominator;
        y=(float)(u*s.a.y+v*s.b.y+(1-u-v)*s.c.y);return u>=-1e-6 && v>=-1e-6 && 1-u-v>=-1e-6;
    }
    static void MeasureOwn(Probe probe,Collider[] colliders,bool before)
    {
        float top=float.NegativeInfinity,gap=1000;string name="none";bool hitAny=false;
        var highRay=new Ray(probe.point+Vector3.up*100,Vector3.down);
        var shortRay=new Ray(probe.point+Vector3.up*.3f,Vector3.down);
        foreach(var collider in colliders)
        {
            if(!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)continue;
            if(collider.Raycast(highRay,out var high,200))top=Mathf.Max(top,high.point.y);
            if(collider.Raycast(shortRay,out var near,.6f) && near.normal.y>=probe.minimumNormalY-.0005f && Mathf.Abs(near.point.y-probe.point.y)<gap)
            { gap=Mathf.Abs(near.point.y-probe.point.y);name=collider.name;hitAny=true; }
        }
        if(before){probe.beforeHit=hitAny;probe.beforeTopY=float.IsNegativeInfinity(top)?-10000:top;probe.beforeNearestGap=gap;probe.beforeCollider=name;}
        else{probe.afterHit=hitAny;probe.afterTopY=float.IsNegativeInfinity(top)?-10000:top;probe.afterNearestGap=gap;probe.afterCollider=name;}
    }
    static void MeasureOriginal(Probe[] probes,Collider[] original)
    {
        var states=original.Select(c=>c.enabled).ToArray();
        try
        {
            foreach(var collider in original)collider.enabled=true;
            Physics.SyncTransforms();
            foreach(var probe in probes)MeasureOwn(probe,original,true);
        }
        finally
        {
            for(int i=0;i<original.Length;i++)original[i].enabled=states[i];
            Physics.SyncTransforms();
        }
    }
    static GroundProbe[] GroundBefore(Transform placement,Bounds bounds)
    {
        var points=new[]{new Vector3(bounds.min.x-1,bounds.max.y+30,bounds.center.z),new Vector3(bounds.max.x+1,bounds.max.y+30,bounds.center.z),
            new Vector3(bounds.center.x,bounds.max.y+30,bounds.min.z-1),new Vector3(bounds.center.x,bounds.max.y+30,bounds.max.z+1)};
        return points.Select(p=>{
            var other=OtherFloor(p,placement);var world=WorldTop(p);
            return new GroundProbe {location=p,externalFloorPresent=other.HasValue,beforeOtherY=other.HasValue?other.Value.point.y:-10000,
                beforeOtherCollider=other.HasValue?other.Value.collider.name:"none",beforeWorldTopY=world.HasValue?world.Value.point.y:-10000};
        }).ToArray();
    }
    static void GroundAfter(Transform placement,GroundProbe[] probes)
    {
        foreach(var p in probes)
        {
            var other=OtherFloor(p.location,placement);var world=WorldTop(p.location);
            p.afterOtherY=other.HasValue?other.Value.point.y:-10000;p.afterOtherCollider=other.HasValue?other.Value.collider.name:"none";
            p.afterWorldTopY=world.HasValue?world.Value.point.y:-10000;
            p.passed=other.HasValue==p.externalFloorPresent && (!other.HasValue || Mathf.Abs(p.afterOtherY-p.beforeOtherY)<.002f);
        }
    }
    static RaycastHit? OtherFloor(Vector3 origin,Transform exclude)
    { var hits=Physics.RaycastAll(origin,Vector3.down,300,1,QueryTriggerInteraction.Ignore).Where(h=>!h.collider.transform.IsChildOf(exclude) && h.normal.y>=.72f).OrderByDescending(h=>h.point.y).ToArray();return hits.Length>0?(RaycastHit?)hits[0]:null; }
    static RaycastHit? WorldTop(Vector3 origin)
    { var hits=Physics.RaycastAll(origin,Vector3.down,300,1,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y).ToArray();return hits.Length>0?(RaycastHit?)hits[0]:null; }
    static void ReviewMaterials(Target target,Item item)
    {
        string path=SourceModels+target.model.ToLowerInvariant()+".materials.json";
        if(!File.Exists(path))throw new FileNotFoundException("Reviewed material sidecar absent",path);
        var data=JsonUtility.FromJson<Sidecar>(File.ReadAllText(path));
        var slots=data.materials??Array.Empty<MaterialSlot>();item.reviewedMaterialSlots=slots.Length;
        var foliage=new Regex(@"leaf|leaves|palm|bush|tree|veg_",RegexOptions.IgnoreCase);
        foreach(var slot in slots)
        {
            if((slot.textures??Array.Empty<string>()).Any(t=>foliage.IsMatch(t)))throw new Exception("Unreviewed foliage slot in solid structure: "+target.model+"/"+slot.material);
            if(slot.color!=null && slot.color.Length>=4 && slot.color[3]<.95f)item.sourceAlphaSlots++;
            if((slot.textures??Array.Empty<string>()).Any(t=>t.IndexOf("fence",StringComparison.OrdinalIgnoreCase)>=0 || t.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0))item.fenceOrGlassSlots++;
        }
    }
    static bool Intersects(Bounds a,Bounds b,float margin)=>a.min.x<=b.max.x+margin && a.max.x>=b.min.x-margin && a.min.z<=b.max.z+margin && a.max.z>=b.min.z-margin;
    static string Hierarchy(Transform node,Transform root)
    {var parts=new List<string>();for(var p=node;p && p!=root;p=p.parent)parts.Add(p.name+"["+p.GetSiblingIndex()+"]");parts.Reverse();return string.Join("/",parts);}
    static string VisualSignature(Transform root,MeshFilter[] filters)
    {
        var text=new StringBuilder();
        foreach(var transform in root.GetComponentsInChildren<Transform>(true))
        {text.Append(Hierarchy(transform,root)).Append('|').Append(transform.localPosition.ToString("R")).Append('|').Append(transform.localRotation.ToString("R")).Append('|').Append(transform.localScale.ToString("R")).Append('|').Append(transform.gameObject.activeSelf).AppendLine();}
        foreach(var filter in filters)
        {
            var renderer=filter.GetComponent<MeshRenderer>();
            text.Append(Hierarchy(filter.transform,root)).Append('|').Append(AssetDatabase.GetAssetPath(filter.sharedMesh)).Append('|').Append(filter.sharedMesh.name).Append('|').Append(filter.sharedMesh.vertexCount).Append('|').Append(renderer.enabled);
            foreach(var material in renderer.sharedMaterials)text.Append('|').Append(material?AssetDatabase.GetAssetPath(material):"null");text.AppendLine();
        }
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
    }
    static string HashFile(string path)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
}
