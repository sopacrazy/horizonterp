using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LosSantosStreamingVerification
{
    [MenuItem("Horizonte/Los Santos/Verificar protecao de streaming")]
    public static void Verify()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.name))
            throw new InvalidOperationException("Abra uma cena salva antes da verificacao.");
        var probe = new GameObject("Temporary streaming verification");
        probe.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var stream = probe.AddComponent<GtaSaSectorStreaming>();
            var loaded = new GtaSaSectorStreaming.Sector {
                scene = scene.name, bounds = new Bounds(Vector3.zero, new Vector3(20, 2, 20))
            };
            var missing = new GtaSaSectorStreaming.Sector {
                scene = "__missing_ground_sector_53fd__", bounds = loaded.bounds
            };
            stream.sectors = new[] { loaded };
            Require(stream.CollisionAreaLoadedAt(new Vector3(0, 1000, 0), 0), "loaded area ignores height");
            stream.sectors = new[] { loaded, missing };
            Require(!stream.CollisionAreaLoadedAt(Vector3.zero, 0), "overlapping missing sector blocks area");
            Require(!stream.CollisionAreaLoadedAt(new Vector3(10.2f, 0, 0), .3f), "capsule margin crosses boundary");
            Require(stream.CollisionAreaLoadedAt(new Vector3(10.4f, 0, 0), .3f), "unrelated bounds do not block area");
            missing.bounds = new Bounds(new Vector3(100, 0, 0), new Vector3(20, 2, 20));
            Require(stream.CollisionAreaLoadedAt(Vector3.zero, .3f), "distant missing scenery does not block area");
            Debug.Log("LOS_SANTOS_STREAMING_VERIFICATION_OK assertions=5");
        }
        finally { Object.DestroyImmediate(probe); }
    }

    static void Require(bool condition, string invariant)
    {
        if (!condition) throw new InvalidOperationException("Streaming invariant failed: " + invariant);
    }
}
