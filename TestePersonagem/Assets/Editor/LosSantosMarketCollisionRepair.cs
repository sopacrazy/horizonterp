using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// market2_lae was replaced by a different shop layout in the supplied mod. Its
// old COL boxes still describe the removed shops, including an invisible box
// over the empty courtyard at (2437,1759). Preserve those components disabled.
public static class LosSantosMarketCollisionRepair
{
    const string Model = "market2_lae";
    const string ScenePath = "Assets/Scenes/GTASanAndreasModFullSectors/GTASASector_p9_m7.unity";
    const string Reports = "../tools/los-santos-repair";
    static readonly Vector3 GhostProbe = new Vector3(2437, 30, 1759);

    [Serializable] sealed class ColliderState
    {
        public string path, type;
        public bool enabledBefore, enabledAfter;
        public Bounds boundsBefore;
    }
    [Serializable] sealed class ProbeState
    {
        public bool hit;
        public float height;
        public string collider;
    }
    [Serializable] sealed class Report
    {
        public string scene, model, backup;
        public int originalColliders, disabledOriginalColliders, visualColliders, addedVisualColliders;
        public Vector3 ghostProbe;
        public ProbeState before, after;
        public ColliderState[] original;
    }

    public static bool UsesVisualCollision(string modelName) =>
        string.Equals(modelName, Model, StringComparison.OrdinalIgnoreCase);

    // The importer calls this before reading the mismatched original COL.
    // Consequently regeneration retains the actual visible walls and floors.
    public static int AddVisualCollision(GameObject instance)
    {
        int count = 0;
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            var collider = filter.GetComponents<MeshCollider>()
                .FirstOrDefault(item => item.sharedMesh == filter.sharedMesh);
            if (!collider) collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.enabled = true;
            count++;
        }
        return count;
    }

    [MenuItem("Horizonte/Los Santos/Corrigir colisao antiga do patio das lojas")]
    public static void ApplyAndVerify()
    {
        string backup = Path.GetFullPath(Reports + "/market-collision-backup-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)), false);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var matches = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(item => item.name.StartsWith(Model + " #", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Instancia market2_lae inesperada: " + matches.Length);
            var placement = matches[0];
            if (Vector3.Distance(placement.position, new Vector3(2450.875f, 16, 1757.3984375f)) > .02f)
                throw new InvalidOperationException("market2_lae fora da posicao auditada: " + placement.position);
            var original = placement.GetComponentsInChildren<Collider>(true)
                .Where(item => IsOriginalCollision(item.transform, placement)).ToArray();
            Physics.SyncTransforms();
            var states = original.Select(item => new ColliderState {
                path = NodePath(item.transform, placement), type = item.GetType().Name,
                boundsBefore = item.bounds, enabledBefore = item.enabled
            }).ToArray();
            var before = Probe();
            int disabled = 0;
            foreach (var collider in original)
            {
                if (collider.enabled) disabled++;
                collider.enabled = false;
            }
            int oldVisual = placement.GetComponentsInChildren<MeshCollider>(true)
                .Count(item => !IsOriginalCollision(item.transform, placement));
            int visual = AddVisualCollision(placement.gameObject);
            if (visual == 0) throw new InvalidOperationException("Malhas visiveis das lojas ausentes.");
            Physics.SyncTransforms();
            var after = Probe();
            // Every old box/mesh remains serialised and can be restored from
            // backup, but none may participate in the courtyard's collision.
            if (original.Any(item => item.enabled))
                throw new InvalidOperationException("Colisao antiga permaneceu ativa.");
            if (after.hit && after.height > 13.5f)
                throw new InvalidOperationException("A caixa fantasma ainda bloqueia o patio: " + after.collider + " y=" + after.height);
            for (int i = 0; i < states.Length; i++) states[i].enabledAfter = original[i].enabled;
            var report = new Report {
                scene = ScenePath, model = Model, backup = backup,
                originalColliders = original.Length, disabledOriginalColliders = disabled,
                visualColliders = visual, addedVisualColliders = visual - oldVisual,
                ghostProbe = GhostProbe, before = before, after = after, original = states
            };
            if (disabled > 0 || visual > oldVisual)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Falha ao salvar " + ScenePath);
            }
            File.WriteAllText(Path.GetFullPath(Reports + "/market-collision-repair.json"), JsonUtility.ToJson(report, true));
            Debug.Log("LOS_SANTOS_MARKET_COLLISION_REPAIR_OK disabled=" + disabled +
                " visual=" + visual + " beforeY=" + before.height + " afterY=" + after.height + " backup=" + backup);
        }
        finally
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    static bool IsOriginalCollision(Transform node, Transform placement)
    {
        for (var ancestor = node; ancestor && ancestor != placement; ancestor = ancestor.parent)
            if (ancestor.name.StartsWith("Colis\u00e3o original GTA SA", StringComparison.Ordinal)) return true;
        return false;
    }

    static string NodePath(Transform node, Transform root)
    {
        var parts = new List<string>();
        while (node && node != root) { parts.Add(node.name); node = node.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static ProbeState Probe()
    {
        var hits = Physics.RaycastAll(GhostProbe, Vector3.down, 40, 1, QueryTriggerInteraction.Ignore);
        var state = new ProbeState { hit = hits.Length > 0 };
        if (!state.hit) return state;
        var hit = hits.OrderByDescending(item => item.point.y).First();
        state.height = hit.point.y;
        state.collider = hit.collider.name;
        return state;
    }
}
