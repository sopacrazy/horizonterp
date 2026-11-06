using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    UIDocument document;
    VisualElement root, landing, form, confirmGroup;
    ScrollView scroll;
    TextField email, password, confirmation;
    Label title, description, message, passwordHint;
    Button submit, toggle, back, showPassword;
    readonly Dictionary<TextField, Label> fieldErrors = new Dictionary<TextField, Label>();

    void Build(Texture2D artwork)
    {
        if(document)return;
        document=gameObject.AddComponent<UIDocument>();
        document.panelSettings=Resources.Load<PanelSettings>("HudPanel");document.sortingOrder=200;
        root=document.rootVisualElement;root.AddToClassList("auth-root");
        root.focusable=true;root.tabIndex=-1;
        root.styleSheets.Add(Resources.Load<StyleSheet>("AuthStyles"));
        var image=new Image { name="auth-artwork",image=HorizonteEntryVisuals.City?HorizonteEntryVisuals.City:artwork,scaleMode=ScaleMode.ScaleAndCrop,pickingMode=PickingMode.Ignore };
        image.AddToClassList("auth-art");root.Add(image);
        var shade=new VisualElement {pickingMode=PickingMode.Ignore};shade.AddToClassList("auth-shade");root.Add(shade);

        landing=new VisualElement {name="auth-landing"};landing.AddToClassList("auth-landing");root.Add(landing);
        landing.Add(HorizonteEntryVisuals.Image(HorizonteEntryVisuals.Logo,"entry-small-logo"));
        LabelAt("BETA FECHADO · EQUIPE","auth-kicker",landing);
        LabelAt("Entre na cidade","auth-welcome",landing);
        LabelAt("Acesse com seu usuário ou UID e senha enviados pela equipe.","auth-intro",landing);
        var login=new Button(()=>OpenEmailForm(false)) {name="auth-login-uid",text="ENTRAR"};
        login.AddToClassList("auth-email-button");landing.Add(login);
        LabelAt("Acesso individual emitido pelo painel. O cadastro público está fechado.","auth-description",landing);

        scroll=new ScrollView(ScrollViewMode.Vertical) {name="auth-form-panel"};scroll.AddToClassList("auth-scroll");
        scroll.horizontalScrollerVisibility=ScrollerVisibility.Hidden;scroll.verticalScrollerVisibility=ScrollerVisibility.Auto;
        root.Add(scroll);
        form=new VisualElement();form.AddToClassList("auth-form");scroll.Add(form);
        back=new Button(ShowLanding) {name="auth-back",text="‹  VOLTAR"};back.AddToClassList("auth-back");form.Add(back);
        LabelAt("ACESSO BETA · EQUIPE","auth-brand",form);
        title=LabelAt("","auth-title",form);
        description=LabelAt("","auth-description",form);

        var emailGroup=FieldGroup(form);
        // The internal field name is retained for old verification assemblies.
        // No e-mail field or public registration control is presented to players.
        email=Field("USUÁRIO OU UID","Seu usuário ou UID","auth-uid",false,emailGroup,32);
        email.keyboardType=TouchScreenKeyboardType.Default;
        string rememberedUid=Hbrp.Realtime.LocalNetworkLab.IsolateSavedAccess?"":PlayerPrefs.GetString("account.uid","");
        email.value=IsBetaUid(rememberedUid)?rememberedUid:"";
        var passwordGroup=FieldGroup(form);
        password=Field("Senha","Sua senha","auth-password",true,passwordGroup,128);
        var passwordRow=password.parent;passwordRow.AddToClassList("auth-password-row");
        showPassword=new Button(TogglePassword) {name="auth-show-password",text="MOSTRAR",tooltip="Mostrar senha"};
        showPassword.AddToClassList("auth-show-password");passwordRow.Add(showPassword);
        passwordHint=LabelAt("Use a senha individual fornecida pela equipe.","auth-field-hint",passwordGroup);

        message=LabelAt("","auth-message",form);message.name="auth-message";
        submit=new Button(()=>StartCoroutine(Submit())) {name="auth-submit"};submit.AddToClassList("auth-submit");form.Add(submit);
        LabelAt("Seu acesso já está criado. Após entrar, escolha ou crie seu personagem.","auth-footer",form);
        var language=LabelAt("PORTUGUÊS  ·  BRASIL","auth-language",root);language.pickingMode=PickingMode.Ignore;
        BuildCharacterLobby(root);
        var entryStyle=Resources.Load<StyleSheet>("Startup/EntryFlow");
        if(entryStyle)root.styleSheets.Add(entryStyle);
        root.RegisterCallback<GeometryChangedEvent>(_=>
        {
            root.EnableInClassList("auth-compact",root.resolvedStyle.width<1100);
            root.EnableInClassList("auth-short",root.resolvedStyle.height<680);
        });
        root.RegisterCallback<KeyDownEvent>(evt=>
        {
            if(!AuthVisible||busy)return;
            if(evt.keyCode==KeyCode.Escape&&scroll.resolvedStyle.display!=DisplayStyle.None){ShowLanding();evt.StopPropagation();}
            else if((evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter)&&scroll.resolvedStyle.display!=DisplayStyle.None)
            {
                var focused=root.focusController?.focusedElement as VisualElement;
                if(focused is TextField||focused?.GetFirstAncestorOfType<TextField>()!=null)
                {StartCoroutine(Submit());evt.StopPropagation();}
            }
        },TrickleDown.TrickleDown);
        root.style.display=DisplayStyle.None;ShowLanding();
    }

    static Label LabelAt(string text,string css,VisualElement parent)
    {
        var label=new Label(text) {pickingMode=PickingMode.Ignore};label.AddToClassList(css);parent.Add(label);return label;
    }

    void ProviderButton(string provider,string text,VisualElement parent)
    {
        // Reserved provider choices. They deliberately have no authentication callback yet.
        var button=new Button {name="auth-"+provider,tooltip="Disponível em breve"};
        button.AddToClassList("auth-provider");button.AddToClassList("auth-"+provider);
        button.Add(new AuthProviderIcon(provider));LabelAt(text,"auth-provider-text",button);parent.Add(button);
    }

    static VisualElement FieldGroup(VisualElement parent)
    {
        var group=new VisualElement();group.AddToClassList("auth-field-group");parent.Add(group);return group;
    }

    TextField Field(string caption,string placeholder,string id,bool secret,VisualElement parent,int maxLength)
    {
        LabelAt(caption,"auth-field-label",parent);
        var row=new VisualElement();row.AddToClassList("auth-input-row");parent.Add(row);
        var field=new TextField {name=id,isPasswordField=secret,maxLength=maxLength};field.AddToClassList("auth-input");
        field.textEdition.placeholder=placeholder;field.textEdition.hidePlaceholderOnFocus=false;row.Add(field);
        var error=LabelAt("","auth-field-error",parent);error.name=id+"-error";error.style.display=DisplayStyle.None;
        fieldErrors.Add(field,error);
        field.RegisterValueChangedCallback(_=>SetFieldError(field,""));
        field.RegisterCallback<FocusInEvent>(_=>scroll.schedule.Execute(()=>scroll.ScrollTo(field)).ExecuteLater(150));
        return field;
    }

    void OpenEmailForm(bool createAccount)
    {
        if(busy)return;
        DisplayEmailForm(createAccount);
    }

    void DisplayEmailForm(bool createAccount)
    {
        // Kept as a compatibility entry point for editor diagnostics. A request
        // to show the former register form can only open the UID login now.
        register=false;
        HideCharacterLobby();
        landing.style.display=DisplayStyle.None;scroll.style.display=DisplayStyle.Flex;
        ClearSecrets();ClearErrors();RefreshForm();scroll.scrollOffset=Vector2.zero;
        root.schedule.Execute(()=>email.Focus());
    }

    void ShowLanding()
    {
        if(busy)return;
        HideCharacterLobby();ClearSecrets();ClearErrors();landing.style.display=DisplayStyle.Flex;scroll.style.display=DisplayStyle.None;root.Focus();
    }

    void RefreshForm()
    {
        register=false;
        title.text="Acesse sua conta";
        description.text="Informe seu usuário ou UID e a senha da equipe.";
        passwordHint.style.display=DisplayStyle.Flex;
        password.textEdition.placeholder="Senha do acesso beta";
        submit.text="ENTRAR NO BETA";
    }

    void ShowLogin(string error="")
    {
        HideEntryScreen();
        Debug.Log("ACCOUNT_LOGIN_SHOWN authenticated="+Authenticated+" code="+LastErrorCode);
        HideRemembered();
        ReadyToEnterCity=false;HideCharacterLobby();AuthVisible=true;root.style.display=DisplayStyle.Flex;
        if(string.IsNullOrEmpty(error))ShowLanding();
        else {DisplayEmailForm(false);SetGeneralMessage(error,true);}
    }

    void ClearSecrets()
    {
        if(password==null)return;
        password.value="";password.isPasswordField=true;
        if(confirmation!=null){confirmation.value="";confirmation.isPasswordField=true;}
        showPassword.text="MOSTRAR";showPassword.tooltip="Mostrar senha";
    }

    void TogglePassword()
    {
        bool secret=!password.isPasswordField;password.isPasswordField=secret;
        if(confirmation!=null)confirmation.isPasswordField=secret;
        showPassword.text=secret?"MOSTRAR":"OCULTAR";showPassword.tooltip=secret?"Mostrar senha":"Ocultar senha";
    }

    void SetGeneralMessage(string value,bool error)
    {
        message.text=value??"";message.style.display=string.IsNullOrEmpty(value)?DisplayStyle.None:DisplayStyle.Flex;
        message.EnableInClassList("error",error);
    }

    void SetFieldError(TextField field,string error)
    {
        field.EnableInClassList("invalid",!string.IsNullOrEmpty(error));
        var label=fieldErrors[field];label.text=error;label.style.display=string.IsNullOrEmpty(error)?DisplayStyle.None:DisplayStyle.Flex;
    }

    void ClearErrors()
    {
        foreach(var field in fieldErrors.Keys)SetFieldError(field,"");
        if(message!=null)SetGeneralMessage("",false);
    }

    bool ValidateForm()
    {
        ClearErrors();TextField firstError=null;
        void Reject(TextField field,string error){SetFieldError(field,error);if(firstError==null)firstError=field;}
        string uid=(email.value??"").Trim();
        if(!IsBetaUid(uid))Reject(email,"Informe seu usuário ou UID recebido da equipe.");
        else email.SetValueWithoutNotify(uid);
        if(string.IsNullOrWhiteSpace(password.value)||password.value.Length<6||password.value.Length>128)
            Reject(password,"Sua senha deve ter de 6 a 128 caracteres.");
        if(firstError==null)return true;
        SetGeneralMessage("Confira os campos destacados.",true);firstError.Focus();return false;
    }

    void SetFormBusy(bool value)
    {
        foreach(var field in fieldErrors.Keys)field.SetEnabled(!value);
        submit.SetEnabled(!value);back.SetEnabled(!value);showPassword.SetEnabled(!value);
        if(value)submit.text="VERIFICANDO ACESSO...";else RefreshForm();
    }
    internal static bool IsBetaUid(string value)=>Regex.IsMatch(value??"",@"\A(?:[1-9][0-9]{0,18}|[A-Za-z][A-Za-z0-9_]{2,31})\z",RegexOptions.CultureInvariant);

    void RememberAccountUid(ApiReply reply,string fallback=null)
    {
        string uid=IsBetaUid(fallback)?fallback:(reply?.account?.uid??reply?.state?.uid);
        if(!IsBetaUid(uid))return;
        email?.SetValueWithoutNotify(uid);
        if(!Hbrp.Realtime.LocalNetworkLab.IsolateSavedAccess&&VerificationTransport==null)
        {PlayerPrefs.SetString("account.uid",uid);PlayerPrefs.Save();}
    }
}

