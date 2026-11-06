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
            remembered=new VisualElement{name="remembered-access"};
            remembered.AddToClassList("entry-screen");
            remembered.AddToClassList("entry-session-check");root.Add(remembered);
            remembered.Add(HorizonteEntryVisuals.Image(HorizonteEntryVisuals.Logo,"entry-small-logo"));
            LabelAt("ACESSO À CIDADE","entry-kicker",remembered);
            rememberedMessage=LabelAt("","entry-note-detail",remembered);
            rememberedMessage.enableRichText=false;rememberedMessage.name="remembered-message";
            rememberedPlay=EntryButton("remembered-play","JOGAR",()=>{if(busy)return;if(HasCharacter)EnterCity();else StartCoroutine(RestoreRemembered());},remembered);
            rememberedSwitch=new Button(()=>{if(busy)return;if(Authenticated)Logout();else {SessionVault.Clear();ShowLogin();}}){text="TROCAR CONTA",name="remembered-switch"};
            rememberedSwitch.AddToClassList("entry-secondary");remembered.Add(rememberedSwitch);
        }
        HideCharacterLobby();landing.style.display=DisplayStyle.None;scroll.style.display=DisplayStyle.None;
        AuthVisible=true;ReadyToEnterCity=false;root.style.display=DisplayStyle.Flex;remembered.style.display=DisplayStyle.Flex;
        rememberedMessage.text=text;rememberedMessage.style.display=string.IsNullOrEmpty(text)?DisplayStyle.None:DisplayStyle.Flex;
        rememberedPlay.text=VerificationTransport!=null||HasCharacter?"JOGAR":"TENTAR NOVAMENTE";
        rememberedPlay.SetEnabled(!busy);rememberedSwitch.SetEnabled(!busy);
        rememberedPlay.style.display=rememberedSwitch.style.display=busy?DisplayStyle.None:DisplayStyle.Flex;
        remembered.BringToFront();
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
            if(VerificationTransport!=null&&HasCharacter)ShowRemembered();
            else ShowCitySelection();
        }
        else if(IsAccessRevokedCode(LastErrorCode))
        {
            SessionVault.Clear();token=null;State=null;HideRemembered();ShowLogin(LastError);
        }
        else if(Authenticated)
        {
            // A network outage must not discard a valid saved session or allow offline entry.
            token=null;State=null;ShowRemembered("Não foi possível verificar o acesso. Confira a conexão e tente novamente.");
        }
        else
        {
            SessionVault.Clear();HideRemembered();ShowLogin(string.IsNullOrEmpty(LastError)?"Entre novamente na sua conta.":LastError);
        }
    }
}
