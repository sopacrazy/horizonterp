using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud
{
    readonly Dictionary<string, Button> economyMiniDots = new Dictionary<string, Button>();
    readonly Dictionary<string, Button> economyMapDots = new Dictionary<string, Button>();
    readonly Dictionary<string, CriminalEconomyLocation> economyLocations = new Dictionary<string, CriminalEconomyLocation>();
    readonly HashSet<string> economyLive = new HashSet<string>();
    readonly List<string> economyRemoved = new List<string>();

    void ResetEconomyMapMarkers()
    {
        foreach (var dot in economyMapDots.Values)
        {
            pointerBlockers.Remove(dot);
            dot.RemoveFromHierarchy();
        }
        economyMapDots.Clear();
    }

    void RefreshEconomyMarkers(float width)
    {
        economyLive.Clear();
        economyLocations.Clear();
        if (CurrentPanel != "map")
            foreach (var dot in economyMapDots.Values) dot.style.display = DisplayStyle.None;
        var self = player.transform.position;
        foreach (var location in CriminalEconomyWorld.Locations)
        {
            economyLive.Add(location.code);
            economyLocations[location.code] = location;
            var position = new Vector3(location.x, location.y, location.z);
            if (!economyMiniDots.TryGetValue(location.code, out var mini))
            {
                mini = EconomyMarker(location, true);
                miniViewport.Add(mini);
                economyMiniDots.Add(location.code, mini);
            }
            mini.tooltip = CriminalEconomyWorld.Caption(location);
            bool near = PlayerRadar.Visible(false, false, self, position);
            mini.style.display = near ? DisplayStyle.Flex : DisplayStyle.None;
            if (near)
            {
                var point = PlayerRadar.OnMinimap(self, position, cityMinimapYaw, width, MinimapVisibleMetres);
                point = Vector2.ClampMagnitude(point, width * .5f - 9);
                mini.style.left = width * .5f + point.x - 9;
                mini.style.top = width * .5f + point.y - 9;
            }
            if (CurrentPanel != "map" || mapArea == null) continue;
            if (!economyMapDots.TryGetValue(location.code, out var map))
            {
                map = EconomyMarker(location, false);
                mapArea.Add(map);
                economyMapDots.Add(location.code, map);
            }
            var uv = CityMapUV(position);
            bool onAtlas = uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
            map.tooltip = CriminalEconomyWorld.Caption(location) + " · " + Mathf.CeilToInt(Vector2.Distance(
                new Vector2(self.x, self.z), new Vector2(position.x, position.z))) + " m";
            map.style.display = onAtlas ? DisplayStyle.Flex : DisplayStyle.None;
            var mapPoint = cityImageOffset + uv * cityImageSize;
            map.style.left = mapPoint.x - 14;
            map.style.top = mapPoint.y - 14;
        }
        RemoveOldEconomyMarkers(economyMiniDots);
        RemoveOldEconomyMarkers(economyMapDots);
    }

    Button EconomyMarker(CriminalEconomyLocation location, bool mini)
    {
        string code = location.code;
        var dot = new Button(() => OpenEconomyMapPoint(code))
        {
            name = (mini ? "mini-economy-" : "map-economy-") + code,
            text = location.kind == "warehouse" ? "D" : location.kind == "craft" ? "B" : location.kind == "shop" ? "L" : "C",
            tooltip = CriminalEconomyWorld.Caption(location)
        };
        float size = mini ? 18 : 28;
        dot.style.position = Position.Absolute;
        dot.style.width = dot.style.height = size;
        dot.style.minWidth = dot.style.minHeight = size;
        dot.style.marginLeft = dot.style.marginRight = dot.style.marginTop = dot.style.marginBottom = 0;
        dot.style.paddingLeft = dot.style.paddingRight = dot.style.paddingTop = dot.style.paddingBottom = 0;
        dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = size / 2;
        dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = size / 2;
        dot.style.borderTopWidth = dot.style.borderBottomWidth = dot.style.borderLeftWidth = dot.style.borderRightWidth = 2;
        dot.style.borderTopColor = dot.style.borderBottomColor = dot.style.borderLeftColor = dot.style.borderRightColor = new Color(.12f, .07f, .16f);
        dot.style.backgroundColor = CriminalEconomyWorld.LocationColor(location.kind);
        dot.style.color = new Color(.1f, .06f, .14f);
        dot.style.fontSize = mini ? 10 : 14;
        dot.style.unityFontStyleAndWeight = FontStyle.Bold;
        dot.style.unityTextAlign = TextAnchor.MiddleCenter;
        // A marker press must never reach the administrative map destination handler.
        dot.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        dot.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        dot.RegisterCallback<ClickEvent>(e => e.StopPropagation());
        RegisterPointerBlocker(dot);
        return dot;
    }

    void OpenEconomyMapPoint(string code)
    {
        if (AccountSession.InputBlocked || StartupLoader.IsLoading || !economyLocations.TryGetValue(code, out var location)) return;
        CancelCityMapSelection();
        ShowToast(CriminalEconomyWorld.Caption(location));
        if (CurrentPanel == "map") ClosePanel();
        CriminalEconomyPilotPanel.OpenSection(location.kind, location.activityCode);
    }

    void RemoveOldEconomyMarkers(Dictionary<string, Button> dots)
    {
        economyRemoved.Clear();
        foreach (var code in dots.Keys) if (!economyLive.Contains(code)) economyRemoved.Add(code);
        foreach (var code in economyRemoved)
        {
            pointerBlockers.Remove(dots[code]);
            dots[code].RemoveFromHierarchy();
            dots.Remove(code);
        }
    }
}
