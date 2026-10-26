using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Renders reference views of the recovered city without changing game scenes.
public static class MapVisualAudit
{
    public static void Capture()
    {
        const string official = "Assets/Scenes/TestePersonagem.unity";
        var scene = EditorSceneManager.OpenScene(official, OpenSceneMode.Single);
        string[] paths = Directory.GetFiles("Assets/Scenes/CitySectors", "CitySector_*.unity");
        Array.Sort(paths, StringComparer.Ordinal);
        foreach (string path in paths)
            EditorSceneManager.OpenScene(path.Replace('\\', '/'), OpenSceneMode.Additive);

        var output = Path.GetFullPath("../tools/map-visual-audit");
        Directory.CreateDirectory(output);
        var cameraObject = new GameObject("Map audit camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 66f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 900f;
        camera.allowHDR = false;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 2.2f, 0f), Quaternion.Euler(0f, yaw, 0f));
            Render(camera, target, Path.Combine(output, "spawn-yaw-" + yaw + ".png"));
        }

        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            cameraObject.transform.SetPositionAndRotation(new Vector3(-390f, 2.2f, 0f), Quaternion.Euler(0f, yaw, 0f));
            Render(camera, target, Path.Combine(output, "map-marker-yaw-" + yaw + ".png"));
        }
        cameraObject.transform.SetPositionAndRotation(new Vector3(-396f, 2.2f, 10f), Quaternion.Euler(0f, 180f, 0f));
        Render(camera, target, Path.Combine(output, "map-marker-from-north.png"));
        cameraObject.transform.SetPositionAndRotation(new Vector3(-396f, 2.2f, -45f), Quaternion.identity);
        Render(camera, target, Path.Combine(output, "map-marker-from-south.png"));

        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        Debug.Log("MAP_VISUAL_AUDIT_READY " + output);
    }

    static void Render(Camera camera, RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = previous;
    }
}
