using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LosSantosPatrolBatch
{
    public static void ApplyRepairs()
    {
        var errors = new List<string>();
        Check("geometry", () => { LosSantosGeometryRepair.ApplyAndVerify(); LosSantosGeometryRepair.VerifyExisting(); }, errors);
        CheckPhysical(errors);
        Check("streaming", () => { EditorSceneManager.OpenScene(GameUpdateSetup.GameScene); LosSantosStreamingVerification.Verify(); }, errors);
        Check("walking", LosSantosWalkingVerification.Verify, errors);
        if (errors.Any()) throw new Exception("LOS_SANTOS_PATROL_REPAIRS_FAILED " + string.Join(" | ", errors));
        LosSantosRepairBatch.WriteSyncPaths();
        Debug.Log("LOS_SANTOS_PATROL_REPAIRS_VERIFIED");
    }

    public static void ApplyPhysicalRepairs()
    {
        var errors = new List<string>();
        CheckPhysical(errors);
        if (errors.Any()) throw new Exception("LOS_SANTOS_PATROL_PHYSICAL_REPAIRS_FAILED " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_PATROL_PHYSICAL_REPAIRS_VERIFIED");
    }

    static void CheckPhysical(List<string> errors)
    {
        Check("surface collision", () => {
            LosSantosSurfaceCollisionRepair.ApplyPatrolAndVerify();
            LosSantosCollisionPatchCatalog.CaptureFromReport("../tools/los-santos-patrol/surface-collision-repair.json");
        }, errors);
        Check("structure collision", LosSantosPatrolStructureRepair.ApplyAndVerify, errors);
        Check("ghost box", LosSantosPatrolGhostBoxRepair.ApplyAndVerify, errors);
    }

    static void Check(string name, Action action, List<string> errors)
    {
        try { action(); }
        catch (Exception exception) { errors.Add(name + ": " + exception.Message); Debug.LogException(exception); }
    }

    public static void Run()
    {
        CapturePatrol(false);
        LosSantosPatrolCapture.CaptureFindings();
    }

    public static void RunAfter()
    {
        CapturePatrol(true);
        LosSantosPatrolRecheck.Run();
        LosSantosPatrolRecheck.RunCurrentFindings();
        LosSantosRepairBatch.WriteSyncPaths();
    }

    public static void FinishAndPatrol()
    {
        ApplyRepairs();
        RunAfter();
        LosSantosPatrolCapture.CaptureRepairs();
        Debug.Log("LOS_SANTOS_FULL_PATROL_AND_REPAIRS_COMPLETE");
    }

    public static void VerifyPatrolSupplement()
    {
        LosSantosSurfaceCollisionRepair.ApplyPatrolSupplementAndVerify();
        LosSantosCollisionPatchCatalog.CaptureFromReport("../tools/los-santos-patrol/surface-collision-repair.json");
        EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);
        LosSantosStreamingVerification.Verify();
        LosSantosWalkingVerification.Verify();
        LosSantosPatrolRecheck.RunCurrentFindings();
        LosSantosRepairBatch.WriteSyncPaths();
        Debug.Log("LOS_SANTOS_PATROL_SUPPLEMENT_VERIFIED");
    }

    public static void VerifyVisualRepairs()
    {
        var errors=new List<string>();
        Check("geometry",()=>{LosSantosGeometryRepair.ApplyAndVerify();LosSantosGeometryRepair.VerifyExisting();},errors);
        Check("structure collision",LosSantosPatrolStructureRepair.ApplyAndVerify,errors);
        Check("foundation route",LosSantosPatrolRecheck.VerifyFoundationRoute,errors);
        Check("streaming",()=>{EditorSceneManager.OpenScene(GameUpdateSetup.GameScene);LosSantosStreamingVerification.Verify();},errors);
        Check("walking",LosSantosWalkingVerification.Verify,errors);
        Check("source views",LosSantosPatrolCapture.CaptureReviewPoses,errors);
        Check("repair views",LosSantosPatrolCapture.CaptureRepairs,errors);
        if(errors.Any())throw new Exception("LOS_SANTOS_VISUAL_REPAIR_CHECKS_FAILED "+string.Join(" | ",errors));
        LosSantosRepairBatch.WriteSyncPaths();
        Debug.Log("LOS_SANTOS_VISUAL_REPAIRS_VERIFIED");
    }

    public static void FinalizeCity()
    {
        VerifyVisualRepairs();
        RunAfter();
        Debug.Log("LOS_SANTOS_FINAL_CITY_VERIFIED");
    }

    static void CapturePatrol(bool continueAfterObstacles)
    {
        var counts = new Dictionary<string, int>();
        LosSantosPatrolCapture.BeginPatrolCapture();
        LosSantosPatrolVerification.OnPatrolSample = (position, direction, route) => {
            string cell = Regex.Match(route ?? "", @"GTASASector_[mp]\d+_[mp]\d+").Value;
            if (string.IsNullOrEmpty(cell)) cell = route ?? "route";
            counts.TryGetValue(cell, out int count);
            if (count >= 2) return;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * .8f;
            LosSantosPatrolCapture.CapturePatrolSample(position, direction, route);
            counts[cell] = count + 1;
        };
        LosSantosPatrolVerification.OnIncident = LosSantosPatrolCapture.CapturePatrolIncident;
        try
        {
            if (continueAfterObstacles) LosSantosPatrolVerification.RunWithContinuations();
            else LosSantosPatrolVerification.Run();
        }
        finally
        {
            LosSantosPatrolVerification.OnPatrolSample = null;
            LosSantosPatrolVerification.OnIncident = null;
            LosSantosPatrolCapture.EndPatrolCapture();
        }
        LosSantosPatrolCapture.CaptureCells();
        Debug.Log("LOS_SANTOS_PLAYER_PATROL_COMPLETE");
    }
}
