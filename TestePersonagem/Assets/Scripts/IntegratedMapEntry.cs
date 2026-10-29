using System;
using System.Collections;
using UnityEngine;

// Keep the capsule suspended until the destination sectors have collision geometry.
[DefaultExecutionOrder(-70)]
public sealed class IntegratedMapEntry : MonoBehaviour
{
    public GtaSaSectorStreaming streaming;
    public ThirdPersonPlayer player;
    public static bool Loading { get; private set; }
    IEnumerator Start()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-gta-sa-map") >= 0) yield break;
        Loading = true;
        player.gameObject.SetActive(false);
        player.transform.position = player.spawnPosition;
        streaming.target = player.transform;
        streaming.RefreshNow();
        float deadline = Time.realtimeSinceStartup + 120;
        while (!streaming.Ready && Time.realtimeSinceStartup < deadline)
        {
            if (!string.IsNullOrEmpty(streaming.LastError)) break;
            yield return null;
        }
        if (!streaming.Ready) { Debug.LogError("SA_ENTRY_FAILED " + streaming.LastError); yield break; }
        Physics.SyncTransforms();
        player.gameObject.SetActive(true);
        // Start/AttachPlayer may restore an old interior save. Reconcile only
        // after Start has run and collision sectors are available.
        yield return null;
        var map = NeighborhoodMap.Active;
        var controller = player.GetComponent<CharacterController>();
        if (map && controller && player.transform.position.y < -45f &&
            Mathf.Abs(player.transform.position.x) < 15f && Mathf.Abs(player.transform.position.z) < 25f)
        {
            controller.enabled = false;
            player.transform.position = map.ResolveSavedPosition(map.SpawnPosition, controller);
            controller.enabled = true;
        }
        Debug.Log("SA_ENTRY_READY position=" + player.transform.position);
        Loading = false;
    }
    void OnDestroy() { Loading = false; }
}
