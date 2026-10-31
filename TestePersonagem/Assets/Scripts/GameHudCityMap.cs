using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud
{
    Texture2D cityMapAtlas;
    VisualElement cityMapPivot;
    // Crop fitted against 9,505 road-surface samples from the imported placements/meshes.
    // Pixel mapping: x = world.x * .1475 - 9.6; y = world.z * .1499 - 116.1 (428 x 300 reference).
    // The artwork has its own crop; the playable region limits are not its image bounds.
    static readonly Vector2 AtlasMin = new Vector2(65.08475f, 774.51634f);
    static readonly Vector2 AtlasMax = new Vector2(2966.77966f, 2775.85057f);
    const float MinimapVisibleMetres = 450;
    float cityMinimapYaw;
    readonly MinimapHeading cityHeading=new MinimapHeading();
    Vector2 cityMapPan, cityImageOffset, cityImageSize, cityDragStart, cityDragPan;
    int cityDragPointer = -1;
    bool cityMapDragged,cityMapTeleportBusy;
    int cityMapVisit;
    int citySelectionVersion;
    bool cityPointSelected;
    Vector2 citySelectedTarget;
    VisualElement cityDestinationMarker,cityTeleportConfirm;
    Button cityTeleportGo;
    bool CanUseMapTeleport => AccountSession.HasCharacter && AccountSession.Instance.State.accountRole == "admin" && AccountSession.Instance.State.adminLevel > 0;

    static Vector2 CityMapUV(Vector3 world) => new Vector2(
        (world.x - AtlasMin.x) / (AtlasMax.x - AtlasMin.x),
        (world.z - AtlasMin.y) / (AtlasMax.y - AtlasMin.y));

    void BuildCityMinimap()
    {
        cityMapPivot = new VisualElement { pickingMode = PickingMode.Ignore };
        cityMapPivot.style.position = Position.Absolute;
        cityMapPivot.style.left = cityMapPivot.style.top = 0;
        cityMapPivot.style.width = cityMapPivot.style.height = Length.Percent(100);
        miniViewport.Add(cityMapPivot);
        miniImage = new Image { image = cityMapAtlas, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
        miniImage.style.position = Position.Absolute;
        cityMapPivot.Add(miniImage);
    }

    void BuildCityMapPanel()
    {
        cityMapVisit++;
        CancelCityMapSelection();
        cityMapPan = Vector2.zero;
        cityDragPointer = -1;
        mapArea = Element("city-map-canvas", content);
        mapArea.tooltip = CanUseMapTeleport ? "Clique para marcar um destino · Arraste para explorar" : "Arraste para explorar o mapa";
        mapImage = new Image { image = cityMapAtlas, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
        mapImage.AddToClassList("mini-image"); mapArea.Add(mapImage);
        mapMarker = new HudIcon("arrow"); mapMarker.SetInk(PlayerMapBlue);
        mapMarker.AddToClassList("city-map-player"); mapArea.Add(mapMarker);
        cityDestinationMarker=Element("city-map-destination",mapArea);
        cityDestinationMarker.pickingMode=PickingMode.Ignore;
        Text("DESTINO","city-map-destination-label",cityDestinationMarker);
        cityDestinationMarker.style.display=DisplayStyle.None;
        mapArea.RegisterCallback<PointerDownEvent>(evt =>
        {
            if(evt.button != 0 || cityDragPointer >= 0) return;
            cityDragPointer = evt.pointerId; cityDragStart = evt.position; cityDragPan = cityMapPan;
            cityMapDragged = false;
            mapArea.CapturePointer(evt.pointerId); evt.StopPropagation();
        });
        mapArea.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if(evt.pointerId != cityDragPointer) return;
            if(((Vector2)evt.position-cityDragStart).sqrMagnitude>64)cityMapDragged=true;
            cityMapPan = cityDragPan + (Vector2)evt.position - cityDragStart;
            FitCityMap(); evt.StopPropagation();
        });
        mapArea.RegisterCallback<PointerUpEvent>(evt =>
        {
            if(evt.pointerId != cityDragPointer) return;
            bool select = !cityMapDragged && ((Vector2)evt.position-cityDragStart).sqrMagnitude<=64;
            cityDragPointer = -1; mapArea.ReleasePointer(evt.pointerId); evt.StopPropagation();
            if(select && CanUseMapTeleport && !cityMapTeleportBusy)
                SelectCityMapDestination(evt.localPosition);
        });
        mapArea.RegisterCallback<PointerCaptureOutEvent>(_ => cityDragPointer = -1);
        cityTeleportConfirm=Element("city-teleport-confirm",content);
        Text("TELEPORTE ADMINISTRATIVO","city-teleport-kicker",cityTeleportConfirm);
        Text("Ir para este destino?","city-teleport-title",cityTeleportConfirm);
        var actions=Element("city-teleport-actions",cityTeleportConfirm);
        var cancel=new Button(CancelCityMapSelection){text="Cancelar"};cancel.AddToClassList("city-teleport-cancel");actions.Add(cancel);
        cityTeleportGo=new Button(()=>{if(cityPointSelected&&!cityMapTeleportBusy)StartCoroutine(RequestCityMapTeleport(citySelectedTarget));}){text="Ir para destino"};
        cityTeleportGo.AddToClassList("city-teleport-go");actions.Add(cityTeleportGo);
        cityTeleportConfirm.style.display=DisplayStyle.None;
        IconButton("close", "city-map-close", "Fechar mapa [Esc]", ClosePanel, content).name = "close-city-map";
        content.schedule.Execute(FitCityMap);
    }

    void CancelCityMapSelection()
    {
        cityPointSelected=false;citySelectionVersion++;
        if(cityDestinationMarker!=null)cityDestinationMarker.style.display=DisplayStyle.None;
        if(cityTeleportConfirm!=null)cityTeleportConfirm.style.display=DisplayStyle.None;
        FindAnyObjectByType<GtaSaSectorStreaming>()?.ClearMapDestinationPreview();
    }

    void SelectCityMapDestination(Vector2 localPoint)
    {
        if(!CanUseMapTeleport||cityMapTeleportBusy||player.IsTeleporting||cityImageSize.x<=0||cityImageSize.y<=0)return;
        Vector2 uv=(localPoint-cityImageOffset)/cityImageSize;
        if(uv.x<0||uv.x>1||uv.y<0||uv.y>1)return;
        var target=new Vector2(Mathf.Lerp(AtlasMin.x,AtlasMax.x,uv.x),Mathf.Lerp(AtlasMin.y,AtlasMax.y,uv.y));
        if(NeighborhoodMap.Active&&!NeighborhoodMap.Active.ContainsXZ(new Vector3(target.x,0,target.y)))
        {ShowToast("Este ponto está fora da região disponível.");return;}
        citySelectedTarget=target;cityPointSelected=true;citySelectionVersion++;
        cityTeleportConfirm.style.display=DisplayStyle.Flex;
        UpdateCityMapMarker();
        FindAnyObjectByType<GtaSaSectorStreaming>()?.PreviewMapDestination(target);
    }

    System.Collections.IEnumerator RequestCityMapTeleport(Vector2 target)
    {
        int visit=cityMapVisit;
        int selection=citySelectionVersion;
        if(!CanUseMapTeleport || !cityPointSelected || player.IsTeleporting) yield break;
        if(player.IsCuffed || player.InPoliceCar || player.IsSpectating || player.CombatLocked || MissionVehicle.Driver==player)
        {ShowToast("Saia do veículo ou encerre a ação atual antes de teleportar.");yield break;}
        if(NeighborhoodMap.Active&&!NeighborhoodMap.Active.ContainsXZ(new Vector3(target.x,0,target.y)))
        {ShowToast("Este ponto está fora da região disponível.");yield break;}
        cityMapTeleportBusy=true;
        cityTeleportGo.SetEnabled(false);
        try
        {
            AdminReply reply=null;
            yield return AccountSession.Instance.AdminRequest("POST","/v1/admin/map-teleport",
                JsonUtility.ToJson(new AdminMapPoint{x=target.x,z=target.y}),r=>reply=r);
            if(!CanUseMapTeleport || CurrentPanel!="map" || visit!=cityMapVisit || selection!=citySelectionVersion || !cityPointSelected)yield break;
            if(reply==null||!reply.authorized||!string.IsNullOrEmpty(reply.code))
            {ShowToast(reply?.error??"Não foi possível autorizar o teleporte.");yield break;}
            ClosePanel();
            if(!player.BeginAdminMapTeleport(new Vector2(reply.x,reply.z)))ShowToast("Não é possível teleportar durante a ação atual.");
        }
        finally {cityMapTeleportBusy=false;cityTeleportGo?.SetEnabled(true);}
    }

    void OnCityMapLayout(GeometryChangedEvent evt) => FitCityMap();

    void FitCityMap()
    {
        if(CurrentPanel != "map" || mapArea == null || !cityMapAtlas) return;
        float width = content.resolvedStyle.width;
        float height = content.resolvedStyle.height;
        if(float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
        float ratio = (float)cityMapAtlas.width / cityMapAtlas.height;
        // Cover the modal without stretching streets. Dragging reveals the cropped edges.
        float fitWidth = Mathf.Max(width, height * ratio);
        cityImageSize = new Vector2(fitWidth, fitWidth / ratio);
        Vector2 excess = (cityImageSize - new Vector2(width, height)) * .5f;
        cityMapPan.x = Mathf.Clamp(cityMapPan.x, -excess.x, excess.x);
        cityMapPan.y = Mathf.Clamp(cityMapPan.y, -excess.y, excess.y);
        cityImageOffset = -excess + cityMapPan;
        mapImage.style.right = StyleKeyword.Auto; mapImage.style.bottom = StyleKeyword.Auto;
        mapImage.style.width = cityImageSize.x; mapImage.style.height = cityImageSize.y;
        mapImage.style.left = cityImageOffset.x; mapImage.style.top = cityImageOffset.y;
        UpdateCityMapMarker();
    }

    void UpdateCityMapMarker()
    {
        if(mapMarker == null || mapArea == null) return;
        Vector2 uv = CityMapUV(player.transform.position);
        float width = mapArea.resolvedStyle.width, height = mapArea.resolvedStyle.height;
        if(float.IsNaN(width) || float.IsNaN(height)) return;
        bool inside = uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
        mapMarker.style.display = inside ? DisplayStyle.Flex : DisplayStyle.None;
        mapMarker.style.left = cityImageOffset.x + uv.x * cityImageSize.x - 12;
        mapMarker.style.top = cityImageOffset.y + uv.y * cityImageSize.y - 12;
        mapMarker.style.rotate = new Rotate(new Angle(180 - player.transform.eulerAngles.y, AngleUnit.Degree));
        if(cityDestinationMarker!=null)
        {
            cityDestinationMarker.style.display=cityPointSelected?DisplayStyle.Flex:DisplayStyle.None;
            Vector2 destination=CityMapUV(new Vector3(citySelectedTarget.x,0,citySelectedTarget.y));
            cityDestinationMarker.style.left=cityImageOffset.x+destination.x*cityImageSize.x-11;
            cityDestinationMarker.style.top=cityImageOffset.y+destination.y*cityImageSize.y-11;
        }
    }

    void RefreshCityMap()
    {
        if(miniImage == null || cityMapPivot == null) return;
        float width = miniViewport.resolvedStyle.width, height = miniViewport.resolvedStyle.height;
        if(float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
        Vector2 uv = CityMapUV(player.transform.position);
        float scale = width / MinimapVisibleMetres;
        float imageWidth = (AtlasMax.x - AtlasMin.x) * scale;
        float imageHeight = (AtlasMax.y - AtlasMin.y) * scale;
        miniImage.style.width = imageWidth; miniImage.style.height = imageHeight;
        miniImage.style.left = width * .5f - uv.x * imageWidth;
        miniImage.style.top = height * .5f - uv.y * imageHeight;
        cityMinimapYaw = cityHeading.Step(player.transform.eulerAngles.y,Time.unscaledDeltaTime);
        cityMapPivot.style.rotate = new Rotate(new Angle(cityMinimapYaw + 180, AngleUnit.Degree));
        miniMarker.style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
        if(minimapNorth != null)
        {
            float angle = (cityMinimapYaw + 180) * Mathf.Deg2Rad;
            minimapNorth.style.left = width * .5f + Mathf.Sin(angle) * (width * .5f - 17) - 12;
            minimapNorth.style.top = height * .5f - Mathf.Cos(angle) * (height * .5f - 17) - 7;
        }
        UpdateCityMapMarker();
    }
}
