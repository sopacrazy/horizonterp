using System;
using UnityEngine;

// The isolated example keeps a cheap city silhouette while detailed sectors stream nearby.
public sealed class CityExampleHorizon : MonoBehaviour
{
    [Serializable]
    public class SectorProxy
    {
        public string scene;
        public Renderer[] renderers = Array.Empty<Renderer>();
    }

    public SectorProxy[] proxies = Array.Empty<SectorProxy>();
    public CitySectorStreaming streaming;
    float nextRefresh;

    void Awake()
    {
        if (!streaming) streaming = FindAnyObjectByType<CitySectorStreaming>();
        Refresh();
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .3f;
        Refresh();
    }

    void Refresh()
    {
        if (proxies == null) return;
        foreach (var proxy in proxies)
        {
            if (proxy == null || proxy.renderers == null) continue;
            bool visible = !streaming || string.IsNullOrEmpty(proxy.scene) || !streaming.IsSectorLoaded(proxy.scene);
            foreach (var renderer in proxy.renderers)
                if (renderer && renderer.enabled != visible) renderer.enabled = visible;
        }
    }
}
