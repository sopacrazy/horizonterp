using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    VisualElement remembered;
    VisualElement rememberedCanvas;
    Texture2D rememberedArtwork;
    Button rememberedPlay,rememberedSwitch;
    Label rememberedMessage;
    void HideRemembered(){if(remembered!=null)remembered.style.display=DisplayStyle.None;}
    void ShowRemembered(string text="")
    {
        if(remembered==null)
        {
            remembered=new VisualElement{name="remembered-access"};root.Add(remembered);
            remembered.style.position=Position.Absolute;remembered.style.left=0;remembered.style.right=0;remembered.style.top=0;remembered.style.bottom=0;
            remembered.style.backgroundColor=Color.black;
            rememberedArtwork=Resources.Load<Texture2D>("Startup/RememberedEntry");
            var image=new Image{name="remembered-artwork",image=rememberedArtwork,scaleMode=ScaleMode.ScaleAndCrop,pickingMode=PickingMode.Ignore};
            image.style.position=Position.Absolute;image.style.left=image.style.right=image.style.top=image.style.bottom=0;remembered.Add(image);
            rememberedCanvas=new VisualElement{name="remembered-canvas",pickingMode=PickingMode.Ignore};rememberedCanvas.style.position=Position.Absolute;remembered.Add(rememberedCanvas);
            var logo=new Image{name="remembered-logo",image=Resources.Load<Texture2D>("Startup/RememberedLogo"),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};logo.AddToClassList("remembered-logo");rememberedCanvas.Add(logo);
            var kicker=new Label("BEM-VINDO AO HORIZONTE"){name="remembered-kicker",pickingMode=PickingMode.Ignore};kicker.AddToClassList("remembered-kicker");rememberedCanvas.Add(kicker);
            var title=new Label("Sua história\ncomeça aqui."){name="remembered-title",pickingMode=PickingMode.Ignore};title.AddToClassList("remembered-title");rememberedCanvas.Add(title);
            var subtitle=new Label("Entre na cidade e viva do seu jeito."){name="remembered-subtitle",pickingMode=PickingMode.Ignore};subtitle.AddToClassList("remembered-subtitle");rememberedCanvas.Add(subtitle);
            rememberedMessage=new Label{enableRichText=false,name="remembered-message"};rememberedMessage.AddToClassList("remembered-message");rememberedCanvas.Add(rememberedMessage);
            rememberedPlay=new Button(()=>{if(busy)return;if(HasCharacter)EnterCity();else StartCoroutine(RestoreRemembered());}){text="JOGAR",name="remembered-play"};rememberedPlay.AddToClassList("remembered-play");rememberedCanvas.Add(rememberedPlay);
            rememberedPlay.style.unityFont=Resources.Load<Font>("HudMinimal/Barlow-SemiBold");
            rememberedSwitch=new Button(()=>{if(busy)return;if(Authenticated)Logout();else {SessionVault.Clear();HideRemembered();ShowLogin();}}){text="Trocar conta",tooltip="Voltar ao login",name="remembered-switch"};rememberedSwitch.AddToClassList("remembered-switch");rememberedCanvas.Add(rememberedSwitch);
            root.RegisterCallback<GeometryChangedEvent>(_=>LayoutRemembered());root.schedule.Execute(LayoutRemembered);
        }
        HideCharacterLobby();landing.style.display=DisplayStyle.None;scroll.style.display=DisplayStyle.None;
        AuthVisible=true;ReadyToEnterCity=false;root.style.display=DisplayStyle.Flex;remembered.style.display=DisplayStyle.Flex;
        rememberedMessage.text=text;rememberedMessage.style.display=string.IsNullOrEmpty(text)?DisplayStyle.None:DisplayStyle.Flex;
        rememberedPlay.SetEnabled(!busy);rememberedSwitch.SetEnabled(!busy);LayoutRemembered();
    }
    void LayoutRemembered()
    {
        if(rememberedCanvas==null)return;
        float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
        if(float.IsNaN(width)||float.IsNaN(height)||width<=0||height<=0)return;
        float artWidth=rememberedArtwork?rememberedArtwork.width:1672,artHeight=rememberedArtwork?rememberedArtwork.height:941;
        float scale=Mathf.Max(width/artWidth,height/artHeight);
        rememberedCanvas.style.width=artWidth*scale;rememberedCanvas.style.height=artHeight*scale;
        rememberedCanvas.style.left=(width-artWidth*scale)*.5f;rememberedCanvas.style.top=(height-artHeight*scale)*.5f;
    }
    internal IEnumerator RestoreRemembered()
    {
        string stored=SessionVault.Read();
        if(string.IsNullOrEmpty(stored)||!Regex.IsMatch(stored,"^[A-Za-z0-9_-]{43}$"))
        {SessionVault.Clear();ShowLogin();yield break;}
        busy=true;token=stored;ShowRemembered("Verificando seu acesso...");
        ApiReply reply=null;yield return Request("GET","/v1/player",null,true,r=>reply=r,false);
        // Some mobile networks briefly replay an unauthorized response while the
        // connection is being restored. Confirm it once before discarding access.
        if(reply==null&&LastErrorCode=="UNAUTHORIZED")
        {
            yield return new WaitForSecondsRealtime(.4f);
            token=stored;
            yield return Request("GET","/v1/player",null,true,r=>reply=r,false);
        }
        busy=false;
        if(reply!=null&&(reply.needsCharacter||!string.IsNullOrEmpty(reply.state?.characterId)))
        {
            State=reply.state;pending=null;leaving=false;RememberAccountUid(reply);
            if(HasCharacter)ShowRemembered();else {HideRemembered();ShowAccountCharacter();}
        }
        else if(IsAccessRevokedCode(LastErrorCode))
        {
            SessionVault.Clear();token=null;State=null;HideRemembered();ShowLogin(LastError);
        }
        else if(Authenticated)
        {
            // A network outage must not discard a valid saved session or allow offline entry.
            token=null;State=null;ShowRemembered("Não foi possível verificar o acesso. Confira a conexão e toque em Jogar para tentar novamente.");
        }
        else
        {
            SessionVault.Clear();HideRemembered();ShowLogin(string.IsNullOrEmpty(LastError)?"Entre novamente na sua conta.":LastError);
        }
    }
}
