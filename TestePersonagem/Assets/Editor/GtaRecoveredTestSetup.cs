using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GtaRecoveredTestSetup
{
    const string ScenePath = "Assets/Scenes/GTAMapaTeste.unity";

    [MenuItem("Horizonte/Mapa GTA recuperado/Montar teste separado")]
    public static void Create()
    {
        Directory.CreateDirectory("../tools/gta-map-review");
        if (File.Exists(ScenePath)) File.Copy(ScenePath, "../tools/gta-map-review/scene-before-repair-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
        var scene = EditorSceneManager.OpenScene("Assets/GTARecoveredMap/GTARecoveredGeometry.unity");
        var root = scene.GetRootGameObjects()[0];
        Physics.SyncTransforms();
        Vector3 spawn = FindSpawn(root);
        root.transform.position -= spawn;
        GtaRecoveredCityRepair.Apply(root);
        Physics.SyncTransforms();
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"));
        player.name = "Jogador - Teste mapa GTA";
        var controller = player.GetComponent<CharacterController>();
        var movement = player.GetComponent<ThirdPersonPlayer>();
        controller.enabled = false;
        movement.spawnPosition = Vector3.up * .08f;
        movement.spawnYaw = 0;
        player.transform.SetPositionAndRotation(movement.spawnPosition, Quaternion.identity);
        controller.enabled = true;
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ThirdPersonCamera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.nearClipPlane = .15f; camera.farClipPlane = 1200; camera.fieldOfView = 58;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = new Color(.48f, .67f, .85f);
        var orbit = camera.GetComponent<ThirdPersonCamera>();
        orbit.target = player.transform; orbit.pitch = 16; orbit.distance = 4.2f;
        movement.cameraTransform = camera.transform;
        camera.transform.SetPositionAndRotation(new Vector3(0, 2.4f, -4.2f), Quaternion.Euler(16, 0, 0));
        var sun = new GameObject("Sol do teste", typeof(Light)).GetComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = .95f;
        sun.transform.rotation = Quaternion.Euler(42, -35, 0); sun.shadows = LightShadows.Soft;
        sun.shadowBias = .035f; sun.shadowNormalBias = .2f;
        RenderSettings.sun = sun; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.47f, .56f, .68f);
        RenderSettings.ambientEquatorColor = new Color(.32f, .35f, .39f);
        RenderSettings.ambientGroundColor = new Color(.20f, .20f, .21f);
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/GTARecoveredMap/Restored/CitySky.mat");
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, "Assets/GTARecoveredMap/Restored/CitySky.mat"); }
        sky.SetFloat("_SunSize", .025f); sky.SetFloat("_AtmosphereThickness", 1.1f); sky.SetFloat("_Exposure", 1.1f);
        sky.SetColor("_SkyTint", new Color(.51f, .56f, .65f)); RenderSettings.skybox = sky;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 350; RenderSettings.fogEndDistance = 1100;
        RenderSettings.fogColor = new Color(.68f, .75f, .81f);
        var preview = new GameObject("Teste local do mapa GTA").AddComponent<GtaRecoveredPreview>();
        preview.player = movement;
        preview.floorCheckpoints = FloorCheckpoints(root, preview.transform);
        ValidateScene(root);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../tools/gta-map-review");
        File.WriteAllText("../tools/gta-map-review/setup.txt", "Scene=" + ScenePath + "\nSource spawn=" + spawn + "\nRenderers=" + root.GetComponentsInChildren<MeshRenderer>().Length + "\nColliders=" + root.GetComponentsInChildren<MeshCollider>().Length + "\nMissing source materials reconstructed; overlapping road copies removed.\n");
        Debug.Log("GTA_MAP_SETUP_OK spawn=" + spawn);
    }

    static Vector3 FindSpawn(GameObject root)
    {
        // Prefer the intact shopping street, with enough space for a 1.8 m character.
        foreach (var position in new[] { new Vector3(60, 40, -45), new Vector3(80, 40, -30), new Vector3(70, 40, -42) })
            if (SafeFloor(position, out var preferred)) return preferred;
        var candidates = root.GetComponentsInChildren<MeshCollider>()
            .Where(c => c.name.ToLowerInvariant().Contains("ground") || c.name.StartsWith("sc1_rd"))
            .OrderBy(c => new Vector2(c.bounds.center.x, c.bounds.center.z).sqrMagnitude);
        foreach (var surface in candidates)
        {
            var b = surface.bounds;
            for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
            {
                Vector3 from = new Vector3(b.center.x + x * 2, b.max.y + 20, b.center.z + z * 2);
                if (!Physics.Raycast(from, Vector3.down, out var hit, b.size.y + 50, 1) || hit.normal.y < .95f) continue;
                var feet = hit.point + Vector3.up * .06f;
                if (Physics.CheckCapsule(feet + Vector3.up * .35f, feet + Vector3.up * 1.5f, .28f, 1)) continue;
                if (!Physics.Raycast(feet + new Vector3(0, 2, 3), Vector3.down, out var next, 4, 1) || Mathf.Abs(next.point.y - hit.point.y) > .2f) continue;
                if (Physics.CheckCapsule(next.point + Vector3.up * .4f, next.point + Vector3.up * 1.5f, .28f, 1)) continue;
                return hit.point;
            }
        }
        throw new Exception("Nenhum piso livre encontrado para o teste do mapa recuperado.");
    }

    static bool SafeFloor(Vector3 from, out Vector3 result)
    {
        result = default;
        if (!Physics.Raycast(from, Vector3.down, out var hit, 100, 1) || hit.normal.y < .95f) return false;
        if (Physics.CheckCapsule(hit.point + Vector3.up * .4f, hit.point + Vector3.up * 1.5f, .28f, 1)) return false;
        if (!Physics.Raycast(hit.point + new Vector3(0, 2, 3), Vector3.down, out var next, 4, 1) || Mathf.Abs(next.point.y - hit.point.y) > .25f) return false;
        if (Physics.CheckCapsule(next.point + Vector3.up * .4f, next.point + Vector3.up * 1.5f, .28f, 1)) return false;
        result = hit.point; return true;
    }

    static Transform[] FloorCheckpoints(GameObject root, Transform parent)
    {
        var points = new System.Collections.Generic.List<Transform>();
        foreach (var road in root.GetComponentsInChildren<MeshCollider>().Where(c => c.name.StartsWith("sc1_rd_r"))
            .OrderBy(c => c.bounds.center.sqrMagnitude))
        {
            var b = road.bounds;
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                var from = new Vector3(b.center.x + x * b.extents.x * .45f, b.max.y + 20, b.center.z + z * b.extents.z * .45f);
                if (!SafeFloor(from, out var floor) || floor.y < -5 || floor.y > 10 || new Vector2(floor.x, floor.z).magnitude > 180) continue;
                if (points.Any(p => Vector3.Distance(p.position, floor) < 12)) continue;
                var obj = new GameObject("Piso verificado " + points.Count);
                obj.transform.SetParent(parent); obj.transform.position = floor + Vector3.up * .06f;
                points.Add(obj.transform);
                if (points.Count >= 24) return points.ToArray();
            }
        }
        if (points.Count < 8) throw new Exception("Poucos pontos de piso encontrados para validar a cidade: " + points.Count);
        return points.ToArray();
    }

    static void ValidateScene(GameObject root)
    {
        int missing = 0, errors = 0, empty = 0;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            foreach (var mat in renderer.sharedMaterials)
            {
                if (!mat) { missing++; continue; }
                if (!mat.shader || mat.shader.name.Contains("InternalError") || ShaderUtil.ShaderHasError(mat.shader)) errors++;
                if (!mat.mainTexture) empty++;
            }
        File.WriteAllText("../tools/gta-map-review/scene-validation.txt", "Missing slots=" + missing + "\nShader errors=" + errors + "\nUntextured slots=" + empty);
        if (missing > 0 || errors > 0 || empty > 0) throw new Exception("Falha na verificacao de materiais do mapa: " + missing + "/" + errors + "/" + empty);
    }

    public static void CreateAndBuildWindows()
    {
        Create();
        const string output = "Builds/GTAMapaTeste/GTAMapaTeste.exe";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Falha na build de teste do mapa GTA.");
    }
}
