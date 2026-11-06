using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud
{
    VisualElement menuShade,menuTabs;
    PoliceDispatchCall[] dispatchCalls;
    PoliceFine[] policeFines;

    System.Collections.IEnumerator RefreshInventoryMenu()
    {
        var account=AccountSession.Instance;
        if(!account)yield break;
        string session=account.CriminalEconomySessionKey;
        CriminalEconomyResponse response=null;
        yield return account.CriminalEconomyRequest("GET","",null,r=>response=r);
        if(CurrentPanel!="inventory"||!account||account.CriminalEconomySessionKey!=session)yield break;
        CriminalEconomyState state=null;
        if(response?.Success==true)
        {try{state=JsonUtility.FromJson<CriminalEconomyState>(response.json);}catch{}}
        content.Clear();
        BuildInventoryMenu(state,state==null?"Não foi possível consultar os itens. Abra o inventário novamente para tentar.":null);
    }

    void BuildInventoryMenu(CriminalEconomyState state,string message)
    {
        var scroll=new ScrollView(ScrollViewMode.Vertical){name="inventory-scroll"};scroll.AddToClassList("player-menu-scroll");content.Add(scroll);
        var heading=Element("inventory-header",scroll);
        Text("SEUS ITENS","player-menu-caption",heading);
        Text("10 ESPAÇOS","inventory-capacity",heading);
        if(!string.IsNullOrEmpty(message))Text(message,"inventory-hint",scroll);
        var items=new System.Collections.Generic.List<CriminalEconomyItem>();
        foreach(var item in state?.inventory??Array.Empty<CriminalEconomyItem>())
            if(item!=null&&item.quantity>0)items.Add(item);
        var documents=TutorialJourney.Instance;
        if(documents&&documents.HasRG)items.Insert(0,new CriminalEconomyItem{itemCode="rg",quantity=1});
        if(documents&&documents.HasCNH)items.Insert(Math.Min(items.Count,1),new CriminalEconomyItem{itemCode="cnh",quantity=1});
        var grid=Element("inventory-grid",scroll);
        for(int i=0;i<10;i++)
        {
            var slot=Element("inventory-slot",grid);slot.name="inventory-slot-"+(i+1);
            Text((i+1).ToString("D2"),"inventory-slot-index",slot);
            if(i>=items.Count){slot.AddToClassList("inventory-slot-empty");Text("+","inventory-slot-placeholder",slot);Text("VAZIO","inventory-slot-name",slot);continue;}
            var item=items[i];
            string icon=item.itemCode=="rg"||item.itemCode=="cnh"?"user":item.itemCode!=null&&item.itemCode.Contains("ammo")?"target":item.itemCode!=null&&(item.itemCode.Contains("ak47")||item.itemCode.Contains("m4")||item.itemCode.Contains("parafal"))?"target":"bag";
            var badge=Element("inventory-slot-icon",slot);badge.Add(HudPack.Image(icon));
            Text(InventoryItemName(item.itemCode),"inventory-slot-name",slot);
            Text("QUANTIDADE  "+item.quantity,"inventory-slot-quantity",slot);
        }
        if(items.Count>10)Text("+"+(items.Count-10)+" tipos de item não exibidos. O inventário visual tem 10 espaços por enquanto.","inventory-hint",scroll);
    }

    static string InventoryItemName(string code)
    {
        switch(code)
        {
            case "rg":return "Identidade";case "cnh":return "CNH";
            case "pilot_scrap":return "Sucata";case "pilot_crystal":return "Cristal";
            case "pilot_logistics_crate":return "Caixa logística";
            case "rp_parafal":return "Parafal (RP)";case "rp_ak47":return "AK-47 (RP)";case "rp_m4":return "M4 (RP)";
            case "rp_ammo_parafal":return "Munição Parafal";case "rp_ammo_ak47":return "Munição AK-47";case "rp_ammo_m4":return "Munição M4";
            default:return string.IsNullOrEmpty(code)?"Item":code.Replace('_',' ');
        }
    }

    internal void ShowPoliceFines(PoliceFine[] fines)
    {
        policeFines=fines??Array.Empty<PoliceFine>();
        if(CurrentPanel!="police-fines")OpenPanel("police-fines");
        else {content.Clear();BuildPoliceFines();}
    }

    void BuildPoliceFines()
    {
        var scroll=new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("player-menu-scroll");content.Add(scroll);
        MenuHeading(scroll,"Multas do personagem","O pagamento usa o saldo em dinheiro.");
        if(policeFines==null||policeFines.Length==0)
        {
            Text("Nenhuma multa registrada.","player-menu-description",scroll);
            return;
        }
        foreach(var fine in policeFines)
        {
            var card=Element("player-mission-card",scroll);
            var status=fine.status=="paid"?"PAGA":fine.status=="cancelled"?"CANCELADA":"PENDENTE";
            Text(fine.amount+" · "+status,"player-menu-caption",card);
            Text(fine.reason,"player-menu-description",card).enableRichText=false;
            if(fine.status=="pending")MenuCommand(card,"Pagar "+fine.amount,()=>InGameChat.Instance?.SendCommand("/pagarmulta "+fine.id));
        }
    }

    internal void ShowPoliceDispatch(PoliceDispatchCall[] calls)
    {
        dispatchCalls=calls??Array.Empty<PoliceDispatchCall>();
        if(CurrentPanel!="police-dispatch")OpenPanel("police-dispatch");
        else {content.Clear();BuildPoliceDispatch();}
    }

    internal void RefreshPoliceDutyMenu()
    {
        dispatchCalls=null;
        if(CurrentPanel=="police-dispatch"){content.Clear();BuildPoliceDispatch();}
    }

    void BuildPoliceDispatch()
    {
        var scroll=new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("player-menu-scroll");content.Add(scroll);
        bool onDuty=AdminPlayerPanel.PoliceDuty;
        MenuHeading(scroll,"Polícia Militar",onDuty?"Chamados da cidade e atendimentos da sua patrulha.":"Entre em serviço para consultar os chamados da cidade.");
        MenuCommand(scroll,onDuty?"Sair de serviço":"Entrar em serviço",()=>InGameChat.Instance?.SendCommand(onDuty?"/servico off":"/servico on"));
        if(!onDuty)return;
        MenuCommand(scroll,"Atualizar chamados",()=>InGameChat.Instance?.SendCommand("/chamados"));
        MenuCommand(scroll,"Ver delegacia no mapa",()=>InGameChat.Instance?.SendCommand("/delegacia"));
        foreach(var call in dispatchCalls??Array.Empty<PoliceDispatchCall>())
        {
            var card=Element("player-mission-card",scroll);
            Text("ID "+call.uid+(call.status=="accepted"?" · COM SUA PATRULHA":" · AGUARDANDO"),"player-menu-caption",card);
            var message=Text(call.message,"player-menu-description",card);
            message.enableRichText=false;
            if(call.status=="accepted")
            {
                MenuCommand(card,"Mostrar no mapa",()=>InGameChat.Instance?.SendCommand("/gpsocorrencia"));
                MenuCommand(card,"Encerrar atendimento",()=>InGameChat.Instance?.SendCommand("/encerrar190"));
            }
            else MenuCommand(card,"Aceitar chamado",()=>InGameChat.Instance?.SendCommand("/proc "+call.uid));
        }
    }
    static readonly CultureInfo MenuLocale=CultureInfo.GetCultureInfo("pt-BR");

    void UpdateMenuChrome(string panel)
    {
        bool phone=panel=="phone";
        bool wide=panel!=null&&panel!="animations"&&!phone&&!(panel=="map"&&cityMapAtlas);
        drawer.EnableInClassList("player-menu",wide);
        drawer.EnableInClassList("phone-shell",phone);
        menuShade.EnableInClassList("phone-shade",phone);
        menuShade.style.display=wide||phone?DisplayStyle.Flex:DisplayStyle.None;
        menuTabs.style.display=wide?DisplayStyle.Flex:DisplayStyle.None;
        menuTabs.Clear();
        if(!wide)return;
        MenuTab(menuTabs,"Central","menu",panel!="map"&&panel!="missions"&&panel!="wallet"&&panel!="settings",()=>OpenPanel("menu"));
        MenuTab(menuTabs,"Mapa","map",panel=="map",()=>OpenPanel("map"));
        MenuTab(menuTabs,"Missões","missions",panel=="missions",()=>OpenPanel("missions"));
        MenuTab(menuTabs,"Economia","economy",panel=="wallet",()=>CriminalEconomyPilotPanel.OpenSection("activity"));
        MenuTab(menuTabs,"Configurações","settings",panel=="settings",()=>OpenPanel("settings"));
    }

    Button MenuTab(VisualElement parent,string title,string id,bool selected,Action click)
    {
        var button=new Button(()=>{if(!selected||id=="economy"||id=="menu"&&CurrentPanel!="menu")click();}){text=title,name="menu-tab-"+id};
        button.AddToClassList("player-menu-tab");
        button.EnableInClassList("player-menu-active",selected);
        parent.Add(button);return button;
    }

    void ScrollSimpleMenu(string panel)
    {
        switch(panel)
        {
            case "session": case "vehicles": case "actions": case "player": case "help":
                var items=content.Children().ToArray();
                var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("player-menu-scroll");content.Add(scroll);
                foreach(var item in items)scroll.Add(item);
                break;
        }
    }

    VisualElement MenuColumns(string heading,out VisualElement categories)
    {
        var columns=Element("player-menu-columns",content);
        categories=Element("player-menu-sidebar",columns);
        Text(heading,"player-menu-caption",categories);
        return Element("player-menu-main",columns);
    }

    void BuildSettingsMenu(string section,bool refocus=false)
    {
        content.Clear();
        var main=MenuColumns("PREFERÊNCIAS",out var categories);
        foreach(var item in new[]{("camera","Câmera"),("interface","Interface"),("controls","Controles"),("audio","Áudio")})
        {
            string key=item.Item1;
            var tab=MenuTab(categories,item.Item2,key,section==key,()=>BuildSettingsMenu(key,true));
            if(refocus&&section==key)tab.schedule.Execute(()=>tab.Focus());
        }
        var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("player-menu-scroll");main.Add(scroll);
        switch(section)
        {
            case "interface":
                MenuHeading(scroll,"Interface","Organize os botões de ação do seu jeito.");
                MenuCommand(scroll,"Editar HUD",OpenActionEditor);
                break;
            case "controls":
                MenuHeading(scroll,"Controles","Atalhos de teclado e mouse. No celular, use os botões na tela.");
                foreach(var binding in new[]{("W A S D","Mover"),("SHIFT","Correr"),("ESPAÇO","Pular"),("F","Atacar"),
                    ("MOUSE","Girar a câmera"),("R","Recarregar arma"),("TAB","Abrir central"),("M","Abrir mapa"),("F7","Abrir economia"),("ESC","Voltar ao jogo")})
                {
                    var row=Element("player-menu-row",scroll);
                    Text(binding.Item2,"player-menu-value-label",row);Text(binding.Item1,"keyboard-key",row);
                }
                break;
            case "audio":
                MenuHeading(scroll,"Áudio","Ajuste o volume geral do jogo.");
                MenuSlider(scroll,"Volume geral","master-volume",0,100,AudioListener.volume*100,"0", "%",v=>
                {AudioListener.volume=v/100;PlayerPrefs.SetFloat("hud.masterVolume",AudioListener.volume);});
                MenuCommand(scroll,"Restaurar volume padrão",()=>
                { AudioListener.volume=1;PlayerPrefs.DeleteKey("hud.masterVolume");PlayerPrefs.Save();BuildSettingsMenu("audio",true); });
                break;
            default:
                MenuHeading(scroll,"Câmera","Ajuste o movimento e a distância da câmera em terceira pessoa.");
                MenuSlider(scroll,"Sensibilidade","camera-sensitivity",.4f,2f,orbit.sensitivity,"0.00","",v=>
                {orbit.sensitivity=v;PlayerPrefs.SetFloat("hud.sensitivity",v);});
                MenuSlider(scroll,"Distância da câmera","camera-distance",2,6,orbit.distance,"0.0"," m",v=>
                {orbit.distance=v;PlayerPrefs.SetFloat("hud.distance",v);});
                MenuCommand(scroll,"Restaurar câmera padrão",()=>
                {
                    PlayerPrefs.DeleteKey("hud.sensitivity");PlayerPrefs.DeleteKey("hud.distance");PlayerPrefs.Save();
                    orbit.sensitivity=1;orbit.distance=4.2f;BuildSettingsMenu("camera",true);
                });
                break;
        }
    }

    void MenuHeading(VisualElement parent,string title,string description)
    {
        var head=Element("player-menu-section",parent);
        Text(title,"player-menu-heading",head);Text(description,"player-menu-description",head);
    }

    void MenuCommand(VisualElement parent,string title,Action action)
    {
        var button=new Button(action){text=title};button.AddToClassList("player-menu-command");parent.Add(button);
    }

    void MenuSlider(VisualElement parent,string title,string id,float low,float high,float value,string format,string suffix,Action<float> change)
    {
        var row=Element("player-menu-row",parent);
        Text(title,"player-menu-value-label",row);
        var slider=new Slider(low,high){name=id,tooltip=title,value=value};
        slider.AddToClassList("player-menu-slider");row.Add(slider);
        string Display(float v)=>v.ToString(format,MenuLocale)+suffix;
        var readout=Text(Display(value),"player-menu-readout",row);
        slider.RegisterValueChangedCallback(e=>{change(e.newValue);readout.text=Display(e.newValue);PlayerPrefs.Save();});
    }

    void BuildMissionMenu(string section,bool refocus=false)
    {
        content.Clear();
        var main=MenuColumns("CATEGORIAS",out var categories);
        string title="Tutorial";
        foreach(var item in new[]{("tutorial","Tutorial"),("city","Cidade"),("daily","Diárias"),("weekly","Semanais")})
        {
            string key=item.Item1;
            var tab=MenuTab(categories,item.Item2,"mission-"+key,section==key,()=>BuildMissionMenu(key,true));
            if(section==key){title=item.Item2;if(refocus)tab.schedule.Execute(()=>tab.Focus());}
        }
        var catalog=MissionCatalog.Data;
        var entries=section=="daily"?catalog.daily:section=="weekly"?catalog.weekly:
            catalog.missions.Where(m=>(m.category=="tutorial")== (section=="tutorial")).ToArray();
        string phase=TutorialJourney.Instance?TutorialJourney.Instance.Phase:"available";
        MenuHeading(main,title,section=="tutorial"?"Acompanhe as etapas iniciais do seu personagem.":"Consulte objetivos, disponibilidade e recompensas.");
        var list=new ScrollView(ScrollViewMode.Vertical);list.AddToClassList("player-menu-scroll");main.Add(list);
        if(entries==null||entries.Length==0)
        {
            MenuHeading(list,"Nenhuma missão disponível","As missões desta categoria aparecem aqui quando forem liberadas.");
            return;
        }
        foreach(var mission in entries)
        {
            var card=Element("player-mission-card",list);
            var titleRow=Element("player-mission-title-row",card);
            Text(mission.order.ToString("00"),"player-mission-number",titleRow);
            Text(mission.title,"player-mission-title",titleRow);
            string status=MissionStatus(mission,phase);
            var badge=Text(status.Split('·')[0].Trim(),"player-mission-status",titleRow);
            badge.EnableInClassList("player-mission-available",status=="DISPONÍVEL"||status=="EM ANDAMENTO");
            if(mission.objectives!=null&&mission.objectives.Length>0)
                Text(string.Join("  ·  ",mission.objectives),"player-menu-description",card);
            string reward=mission.rewardXp.ToString("N0",MenuLocale)+" XP";
            if(mission.rewardCashCents>0)reward+="  +  "+(mission.rewardCashCents/100m).ToString("C2",MenuLocale);
            Text("RECOMPENSA   "+reward,"player-menu-caption",card);
        }
    }

    void BuildWalletMenu()
    {
        var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("player-menu-scroll");content.Add(scroll);
        MenuHeading(scroll,"Carteira do personagem","Consulte seu dinheiro e os valores de teste do tutorial.");
        var values=Element("player-wallet-grid",scroll);
        WalletValue(values,"SALDO DA CONTA",player.Cash.ToString("C0",MenuLocale));
        var journey=TutorialJourney.Instance;
        WalletValue(values,"HZ COINS DE TESTE",(journey?journey.DemoCoins:0).ToString("N0",MenuLocale));
        WalletValue(values,"DOAÇÕES DE TESTE",(journey?journey.DemoDonations:0).ToString("C0",MenuLocale));
        MenuCommand(scroll,"Abrir economia da cidade",()=>CriminalEconomyPilotPanel.OpenSection("activity"));
        MenuCommand(scroll,"Consultar multas",()=>InGameChat.Instance?.SendCommand("/minhasmultas"));
    }

    void WalletValue(VisualElement parent,string caption,string value)
    {
        var card=Element("player-wallet-card",parent);
        Text(caption,"player-menu-caption",card);Text(value,"player-wallet-value",card);
    }
}
