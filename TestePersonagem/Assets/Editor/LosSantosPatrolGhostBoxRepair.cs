using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One reviewed obsolete COL pillar. Its lower edge overlaps an existing curb,
// whose own original COL is retained. The 10m pillar has no corresponding body.
public static class LosSantosPatrolGhostBoxRepair
{
    const string Sector = "GTASASector_p9_m8";
    const string ScenePath = "Assets/Scenes/GTASanAndreasModFullSectors/" + Sector + ".unity";
    const string Output = "../tools/los-santos-patrol";
    public const string TargetModel = "RiverBridge1_LAe";
    public const int SourceBoxIndex = 8;
    static readonly Vector3 Position = new Vector3(2413.7734375f, 12.8359375f, 1851.09375f);
    static readonly Vector3 Center = new Vector3(9.4698905945f, 3.9161703587f, 4.6354336739f);
    static readonly Vector3 Size = new Vector3(.39990234375f, 10.1928286552f, 1.000244140625f);
    static readonly Vector3 PlayerStart = new Vector3(2422.74853515625f, 12.5718765259f, 1856f);

    [Serializable] sealed class FloorSample
    {
        public Vector3 point;
        public bool found;
        public float height;
        public string collider;
    }
    [Serializable] sealed class CapsuleProbe
    {
        public Vector3 start, finalPosition;
        public float requestedX, advancedX;
        public int belowFlags;
        public bool supportedAtEnd;
        public string floorCollider;
    }
    [Serializable] sealed class Pass
    {
        public string objectName, otherCollisionBeforeSha256, otherCollisionAfterSha256, visualBeforeSha256, visualAfterSha256;
        public int sourceBoxes, otherColliders, changed;
        public bool enabledBefore, enabledAfter, otherCollisionUnchanged, visualUnchanged, adjacentFloorUnchanged, passed;
        public Vector3 position, localCenter, localSize;
        public Bounds targetBounds;
        public FloorSample[] floorBefore, floorAfter;
        public CapsuleProbe capsuleBefore, capsuleAfter;
    }
    [Serializable] sealed class Report
    {
        public string generatedUtc, method, backup, scene, sceneSha256First, sceneSha256Repeat;
        public bool passed, idempotent;
        public Pass firstPass, repeatPass;
        public string[] errors;
    }

    // Root calls this in the source-box creation loop. The reviewed model has
    // exactly one exterior IPL placement globally; pose also guards that fact.
    public static bool SkipSourceBox(GameObject instance, string modelName, int sourceBoxIndex)
    {
        return instance && sourceBoxIndex == SourceBoxIndex &&
            string.Equals(modelName, TargetModel, StringComparison.OrdinalIgnoreCase) &&
            Vector3.Distance(instance.transform.position, Position) < .02f;
    }

