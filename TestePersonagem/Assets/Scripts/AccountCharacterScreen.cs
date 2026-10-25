using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    VisualElement characterLobby, characterCreation, characterExisting, characterDock;
    ScrollView characterScroll;
    TextField characterFirstName, characterLastName;
    Label characterFirstError, characterLastError, characterGenderError, characterMessage;
    Label characterFemaleStatus, characterMaleStatus, characterSelection, characterExistingName;
    Image characterExistingImage;
    Button characterFemale, characterMale, characterSave, characterEnter, characterLogout;
    Texture2D characterFemaleArtwork, characterMaleArtwork;
    VisualElement characterStage;
    Image characterFemalePreview, characterMalePreview;
    Label characterPreviewName;
    Coroutine characterSlide;
    string characterPreviewGender = "male";
    string characterGender = "";
    bool characterBusy, characterLobbyShown, characterValidationMessage;

    public string CharacterFirstName => NormalizeCharacterName(characterFirstName?.value);
    public string CharacterLastName => NormalizeCharacterName(characterLastName?.value);
    public string CharacterGender => characterGender;

    void BuildCharacterLobby(VisualElement parent)
    {
        if(characterLobby!=null)return;
        var styles=Resources.Load<StyleSheet>("CharacterStyles");
        if(styles)parent.styleSheets.Add(styles);
        var garageStyles=Resources.Load<StyleSheet>("CharacterGarage");
        if(garageStyles)parent.styleSheets.Add(garageStyles);
        characterFemaleArtwork=Resources.Load<Texture2D>("Characters/Feminina");
        characterMaleArtwork=Resources.Load<Texture2D>("Characters/Masculino");
        characterLobby=new VisualElement {name="character-lobby"};
        characterLobby.AddToClassList("character-lobby");parent.Add(characterLobby);
        var garage=new Image {name="character-garage",image=Resources.Load<Texture2D>("Characters/garagem"),
            scaleMode=ScaleMode.ScaleAndCrop,pickingMode=PickingMode.Ignore};
        garage.AddToClassList("character-garage");characterLobby.Add(garage);
        var shade=new VisualElement {pickingMode=PickingMode.Ignore};shade.AddToClassList("character-garage-shade");characterLobby.Add(shade);
        characterScroll=new ScrollView(ScrollViewMode.Vertical) {name="character-scroll"};
        characterScroll.AddToClassList("character-scroll");
        characterScroll.horizontalScrollerVisibility=ScrollerVisibility.Hidden;
        characterScroll.verticalScrollerVisibility=ScrollerVisibility.Auto;
        characterLobby.Add(characterScroll);
        var shell=new VisualElement();shell.AddToClassList("character-shell");characterScroll.Add(shell);
        var header=new VisualElement();header.AddToClassList("character-header");shell.Add(header);
        var brand=new VisualElement();brand.AddToClassList("character-brand-block");header.Add(brand);
        CharacterLabel("HORIZONTE","character-brand",brand);
        CharacterLabel("B R A S I L   R P","character-brand-sub",brand);
        CharacterLabel("N O V O   P E R S O N A G E M","character-header-title",header);
        characterLogout=new Button(()=>{if(!characterBusy&&!busy)Logout();}) {name="character-logout",text="TROCAR CONTA"};
        characterLogout.AddToClassList("character-logout");header.Add(characterLogout);

        characterCreation=new VisualElement {name="character-create"};shell.Add(characterCreation);
        characterCreation.AddToClassList("character-creation");
        var body=new VisualElement();body.AddToClassList("character-body");characterCreation.Add(body);
        characterStage=new VisualElement {name="character-stage",pickingMode=PickingMode.Ignore};
        characterStage.AddToClassList("character-stage");body.Add(characterStage);
        characterFemalePreview=new Image {name="character-female-preview",image=characterFemaleArtwork,
            scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
        characterMalePreview=new Image {name="character-male-preview",image=characterMaleArtwork,
            scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
        characterFemalePreview.AddToClassList("character-stage-image");characterMalePreview.AddToClassList("character-stage-image");
        characterStage.Add(characterFemalePreview);characterStage.Add(characterMalePreview);
        characterPreviewName=CharacterLabel("SEU PERSONAGEM","character-preview-name",characterStage);
        characterPreviewName.name="character-preview-name";

        var identity=new VisualElement();identity.AddToClassList("character-identity");body.Add(identity);
        CharacterLabel("IDENTIDADE","character-section",identity);
        var fields=new VisualElement();fields.AddToClassList("character-name-fields");identity.Add(fields);
        characterFirstName=CharacterField("NOME","Seu primeiro nome","character-first-name",fields,out characterFirstError);
        characterLastName=CharacterField("SOBRENOME","Seu sobrenome","character-last-name",fields,out characterLastError);
        characterLastName.parent.AddToClassList("character-field-last");
        var choices=new VisualElement();choices.AddToClassList("character-choices");identity.Add(choices);
        CharacterLabel("PERSONAGEM","character-section",choices);
        var cards=new VisualElement {name="character-cards"};cards.AddToClassList("character-cards");choices.Add(cards);
        characterMale=CharacterCard("male","MASCULINO",characterMaleArtwork,cards,out characterMaleStatus);
        characterFemale=CharacterCard("female","FEMININO",characterFemaleArtwork,cards,out characterFemaleStatus);
        characterFemale.AddToClassList("character-card-last");
        characterSelection=CharacterLabel("Selecione uma das opções para continuar.","character-selection",choices);
        characterSelection.name="character-selection";
        characterGenderError=CharacterLabel("","character-field-error",choices);
        characterGenderError.name="character-gender-error";characterGenderError.style.display=DisplayStyle.None;
        characterDock=new VisualElement {name="character-dock"};characterDock.AddToClassList("character-dock");identity.Add(characterDock);
        characterSave=new Button(()=>{if(!characterBusy&&!busy)StartCoroutine(SubmitCharacter());})
            {name="character-save",text="CRIAR PERSONAGEM   ›"};
        characterSave.AddToClassList("character-primary");characterDock.Add(characterSave);
        characterMessage=CharacterLabel("","character-message",characterDock);characterMessage.name="character-message";

        characterExisting=new VisualElement {name="character-existing"};
        characterExisting.AddToClassList("character-existing");shell.Add(characterExisting);
        CharacterLabel("SEU PERSONAGEM","character-kicker",characterExisting);
        CharacterLabel("Sua história continua","character-title",characterExisting);
        CharacterLabel("Tudo pronto para voltar à cidade.","character-description",characterExisting);
        var existingCard=new VisualElement();existingCard.AddToClassList("character-existing-card");characterExisting.Add(existingCard);
        var portrait=new VisualElement();portrait.AddToClassList("character-existing-portrait");existingCard.Add(portrait);
        characterExistingImage=new Image {name="character-existing-image",scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
        characterExistingImage.AddToClassList("character-existing-image");portrait.Add(characterExistingImage);
        CharacterLabel("✓  SELECIONADO","character-existing-selected",portrait);
        var summary=new VisualElement();summary.AddToClassList("character-existing-summary");existingCard.Add(summary);
        CharacterLabel("PERSONAGEM SALVO","character-section",summary);
        characterExistingName=CharacterLabel("","character-existing-name",summary);characterExistingName.name="character-existing-name";
        CharacterLabel("Seu personagem e seu progresso estão prontos. Entre para continuar sua jornada.","character-existing-description",summary);
        characterEnter=new Button(()=>{if(!characterBusy&&!busy)EnterCity();}) {name="character-enter",text="ENTRAR NA CIDADE"};
        characterEnter.AddToClassList("character-primary");summary.Add(characterEnter);

        characterLobby.RegisterCallback<GeometryChangedEvent>(_=>
        {
            characterLobby.EnableInClassList("character-narrow",characterLobby.resolvedStyle.width<700);
            characterLobby.EnableInClassList("character-phone",characterLobby.resolvedStyle.width<520);
            characterLobby.EnableInClassList("character-short",characterLobby.resolvedStyle.height<650);
            shell.style.minHeight=characterLobby.resolvedStyle.height;
        });
        characterLobby.RegisterCallback<KeyDownEvent>(evt=>
        {
            if(!characterLobbyShown||characterBusy||busy||characterCreation.resolvedStyle.display==DisplayStyle.None)return;
            if(evt.keyCode!=KeyCode.Return&&evt.keyCode!=KeyCode.KeypadEnter)return;
            var focused=parent.focusController?.focusedElement as VisualElement;
            if(focused is TextField||focused?.GetFirstAncestorOfType<TextField>()!=null)
            {StartCoroutine(SubmitCharacter());evt.StopPropagation();}
        },TrickleDown.TrickleDown);
        HideCharacterLobby();
    }

    static Label CharacterLabel(string text,string css,VisualElement parent)
    {
        var label=new Label(text) {pickingMode=PickingMode.Ignore};label.AddToClassList(css);parent.Add(label);return label;
    }

    TextField CharacterField(string caption,string placeholder,string id,VisualElement parent,out Label error)
    {
        var group=new VisualElement();group.AddToClassList("character-field-group");parent.Add(group);
        CharacterLabel(caption,"character-field-label",group);
        // Two UTF-16 code units may represent one Unicode letter; validation counts Unicode characters.
        var field=new TextField {name=id,maxLength=48};field.AddToClassList("character-input");
        field.textEdition.placeholder=placeholder;field.textEdition.hidePlaceholderOnFocus=false;group.Add(field);
        var errorLabel=CharacterLabel("","character-field-error",group);errorLabel.name=id+"-error";
        errorLabel.style.display=DisplayStyle.None;error=errorLabel;
        field.RegisterValueChangedCallback(_=>
        {SetCharacterFieldError(field,errorLabel,"");ClearCharacterValidationMessage();RefreshPreviewName();});
        field.RegisterCallback<FocusInEvent>(_=>characterScroll.schedule.Execute(()=>
        {if(characterLobbyShown&&characterCreation.resolvedStyle.display!=DisplayStyle.None)characterScroll.ScrollTo(field);}).ExecuteLater(150));
        return field;
    }

    Button CharacterCard(string gender,string caption,Texture2D artwork,VisualElement parent,out Label status)
    {
        string id=gender=="female"?"character-female":"character-male";
        var card=new Button(()=>SelectCharacter(gender)) {name=id,tooltip="Selecionar personagem "+caption.ToLowerInvariant()};
        card.AddToClassList("character-card");parent.Add(card);
        CharacterLabel(gender=="female"?"♀":"♂","character-gender-icon",card);
        CharacterLabel(caption,"character-card-name",card);
        CharacterLabel("✓","character-card-check",card);
        status=CharacterLabel("","character-card-status",card);status.name=id+"-status";
        return card;
    }

    void ShowCharacterLobby()
    {
        if(characterLobby==null)return;
        characterLobbyShown=true;characterLobby.style.display=DisplayStyle.Flex;
        characterCreation.style.display=DisplayStyle.Flex;characterExisting.style.display=DisplayStyle.None;
        characterLobby.RemoveFromClassList("character-existing-mode");
        characterLobby.AddToClassList("character-create-mode");characterDock.style.display=DisplayStyle.Flex;
        characterFirstName.SetValueWithoutNotify("");characterLastName.SetValueWithoutNotify("");characterGender="";
        ClearCharacterErrors();SetCharacterBusy(false);RefreshCharacterSelection();ResetCharacterPreview();RefreshPreviewName();characterScroll.scrollOffset=Vector2.zero;
        characterLobby.schedule.Execute(()=>
        {if(characterLobbyShown&&characterCreation.resolvedStyle.display!=DisplayStyle.None)characterFirstName.Focus();});
    }

    void ShowExistingCharacter(ApiPlayerState state)
    {
        if(characterLobby==null)return;
        characterLobbyShown=true;characterLobby.style.display=DisplayStyle.Flex;
        characterCreation.style.display=DisplayStyle.None;characterExisting.style.display=DisplayStyle.Flex;
        characterLobby.AddToClassList("character-existing-mode");
        characterLobby.RemoveFromClassList("character-create-mode");characterDock.style.display=DisplayStyle.None;
        characterExistingImage.image=state?.gender=="female"?characterFemaleArtwork:characterMaleArtwork;
        string fullName=PlayerDisplayName.From(state);
        characterExistingName.text=!string.IsNullOrWhiteSpace(fullName)?fullName:state?.name??"Seu personagem";
        ClearCharacterErrors();SetCharacterBusy(false);characterScroll.scrollOffset=Vector2.zero;
        characterLobby.schedule.Execute(()=>{if(characterLobbyShown&&characterExisting.resolvedStyle.display!=DisplayStyle.None)characterEnter.Focus();});
    }

    void HideCharacterLobby()
    {
        characterLobbyShown=false;
        if(characterLobby==null)return;
        var focused=root.focusController?.focusedElement as VisualElement;
        if(focused!=null&&characterLobby.Contains(focused))root.Focus();
        if(characterSlide!=null){StopCoroutine(characterSlide);characterSlide=null;}
        characterDock.style.display=DisplayStyle.None;
        characterLobby.style.display=DisplayStyle.None;
    }

    void SelectCharacter(string gender)
    {
        if(characterBusy||busy||characterGender==gender)return;
        if(gender!="female"&&gender!="male")return;
        AnimateCharacterPreview(gender);
        characterGender=gender;characterGenderError.text="";characterGenderError.style.display=DisplayStyle.None;
        ClearCharacterValidationMessage();RefreshCharacterSelection();
    }

    void RefreshCharacterSelection()
    {
        bool female=characterGender=="female",male=characterGender=="male";
        characterFemale.EnableInClassList("selected",female);characterMale.EnableInClassList("selected",male);
        characterFemaleStatus.text=female?"✓  SELECIONADO":"";
        characterMaleStatus.text=male?"✓  SELECIONADO":"";
        characterSelection.text=female?"Personagem feminino selecionado.":male?"Personagem masculino selecionado.":"Selecione uma das opções para continuar.";
        characterSelection.EnableInClassList("selected",female||male);
    }

    void RefreshPreviewName()
    {
        if(characterPreviewName==null)return;
        string fullName=CharacterFirstName;
        characterPreviewName.text=string.IsNullOrEmpty(fullName)?"SEU PERSONAGEM":fullName.ToUpperInvariant();
    }

    Image PreviewFor(string gender)=>gender=="female"?characterFemalePreview:characterMalePreview;

    void ResetCharacterPreview()
    {
        if(characterSlide!=null){StopCoroutine(characterSlide);characterSlide=null;}
        characterPreviewGender="male";
        characterMalePreview.style.translate=new Translate(0,0);characterMalePreview.style.opacity=1;
        characterFemalePreview.style.translate=new Translate(0,0);characterFemalePreview.style.opacity=0;
    }

    void AnimateCharacterPreview(string gender)
    {
        // Restart from the current visual positions, including when the player taps rapidly.
        if(characterPreviewGender==gender)return;
        if(characterSlide!=null)StopCoroutine(characterSlide);
        var outgoing=PreviewFor(characterPreviewGender);
        var incoming=PreviewFor(gender);
        float distance=Mathf.Max(300,characterStage.resolvedStyle.width);
        float oldX=outgoing.resolvedStyle.translate.x,oldOpacity=outgoing.resolvedStyle.opacity;
        float newOpacity=incoming.resolvedStyle.opacity;
        float newX=newOpacity<.01f?distance:incoming.resolvedStyle.translate.x;
        characterPreviewGender=gender;
        characterSlide=StartCoroutine(SlideCharacter(outgoing,incoming,oldX,newX,oldOpacity,newOpacity,distance));
    }

    IEnumerator SlideCharacter(Image outgoing,Image incoming,float oldX,float newX,float oldOpacity,float newOpacity,float distance)
    {
        const float duration=.45f;
        float elapsed=0;
        while(elapsed<duration)
        {
            float t=Mathf.Clamp01(elapsed/duration),ease=1-Mathf.Pow(1-t,3);
            outgoing.style.translate=new Translate(Mathf.Lerp(oldX,-distance,ease),0);
            outgoing.style.opacity=Mathf.Lerp(oldOpacity,0,ease);
            incoming.style.translate=new Translate(Mathf.Lerp(newX,0,ease),0);
            incoming.style.opacity=Mathf.Lerp(newOpacity,1,ease);
            elapsed+=Time.unscaledDeltaTime;yield return null;
        }
        outgoing.style.opacity=0;outgoing.style.translate=new Translate(0,0);
        incoming.style.opacity=1;incoming.style.translate=new Translate(0,0);characterSlide=null;
    }

    void SetCharacterBusy(bool value)
    {
        characterBusy=value;
        characterFirstName.SetEnabled(!value);characterLastName.SetEnabled(!value);
        characterFemale.SetEnabled(!value);characterMale.SetEnabled(!value);
        characterSave.SetEnabled(!value);characterEnter.SetEnabled(!value);
        characterLogout.SetEnabled(!value);
        characterSave.text=value?"SALVANDO PERSONAGEM...":"CRIAR PERSONAGEM   ›";
        characterEnter.text=value?"PREPARANDO A CIDADE...":"ENTRAR NA CIDADE";
        characterLobby.EnableInClassList("character-busy",value);
    }

    void SetCharacterMessage(string value,bool error)
    {
        if(characterMessage==null)return;
        characterValidationMessage=false;
        characterMessage.text=value??"";characterMessage.style.display=string.IsNullOrEmpty(value)?DisplayStyle.None:DisplayStyle.Flex;
        characterMessage.EnableInClassList("error",error);
    }

    void ClearCharacterValidationMessage()
    {
        // Only local validation becomes stale when editing; keep network/server failures visible.
        if(characterValidationMessage&&!characterBusy&&!busy)SetCharacterMessage("",false);
    }

    static void SetCharacterFieldError(TextField field,Label label,string value)
    {
        field.EnableInClassList("invalid",!string.IsNullOrEmpty(value));label.text=value;
        label.style.display=string.IsNullOrEmpty(value)?DisplayStyle.None:DisplayStyle.Flex;
    }

    void ClearCharacterErrors()
    {
        SetCharacterFieldError(characterFirstName,characterFirstError,"");
        SetCharacterFieldError(characterLastName,characterLastError,"");
        characterGenderError.text="";characterGenderError.style.display=DisplayStyle.None;SetCharacterMessage("",false);
    }

    bool ValidateCharacterForm()
    {
        ClearCharacterErrors();TextField firstInvalid=null;
        void Validate(TextField field,Label label,string caption)
        {
            string value=NormalizeCharacterName(field.value);
            if(!IsValidCharacterName(value))
            {
                SetCharacterFieldError(field,label,string.IsNullOrEmpty(value)?"Informe seu "+caption+".":"Use de 2 a 24 caracteres, começando e terminando com uma letra.");
                if(firstInvalid==null)firstInvalid=field;
            }
            else field.SetValueWithoutNotify(value);
        }
        Validate(characterFirstName,characterFirstError,"nome");Validate(characterLastName,characterLastError,"sobrenome");
        bool genderValid=characterGender=="female"||characterGender=="male";
        if(!genderValid){characterGenderError.text="Escolha um personagem feminino ou masculino para continuar.";characterGenderError.style.display=DisplayStyle.Flex;}
        if(firstInvalid==null&&genderValid)return true;
        SetCharacterMessage("Confira os campos destacados e escolha seu personagem.",true);
        characterValidationMessage=true;
        if(firstInvalid!=null)firstInvalid.Focus();
        else {characterFemale.Focus();characterScroll.ScrollTo(characterFemale);}
        return false;
    }

    static string NormalizeCharacterName(string value)
    {
        try
        {
            // Match the API's NFC, whitespace and typographic-apostrophe normalization.
            value=(value??"").Normalize(NormalizationForm.FormC);
            return Regex.Replace(value,@"[\u0009-\u000D\u0020\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]+"," ").Trim(' ').Replace('’','\'');
        }
        catch(ArgumentException){return "";}
    }

    static bool IsValidCharacterName(string value)
    {
        int count=0;bool needsLetter=true;
        for(int index=0;index<value.Length;)
        {
            int width=char.IsSurrogatePair(value,index)?2:1;
            var category=CharUnicodeInfo.GetUnicodeCategory(value,index);
            bool letter=category==UnicodeCategory.UppercaseLetter||category==UnicodeCategory.LowercaseLetter||category==UnicodeCategory.TitlecaseLetter||category==UnicodeCategory.ModifierLetter||category==UnicodeCategory.OtherLetter;
            bool mark=category==UnicodeCategory.NonSpacingMark||category==UnicodeCategory.SpacingCombiningMark||category==UnicodeCategory.EnclosingMark;
            bool separator=width==1&&(value[index]==' '||value[index]=='\''||value[index]=='-');
            if(letter)needsLetter=false;
            else if(mark){if(needsLetter)return false;}
            else if(separator){if(needsLetter)return false;needsLetter=true;}
            else return false;
            if(++count>24)return false;
            index+=width;
        }
        return count>=2&&!needsLetter;
    }
}
