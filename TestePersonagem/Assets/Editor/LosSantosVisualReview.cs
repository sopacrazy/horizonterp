using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Focused photographs of measured gaps, including the original water geometry.
public static class LosSantosVisualReview
{
    [Serializable] sealed class GapFile { public Gap[] candidates; }
    [Serializable] sealed class Gap { public int id; public float[] unityBoundsXZ, representativeUnityXZ; }

    public static void Capture()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var stream = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var loaded = new Dictionary<string, Scene>();
            var gaps = JsonUtility.FromJson<GapFile>(File.ReadAllText("../tools/los-santos-repair/surface-gap-audit.json"));
            string tag = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--ls-audit-tag="))?.Split('=')[1] ?? "current";
            string folder = Path.GetFullPath("../tools/los-santos-repair/focused-views-" + tag);
            Directory.CreateDirectory(folder);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * .8f;
            var camera = new GameObject("Focused gap review camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, source);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.25f, .42f, .65f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 1200;
            foreach (Gap gap in gaps.candidates.Where(g => new[] {1,2,4,6,9,14,21,23,24,29}.Contains(g.id)))
            {
                float x = gap.representativeUnityXZ[0], z = gap.representativeUnityXZ[1];
                LoadAround(stream, loaded, x, z);
                Physics.SyncTransforms();
                float height = Physics.RaycastAll(new Vector3(x, 500, z), Vector3.down, 600)
                    .Where(h => !h.collider.isTrigger).Select(h => h.point.y).DefaultIfEmpty(20).Max();
                var focus = new Vector3(x, height, z);
                float width = Mathf.Max(gap.unityBoundsXZ[2] - gap.unityBoundsXZ[0], gap.unityBoundsXZ[3] - gap.unityBoundsXZ[1]);
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(50, width * .7f);
                camera.transform.SetPositionAndRotation(focus + Vector3.up * 400, Quaternion.Euler(90, 0, 0));
                Photograph(camera, Path.Combine(folder, "gap-" + gap.id + "-top.png"));
                camera.orthographic = false; camera.fieldOfView = 62;
                camera.transform.position = focus + new Vector3(-width * .65f - 30, width * .65f + 25, width * .65f + 30);
                camera.transform.LookAt(focus);
                Photograph(camera, Path.Combine(folder, "gap-" + gap.id + "-street.png"));
                Debug.Log("LOS_SANTOS_GAP_CAPTURE id=" + gap.id + " focus=" + focus);
            }
            // The observatory/favela overlap comes from the source map.
            LoadAround(stream, loaded, 1176, 2075);
            camera.orthographic = false;
            camera.transform.position = new Vector3(1155, 82, 2100);
            camera.transform.LookAt(new Vector3(1176, 70, 2075));
            Photograph(camera, Path.Combine(folder, "observatory-favela.png"));
            Object.DestroyImmediate(camera.gameObject);
        }
        finally
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    static void LoadAround(GtaSaSectorStreaming stream, Dictionary<string, Scene> loaded, float x, float z)
    {
        var wanted = new HashSet<string>(stream.sectors.Where(s => s != null &&
            s.bounds.min.x <= x + 320 && s.bounds.max.x >= x - 320 &&
            s.bounds.min.z <= z + 320 && s.bounds.max.z >= z - 320).Select(s => s.scene));
        foreach (string name in loaded.Keys.Where(n => !wanted.Contains(n)).ToArray())
        { EditorSceneManager.CloseScene(loaded[name], true); loaded.Remove(name); }
        foreach (string name in wanted.Where(n => !loaded.ContainsKey(n)))
            loaded.Add(name, EditorSceneManager.OpenScene("Assets/Scenes/GTASanAndreasModFullSectors/" + name + ".unity", OpenSceneMode.Additive));
    }

    static void Photograph(Camera camera, string file)
    {
        var texture = new RenderTexture(1280, 960, 24);
        var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); image.Apply();
            File.WriteAllBytes(file, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(image); Object.DestroyImmediate(texture);
        }
    }
}