sealed class AuthProviderIcon : VisualElement
{
    readonly string provider;
    public AuthProviderIcon(string kind)
    {
        provider=kind;AddToClassList("auth-provider-icon");pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;
    }
    void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D;float size=Mathf.Min(contentRect.width,contentRect.height);
        Vector2 Point(float x,float y)=>new Vector2((contentRect.width-size)/2+x*size/24,(contentRect.height-size)/2+y*size/24);
        void Arc(float from,float to,Color color)
        {
            p.BeginPath();p.lineWidth=size*4/24;p.strokeColor=color;
            for(int i=0;i<=30;i++){float a=Mathf.Lerp(from,to,i/30f)*Mathf.Deg2Rad;var v=Point(12+Mathf.Cos(a)*8,12+Mathf.Sin(a)*8);if(i==0)p.MoveTo(v);else p.LineTo(v);}p.Stroke();
        }
        if(provider=="google")
        {
            Arc(42,140,new Color(.20f,.66f,.33f));Arc(138,202,new Color(.98f,.74f,.02f));
            Arc(200,316,new Color(.92f,.26f,.21f));Arc(-2,45,new Color(.26f,.52f,.96f));
            p.BeginPath();p.strokeColor=new Color(.26f,.52f,.96f);p.MoveTo(Point(12,12));p.LineTo(Point(20,12));p.LineTo(Point(20,14));p.Stroke();
        }
        else
        {
            p.BeginPath();p.fillColor=Color.white;
            for(int i=0;i<=40;i++){float a=i*Mathf.PI/20;var v=Point(12+Mathf.Cos(a)*11,12+Mathf.Sin(a)*11);if(i==0)p.MoveTo(v);else p.LineTo(v);}p.ClosePath();p.Fill();
            p.BeginPath();p.fillColor=new Color(.08f,.46f,.95f);
            float[] points={13,23,13,13,16.5f,13,17,9.5f,13,9.5f,13,7.2f,14,6.3f,17,6.3f,17,3,13.5f,3,10.5f,3.5f,9,6,9,9.5f,6.8f,9.5f,6.8f,13,9,13,9,23};
            p.MoveTo(Point(points[0],points[1]));for(int i=2;i<points.Length;i+=2)p.LineTo(Point(points[i],points[i+1]));p.ClosePath();p.Fill();
        }
    }
}