    [MenuItem("Horizonte/Los Santos/Patrulha - remover uma caixa fantasma verificada")]
    public static void ApplyAndVerify()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Pare o Play Mode antes do reparo da caixa.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool backfaces = Physics.queriesHitBackfaces;
        Directory.CreateDirectory(Output);
        var report = new Report {
            generatedUtc = DateTime.UtcNow.ToString("o"), scene = ScenePath,
            method = "Only RiverBridge1_LAe #17501, original IPL lae2_stream2 index28, zero-based source box8. Retain this editable collider disabled; preserve other17 boxes plus original mesh COL and all visual geometry. Native CharacterController before/after moves .42m at P02795; original floor probes, collision/visual hashes and saved scene repeat verify scope.",
            backup = Path.GetFullPath(Output + "/ghostbox-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"))
        };
        var errors = new List<string>();
        try
        {
            Physics.queriesHitBackfaces = false;
            Directory.CreateDirectory(report.backup);
            File.Copy(ScenePath, Path.Combine(report.backup, Sector + ".unity"), false);
            report.firstPass = ApplyOne();
            report.sceneSha256First = HashFile(ScenePath);
            report.repeatPass = ApplyOne();
            report.sceneSha256Repeat = HashFile(ScenePath);
            report.idempotent = report.sceneSha256First == report.sceneSha256Repeat && report.repeatPass.changed == 0;
            report.passed = report.firstPass.passed && report.repeatPass.passed && report.idempotent;
            if (!report.passed) throw new InvalidOperationException("Ghost box scope/native/repeat verification failed.");
        }
        catch (Exception e) { errors.Add(e.ToString()); Debug.LogException(e); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try { if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup); }
            catch (Exception e) { errors.Add("Scene restore: " + e); }
            report.errors = errors.ToArray();
            report.passed &= errors.Count == 0;
            File.WriteAllText(Output + "/ghostbox-repair-audit.json", JsonUtility.ToJson(report, true));
        }
        Debug.Log("LOS_SANTOS_GHOST_BOX_REPAIR passed=" + report.passed + " idempotent=" + report.idempotent);
        if (!report.passed) throw new InvalidOperationException("See tools/los-santos-patrol/ghostbox-repair-audit.json");
    }

    static Pass ApplyOne()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == TargetModel + " #17501" && Vector3.Distance(t.position, Position) < .02f).ToArray();
        if (roots.Length != 1) throw new InvalidOperationException("Expected one exact reviewed RiverBridge placement.");
        var root = roots[0];
        if (Vector3.Distance(root.lossyScale, Vector3.one) > .0001f || Quaternion.Angle(root.rotation, Quaternion.identity) > .001f)
            throw new InvalidOperationException("Reviewed RiverBridge pose has an unexpected scale/rotation.");
        var boxes = root.GetComponentsInChildren<BoxCollider>(true);
        if (boxes.Length != 18) throw new InvalidOperationException("Expected all18 editable source boxes, found " + boxes.Length);
        var matches = boxes.Where(b => Vector3.Distance(b.center, Center) < .0001f && Vector3.Distance(b.size, Size) < .0001f &&
            b.transform.parent == root && b.transform.localPosition.sqrMagnitude < .0000001f &&
            Vector3.Distance(b.transform.localScale, Vector3.one) < .0001f &&
            Quaternion.Angle(b.transform.localRotation, Quaternion.identity) < .001f).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one exact source box8 by pose/center/size.");
        var target = matches[0];
        var result = new Pass { objectName = root.name, position = root.position, sourceBoxes = boxes.Length,
            otherColliders = root.GetComponentsInChildren<Collider>(true).Length - 1,
            enabledBefore = target.enabled, localCenter = target.center, localSize = target.size, targetBounds = target.bounds,
            otherCollisionBeforeSha256 = CollisionHash(root, target), visualBeforeSha256 = VisualHash(root) };
        Physics.SyncTransforms();
        result.floorBefore = Floors();
        result.capsuleBefore = NativeProbe();
        bool changed = target.enabled;
        target.enabled = false;
        Physics.SyncTransforms();
        result.floorAfter = Floors();
        result.capsuleAfter = NativeProbe();
        result.enabledAfter = target.enabled;
        result.otherCollisionAfterSha256 = CollisionHash(root, target);
        result.visualAfterSha256 = VisualHash(root);
        result.otherCollisionUnchanged = result.otherCollisionBeforeSha256 == result.otherCollisionAfterSha256;
        result.visualUnchanged = result.visualBeforeSha256 == result.visualAfterSha256;
        result.adjacentFloorUnchanged = result.floorBefore.Zip(result.floorAfter, (a, b) => a.found && b.found &&
            Mathf.Abs(a.height - b.height) < .0001f && a.collider == b.collider).All(v => v);
        bool beforeExpected = !result.enabledBefore || result.capsuleBefore.advancedX < .12f;
        result.passed = result.otherCollisionUnchanged && result.visualUnchanged && result.adjacentFloorUnchanged &&
            !result.enabledAfter && beforeExpected && result.capsuleAfter.advancedX > .30f && result.capsuleAfter.supportedAtEnd;
        if (!result.passed)
        {
            target.enabled = result.enabledBefore;
            Physics.SyncTransforms();
            throw new InvalidOperationException("Ghost box native/scope check failed before saving: " + JsonUtility.ToJson(result));
        }
        if (changed)
        {
            EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save reviewed ghost box scene.");
            result.changed = 1;
        }
        return result;
    }

    static CapsuleProbe NativeProbe()
    {
        var go = new GameObject("Patrol ghost box temporary native capsule");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.position = PlayerStart + Vector3.up * .03f;
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = .26f; cc.center = new Vector3(0, .91f, 0);
        cc.stepOffset = .25f; cc.slopeLimit = 45; cc.skinWidth = .035f; cc.minMoveDistance = .001f;
        var result = new CapsuleProbe { start = go.transform.position, requestedX = .42f };
        try
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 14; i++)
            {
                var flags = cc.Move(new Vector3(.03f, -.02f, 0));
                if ((flags & CollisionFlags.Below) != 0) result.belowFlags++;
            }
            result.finalPosition = go.transform.position;
            result.advancedX = result.finalPosition.x - result.start.x;
            var floor = FloorAt(result.finalPosition, cc);
            result.supportedAtEnd = floor.found && Mathf.Abs(result.finalPosition.y - floor.height) < .20f;
            result.floorCollider = floor.collider;
        }
        finally { UnityEngine.Object.DestroyImmediate(go); Physics.SyncTransforms(); }
        return result;
    }

    static FloorSample[] Floors() => new[] { 0f, .18f, .36f, .54f }
        .Select(x => FloorAt(PlayerStart + Vector3.right * x, null)).ToArray();
    static FloorSample FloorAt(Vector3 point, Collider excluded)
    {
        var hit = Physics.RaycastAll(point + Vector3.up * 2, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider != excluded && h.normal.y > .7f && h.point.y < PlayerStart.y + .35f && h.point.y > PlayerStart.y - 1)
            .OrderByDescending(h => h.point.y).FirstOrDefault();
        return new FloorSample { point = point, found = hit.collider, height = hit.collider ? hit.point.y : -10000,
            collider = hit.collider ? Hierarchy(hit.collider.transform) + "/" + hit.collider.GetType().Name : "" };
    }

    static string CollisionHash(Transform root, Collider excluded)
    {
        var lines = new List<string>();
        foreach (var c in root.GetComponentsInChildren<Collider>(true).Where(c => c != excluded))
        {
            string line = Hierarchy(c.transform) + "|" + c.GetType().Name + "|" + c.enabled + "|" + c.isTrigger + "|" + c.gameObject.activeSelf + "|" + c.gameObject.layer +
                "|" + F(c.contactOffset) + "|" + c.includeLayers.value + "|" + c.excludeLayers.value + "|" + c.layerOverridePriority + "|" + c.providesContacts +
                "|" + AssetDatabase.GetAssetPath(c.sharedMaterial) + "|" + Matrix(c.transform.localToWorldMatrix);
            if (c is BoxCollider b) line += "|" + Vec(b.center) + "|" + Vec(b.size);
            else if (c is SphereCollider s) line += "|" + Vec(s.center) + "|" + F(s.radius);
            else if (c is MeshCollider m) line += "|" + AssetDatabase.GetAssetPath(m.sharedMesh) + "|" + m.convex + "|" + m.cookingOptions;
            lines.Add(line);
        }
        return Hash(string.Join("\n", lines.OrderBy(x => x, StringComparer.Ordinal)));
    }
    static string VisualHash(Transform root)
    {
        var lines = new List<string>();
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = f.GetComponent<Renderer>();
            if (!r) continue;
            string path = AssetDatabase.GetAssetPath(f.sharedMesh);
            lines.Add(Hierarchy(f.transform) + "|" + path + "|" + (File.Exists(path) ? HashFile(path) : "") + "|" + Matrix(f.transform.localToWorldMatrix) + "|" + r.enabled + "|" + string.Join(",", r.sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m))));
        }
        return Hash(string.Join("\n", lines.OrderBy(x => x, StringComparer.Ordinal)));
    }
    static string Hierarchy(Transform t)
    {
        var parts = new List<string>();
        while (t) { parts.Add(t.name + "[" + t.GetSiblingIndex() + "]"); t = t.parent; }
        parts.Reverse(); return string.Join("/", parts);
    }
    static string Matrix(Matrix4x4 m) => string.Join(",", Enumerable.Range(0, 16).Select(i => F(m[i])));
    static string Vec(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string HashFile(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
}
