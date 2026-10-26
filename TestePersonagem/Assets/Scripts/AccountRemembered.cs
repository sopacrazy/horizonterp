using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    VisualElement remembered;
    Button rememberedPlay,rememberedSwitch;
    Label rememberedMessage;
    void HideRemembered(){if(remembered!=null)remembered.style.display=DisplayStyle.None;}
    void ShowRemembered(string text="")
    {
        if(remembered==null)
        {
            remembered=new VisualElement{name="remembered-access"};root.Add(remembered);
            remembered.style.position=Position.Absolute;remembered.style.left=0;remembered.style.right=0;remembered.style.top=0;remembered.style.bottom=0;
            remembered.style.alignItems=Align.Center;remembered.style.justifyContent=Justify.FlexEnd;remembered.style.paddingBottom=Length.Percent(12);
            rememberedMessage=new Label{enableRichText=false,name="remembered-message"};remembered.Add(rememberedMessage);
            rememberedMessage.style.color=Color.white;rememberedMessage.style.fontSize=14;rememberedMessage.style.marginBottom=14;
            rememberedMessage.style.whiteSpace=WhiteSpace.Normal;rememberedMessage.style.maxWidth=Length.Percent(85);
            rememberedPlay=new Button(()=>{if(busy)return;if(HasCharacter)EnterCity();else StartCoroutine(RestoreRemembered());}){text="JOGAR",name="remembered-play"};remembered.Add(rememberedPlay);
            rememberedPlay.style.width=320;rememberedPlay.style.maxWidth=Length.Percent(85);rememberedPlay.style.height=64;
            rememberedPlay.style.backgroundColor=new Color(.10f,.67f,.33f);rememberedPlay.style.color=Color.white;
            rememberedPlay.style.fontSize=23;rememberedPlay.style.unityFontStyleAndWeight=FontStyle.Bold;
            rememberedPlay.style.borderTopLeftRadius=12;rememberedPlay.style.borderTopRightRadius=12;rememberedPlay.style.borderBottomLeftRadius=12;rememberedPlay.style.borderBottomRightRadius=12;
            rememberedSwitch=new Button(()=>{if(busy)return;if(Authenticated)Logout();else {SessionVault.Clear();HideRemembered();ShowLogin();}}){text="Trocar conta",name="remembered-switch"};remembered.Add(rememberedSwitch);
            rememberedSwitch.style.backgroundColor=Color.clear;rememberedSwitch.style.color=Color.white;rememberedSwitch.style.marginTop=14;
            rememberedSwitch.style.borderTopWidth=0;rememberedSwitch.style.borderBottomWidth=0;rememberedSwitch.style.borderLeftWidth=0;rememberedSwitch.style.borderRightWidth=0;
        }
        HideCharacterLobby();landing.style.display=DisplayStyle.None;scroll.style.display=DisplayStyle.None;
        AuthVisible=true;ReadyToEnterCity=false;root.style.display=DisplayStyle.Flex;remembered.style.display=DisplayStyle.Flex;
        rememberedMessage.text=text;rememberedPlay.SetEnabled(!busy);rememberedSwitch.SetEnabled(!busy);
    }
    internal IEnumerator RestoreRemembered()
    {
        string stored=SessionVault.Read();
        if(string.IsNullOrEmpty(stored)||!Regex.IsMatch(stored,"^[A-Za-z0-9_-]{43}$"))
        {SessionVault.Clear();ShowLogin();yield break;}
        busy=true;token=stored;ShowRemembered("Verificando seu acesso...");
        ApiReply reply=null;yield return Request("GET","/v1/player",null,true,r=>reply=r);
        busy=false;
        if(reply!=null&&(reply.needsCharacter||!string.IsNullOrEmpty(reply.state?.characterId)))
        {
            State=reply.state;pending=null;leaving=false;
            if(HasCharacter)ShowRemembered();else {HideRemembered();ShowAccountCharacter();}
        }
        else if(Authenticated)
        {
            // A network outage must not discard a valid saved session or allow offline entry.
            token=null;State=null;ShowRemembered("Não foi possível verificar o acesso. Confira a conexão e toque em Jogar para tentar novamente.");
        }
        else {HideRemembered();ShowLogin(string.IsNullOrEmpty(LastError)?"Entre novamente na sua conta.":LastError);}
    }
}
