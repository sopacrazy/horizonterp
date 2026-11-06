using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Uses the offline session established by --verify-sa-session.
internal static class PlayerMenuVerification
{
    internal static IEnumerator Run(Action<bool> finish)
    {
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../tools/player-menu-review"));
        Directory.CreateDirectory(folder);
        var hud=GameHud.Instance;
        hud.OpenPanel("settings");
        yield return Capture(folder,"settings");
        var drawer=hud.Root.Q("hud-drawer");
        var camera=drawer.Q<Slider>("camera-sensitivity");
        bool settings=drawer.worldBound.width>hud.Root.worldBound.width*.7f&&Visible(camera)&&Visible(drawer.Q<Slider>("camera-distance"));
        Submit(drawer.Q<Button>("menu-tab-audio"));yield return null;
        settings&=Visible(drawer.Q<Slider>("master-volume"));
        Submit(drawer.Q<Button>("menu-tab-interface"));yield return null;
        settings&=drawer.Query<Button>().ToList().Exists(b=>b.text=="Editar HUD"&&Visible(b));

        Submit(drawer.Q<Button>("menu-tab-missions"));yield return Capture(folder,"missions");
        bool missions=drawer.Query<VisualElement>(className:"player-mission-card").ToList().Count>0;
        Submit(drawer.Q<Button>("menu-tab-mission-daily"));yield return null;
        missions&=drawer.Query<VisualElement>(className:"player-mission-card").ToList().Count==MissionCatalog.Data.daily.Length;
        Submit(drawer.Q<Button>("menu-tab-economy"));yield return Capture(folder,"economy");
        bool economy=CriminalEconomyPilotPanel.IsOpen&&hud.CurrentPanel==null;
        CriminalEconomyPilotPanel.CloseOverlay();

        hud.OpenPanel("phone");yield return Capture(folder,"phone");
        var phoneDrawer=hud.Root.Q("hud-drawer");
        bool phone=phoneDrawer.ClassListContains("phone-shell")&&Visible(phoneDrawer.Q(className:"phone-screen"))&&
            phoneDrawer.Query<Button>(className:"phone-app").ToList().Count==7&&
            phoneDrawer.Query<Button>(className:"phone-dock-app").ToList().Count==3;
        hud.ClosePanel();

        var obj=new GameObject("Prévia de seleção de cidades");
        var doc=obj.AddComponent<UIDocument>();doc.panelSettings=Resources.Load<PanelSettings>("HudPanel");doc.sortingOrder=250;
        var root=doc.rootVisualElement;root.AddToClassList("auth-root");
        root.styleSheets.Add(Resources.Load<StyleSheet>("AuthStyles"));root.styleSheets.Add(Resources.Load<StyleSheet>("Startup/EntryFlow"));
        root.Add(HorizonteEntryVisuals.Image(HorizonteEntryVisuals.City,"auth-art",ScaleMode.ScaleAndCrop));
        var screen=new VisualElement();screen.AddToClassList("entry-screen");root.Add(screen);
        bool entered=false;
        AccountSession.BuildCitySelection(screen,"18",()=>entered=true,()=>{});
        var country=screen.Q<Button>("city-country-br");
        var spain=screen.Q<Button>("city-country-es");
        var unitedStates=screen.Q<Button>("city-country-us");
        var enter=screen.Q<Button>("city-select-enter");
        var countrySelect=screen.Q<Button>("city-country-select");
        var countryOptions=screen.Q(className:"entry-countries");
        var serverList=screen.Q(className:"entry-servers");
        bool serversReady=country?.Q<Image>()?.image!=null&&country.ClassListContains("entry-country-selected")&&
            enter!=null&&enter.enabledSelf&&serverList!=null&&serverList.style.display.value==DisplayStyle.Flex&&
            countryOptions?.style.display.value==DisplayStyle.None;
        bool countriesComingSoon=true;
        foreach(var futureCountry in new[]{spain,unitedStates})
        {
            Submit(countrySelect);
            countriesComingSoon &= countryOptions.style.display.value==DisplayStyle.Flex;
            Submit(futureCountry);
            countriesComingSoon &= futureCountry?.Q<Image>()?.image!=null&&enter!=null&&enter.enabledSelf&&
                serverList!=null&&serverList.style.display.value==DisplayStyle.Flex&&
                countryOptions.style.display.value==DisplayStyle.None&&!entered;
        }
        Submit(countrySelect);
        Submit(country);
        yield return Capture(folder,"servers");
        var active=screen.Q("city-server-01");var locked=screen.Q("city-server-02");
        Submit(enter);yield return null;
        bool servers=serversReady&&countriesComingSoon&&Visible(active)&&Visible(locked)&&active.Q<Image>()?.image!=null&&locked.Q<Image>()?.image!=null&&
            locked.Q<Button>()==null&&Visible(enter)&&entered;
        UnityEngine.Object.Destroy(obj);
        bool pass=settings&&missions&&economy&&phone&&servers;
        Debug.Log("PLAYER_MENU_"+(pass?"PASSED":"FAILED")+" settings="+settings+" missions="+missions+" economy="+economy+" phone="+phone+" servers="+servers);
        finish(pass);
    }
    static bool Visible(VisualElement element)=>element!=null&&element.worldBound.width>1&&element.worldBound.height>1;
    static void Submit(Button button)
    {
        if(button==null)return;
        button.Focus();using(var submit=NavigationSubmitEvent.GetPooled())button.SendEvent(submit);
    }
    static IEnumerator Capture(string folder,string name)
    {
        yield return null;yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));yield return null;
    }
}
