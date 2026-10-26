using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// Session-only prototype. Documents and donations are separate from the server economy.
public sealed class TutorialJourney : MonoBehaviour
{
    const string IntroNarration = "Salve, cria! Chegou no Horizonte? Então cola comigo que eu te passo a visão. " +
        "Pra andar, usa W, A, S e D — ou o controle na tela. Quer dar um gás? Segura Shift. " +
        "O menu tá naquele botão dos quatro quadradinhos, e o mapa te ajuda a se localizar.";
    const string CoinNarration = "Pra começar o corre, tu recebe trezentos HZ Coins de doação. " +
        "Pega essa ajuda e bora resolver teus documentos.";
    const string PrefeituraNarration = "Boa, cria! Os trezentos HZ Coins já tão contigo. " +
        "Agora cola na Prefeitura pra tirar teu RG. Segue o marcador azul e, quando chegar, aperta E ou toca em Tirar RG.";
    const string RgNarration = "Aí sim, cria! Teu RG de teste tá na mão e entrou mais cento e cinquenta reais de ajuda. " +
        "Agora segue o marcador da Autoescola pra tirar tua CNH.";
    const string CnhNarration = "Fechou o corre! Tua CNH de teste saiu e entrou mais cento e cinquenta reais de ajuda. " +
        "RG e CNH tão no Inventário. Agora pode explorar o Horizonte!";
    const float IntroArtAspect = 1672f / 941f;
    public static TutorialJourney Instance { get; private set; }
    public bool HasRG => stage >= 2;
    public bool HasCNH => stage >= 3;
    public long DemoCoins => stage >= 1 ? 300 : 0;
    public long DemoDonations => (stage >= 2 ? 150 : 0) + (stage >= 3 ? 150 : 0);

    ThirdPersonPlayer player;
    GameObject uiObject, prefeitura, autoescola;
    UIDocument document;
    VisualElement questCard, prompt, modal, modalPanel;
    VisualElement introArt;
    Image introImage;
    Label introText;
    Button introStart, introLater;
    AudioSource narration;
    Coroutine slideRoutine;
    AudioClip salveClip, coinClip;
    Texture2D salveArt, coinArt;
    Label questTitle, questDetail, promptLabel, modalTitle, modalBody;
    Button questButton, promptButton, modalButton, postponeButton;
    TextMesh prefeituraName, autoescolaName;
    int stage, modalAction;
    bool modalOpen, combatWasLocked, changingSlide;
    float nextInteraction;

    public static void Create(ThirdPersonPlayer target)
    {
        if (!target || !NeighborhoodMap.Active || !NeighborhoodMap.Active.isOfficialCity ||
            AccountSession.VerificationTransport != null || target.GetComponent<TutorialJourney>()) return;
        target.gameObject.AddComponent<TutorialJourney>();
    }

    void Awake()
    {
        Instance = this;
        player = GetComponent<ThirdPersonPlayer>();
        narration = gameObject.AddComponent<AudioSource>();
        narration.playOnAwake = false;
        narration.loop = false;
        narration.spatialBlend = 0;
        salveClip = Resources.Load<AudioClip>("TutorialSalve");
        coinClip = Resources.Load<AudioClip>("TutorialCoin");
        salveArt = Resources.Load<Texture2D>("TutorialSalve");
        coinArt = Resources.Load<Texture2D>("TutorialCoinBanner");
    }

    IEnumerator Start()
    {
        while (player && (StartupLoader.IsLoading || !AccountSession.HasCharacter || AccountSession.InputBlocked))
            yield return null;
        if (!player) yield break;
        stage = 0;
        CreateUI();
        RefreshUI();
        ShowModal("SALVE, CRIA!", IntroNarration, "BORA PRO CORRE", 0);
    }

    void Update()
    {
        if (!player || !document) return;
        if (stage > 0 && stage < 3 && !prefeitura && player.transform.position.y > -40f &&
            !StartupLoader.IsLoading && !AccountSession.InputBlocked) BuildMarkers();

        var target = stage == 1 ? prefeitura : stage == 2 ? autoescola : null;
        bool nearby = target && Vector3.Distance(player.transform.position, target.transform.position) < 2.1f;
        bool available = nearby && !modalOpen && !player.IsTeleporting && !player.IsJumping &&
            !player.CombatLocked && !InGameChat.Editing && !StartupLoader.IsLoading &&
            !AccountSession.InputBlocked && !(GameHud.Instance && GameHud.Instance.IsPanelOpen) &&
            Time.unscaledTime >= nextInteraction;
        prompt.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
        if (available && Input.GetKeyDown(KeyCode.E)) CompleteVisit();
        if (target && questDetail != null)
        {
            float distance = Vector3.Distance(player.transform.position, target.transform.position);
            questDetail.text = (stage == 1 ? "Cola na Prefeitura" : "Partiu Autoescola") +
                " · " + Mathf.CeilToInt(distance) + " m";
        }
        if (prefeituraName && Camera.main) prefeituraName.transform.rotation = Camera.main.transform.rotation;
        if (autoescolaName && Camera.main) autoescolaName.transform.rotation = Camera.main.transform.rotation;
        if (prefeitura) prefeitura.transform.Rotate(Vector3.up, 13f * Time.deltaTime);
        if (autoescola) autoescola.transform.Rotate(Vector3.up, 13f * Time.deltaTime);
    }

    void CompleteVisit()
    {
        if (stage != 1 && stage != 2 || modalOpen || Time.unscaledTime < nextInteraction) return;
        nextInteraction = Time.unscaledTime + 1f;
        if (stage == 1)
        {
            SetStage(2);
            ShowModal("RG NA MÃO · TESTE", RgNarration, "PARTIU AUTOESCOLA", 5);
        }
        else
        {
            SetStage(3);
            ShowModal("CNH GARANTIDA · TESTE", CnhNarration, "BORA JOGAR", 6);
        }
    }

    void SetStage(int value)
    {
        stage = value;
        RefreshUI();
    }

    void RefreshUI()
    {
        if (questCard == null) return;
        questCard.style.display = stage == 3 ? DisplayStyle.None : DisplayStyle.Flex;
        if (stage == 0)
        {
            questTitle.text = "COMEÇA O CORRE";
            questDetail.text = "Pega a visão e recebe a doação inicial";
            questButton.style.display = DisplayStyle.Flex;
        }
        else
        {
            questTitle.text = stage == 1 ? "1 / 2  ·  COLA NA PREFEITURA" : "2 / 2  ·  PARTIU AUTOESCOLA";
            questButton.style.display = DisplayStyle.None;
            questDetail.text = stage == 1 ? "Chega junto pra tirar o RG" : "Chega junto pra tirar a CNH";
        }
        promptLabel.text = stage == 1 ? "PREFEITURA · RG" : "AUTOESCOLA · CNH";
        promptButton.text = stage == 1 ? "E  TIRAR RG" : "E  TIRAR CNH";
        if (prefeitura) prefeitura.SetActive(stage == 1);
        if (autoescola) autoescola.SetActive(stage == 2);
        prompt.style.display = DisplayStyle.None;
    }

    void CreateUI()
    {
        uiObject = new GameObject("Mini tutorial Horizonte");
        uiObject.transform.SetParent(transform, false);
        document = uiObject.AddComponent<UIDocument>();
        document.panelSettings = Resources.Load<PanelSettings>("HudPanel");
        document.sortingOrder = 90;
        var root = document.rootVisualElement;

        questCard = new VisualElement { name = "tutorial-objective" };
        questCard.style.position = Position.Absolute;
        questCard.style.left = Length.Percent(50);
        questCard.style.marginLeft = -200;
        questCard.style.top = 112;
        questCard.style.width = 400;
        questCard.style.paddingLeft = questCard.style.paddingRight = 14;
        questCard.style.paddingTop = questCard.style.paddingBottom = 9;
        questCard.style.backgroundColor = new Color(.045f, .075f, .11f, .91f);
        Border(questCard, new Color(.25f, .74f, 1f), 9);
        questTitle = Label("", 13, new Color(.37f, .8f, 1f), true, questCard);
        questDetail = Label("", 14, Color.white, false, questCard);
        questButton = Button("PEGAR A VISÃO", () => ShowModal("SALVE, CRIA!",
            IntroNarration, "BORA PRO CORRE", 0), questCard);
        root.Add(questCard);

        prompt = new VisualElement { name = "tutorial-visit-prompt" };
        prompt.style.position = Position.Absolute;
        prompt.style.left = Length.Percent(50);
        prompt.style.marginLeft = -125;
        prompt.style.bottom = 145;
        prompt.style.width = 250;
        prompt.style.paddingLeft = prompt.style.paddingRight = 12;
        prompt.style.paddingTop = prompt.style.paddingBottom = 9;
        prompt.style.alignItems = Align.Center;
        prompt.style.backgroundColor = new Color(.045f, .075f, .11f, .95f);
        Border(prompt, new Color(.24f, .83f, .95f), 10);
        promptLabel = Label("", 12, new Color(.5f, .9f, 1f), true, prompt);
        promptButton = Button("", CompleteVisit, prompt);
        root.Add(prompt);

        modal = new VisualElement { name = "tutorial-dialog" };
        modal.style.position = Position.Absolute;
        modal.style.left = modal.style.top = modal.style.right = modal.style.bottom = 0;
        modal.style.backgroundColor = new Color(.015f, .025f, .04f, .78f);
        modal.style.justifyContent = Justify.Center;
        modal.style.alignItems = Align.Center;
        modalPanel = new VisualElement();
        modalPanel.style.width = 455;
        modalPanel.style.maxWidth = Length.Percent(90);
        modalPanel.style.paddingLeft = modalPanel.style.paddingRight = 24;
        modalPanel.style.paddingTop = modalPanel.style.paddingBottom = 24;
        modalPanel.style.backgroundColor = new Color(.06f, .095f, .14f, .98f);
        Border(modalPanel, new Color(.28f, .77f, 1f), 16);
        Label("HORIZONTE  ·  PRIMEIROS CORRES", 12, new Color(.38f, .82f, 1f), true, modalPanel);
        modalTitle = Label("", 24, Color.white, true, modalPanel);
        modalTitle.style.marginTop = 10;
        modalBody = Label("", 15, new Color(.86f, .92f, .97f), false, modalPanel);
        modalBody.style.whiteSpace = WhiteSpace.Normal;
        modalBody.style.marginTop = 10;
        modalBody.style.marginBottom = 15;
        modalButton = Button("", OnModalPrimary, modalPanel);
        postponeButton = Button("AGORA NÃO", HideModal, modalPanel);
        postponeButton.style.backgroundColor = new Color(.16f, .23f, .3f);
        modal.Add(modalPanel);
        CreateIntroArt();
        root.Add(modal);
        modal.RegisterCallback<GeometryChangedEvent>(_ => LayoutIntroArt());
        modal.style.display = DisplayStyle.None;
        prompt.style.display = DisplayStyle.None;
    }

    void CreateIntroArt()
    {
        if (!salveArt || !coinArt || !salveClip || !coinClip)
            throw new System.InvalidOperationException("Arte ou áudio do tutorial ausente.");
        introArt = new VisualElement { name = "tutorial-salve-art" };
        introArt.style.position = Position.Relative;
        introImage = new Image { image = salveArt, scaleMode = ScaleMode.StretchToFill,
            pickingMode = PickingMode.Ignore };
        introImage.style.position = Position.Absolute;
        introImage.style.left = introImage.style.top = introImage.style.right = introImage.style.bottom = 0;
        introArt.Add(introImage);
        introText = Label(IntroNarration, 20, new Color(.055f, .075f, .105f), true, introArt);
        introText.name = "tutorial-salve-balloon-text";
        introText.style.position = Position.Absolute;
        introText.style.unityTextAlign = TextAnchor.UpperLeft;
        introText.pickingMode = PickingMode.Ignore;
        introStart = new Button(OnModalPrimary) { name = "tutorial-salve-start",
            tooltip = "Continuar para receber 300 HZ Coins de teste" };
        introLater = new Button(HideModal) { name = "tutorial-salve-later", tooltip = "Agora não" };
        foreach (var button in new[] { introStart, introLater })
        {
            button.style.position = Position.Absolute;
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.paddingLeft = button.style.paddingRight = 0;
            button.style.paddingTop = button.style.paddingBottom = 0;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            introArt.Add(button);
        }
        modal.Add(introArt);
    }

    void LayoutIntroArt()
    {
        if (introArt == null || modal == null) return;
        float availableWidth = modal.resolvedStyle.width;
        float availableHeight = modal.resolvedStyle.height;
        if (float.IsNaN(availableWidth) || float.IsNaN(availableHeight) ||
            availableWidth < 100 || availableHeight < 100) return;
        float width = Mathf.Min(availableWidth * .96f, availableHeight * .92f * IntroArtAspect);
        float height = width / IntroArtAspect;
        introArt.style.width = width;
        introArt.style.height = height;
        introText.style.left = width * .375f;
        introText.style.top = height * .475f;
        introText.style.width = width * .54f;
        introText.style.height = height * .27f;
        introText.style.fontSize = Mathf.Clamp(width / 1672f * 28f, 14f, 27f);
        if (modalAction >= 3)
        {
            introText.style.top = height * .39f;
            introText.style.height = height * .29f;
            introText.style.fontSize = Mathf.Clamp(width / 1672f * 38f, 18f, 37f);
            introText.style.unityTextAlign = TextAnchor.MiddleLeft;
            introStart.style.fontSize = Mathf.Clamp(width / 1672f * 34f, 14f, 32f);
            introLater.style.fontSize = Mathf.Clamp(width / 1672f * 30f, 13f, 28f);
            introStart.style.color = new Color(.035f, .035f, .045f);
            introLater.style.color = Color.white;
        }
        else
        {
            introText.style.unityTextAlign = TextAnchor.UpperLeft;
        }
        introStart.style.left = width * .398f;
        introStart.style.top = height * .744f;
        introStart.style.width = width * .323f;
        introStart.style.height = height * .13f;
        introLater.style.left = width * .723f;
        introLater.style.top = height * .738f;
        introLater.style.width = width * .195f;
        introLater.style.height = height * .135f;
    }

    void ShowModal(string title, string body, string primary, int action)
    {
        if (modal == null || modalOpen) return;
        modalAction = action;
        modalTitle.text = title;
        modalBody.text = body;
        modalButton.text = primary;
        bool artSlide = action == 0 || action >= 3;
        postponeButton.style.display = action == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        introArt.style.display = artSlide ? DisplayStyle.Flex : DisplayStyle.None;
        modalPanel.style.display = artSlide ? DisplayStyle.None : DisplayStyle.Flex;
        modal.style.backgroundColor = artSlide ? new Color(.015f, .025f, .04f, .46f) :
            new Color(.015f, .025f, .04f, .78f);
        modal.style.display = DisplayStyle.Flex;
        if (action == 0)
        {
            introImage.image = salveArt;
            introText.text = IntroNarration;
            introStart.text = "";
            introLater.text = "";
            narration.clip = salveClip;
            narration.volume = 1f;
            introArt.style.opacity = 1f;
            LayoutIntroArt();
            if (narration && narration.clip) { narration.Stop(); narration.Play(); }
        }
        else if (artSlide)
        {
            narration.Stop();
            introImage.image = coinArt;
            introText.text = body;
            introStart.text = primary;
            introLater.text = "FECHAR";
            introArt.style.opacity = 1f;
            LayoutIntroArt();
        }
        modalOpen = true;
        combatWasLocked = player.CombatLocked;
        player.CombatLocked = true;
    }

    void OnModalPrimary()
    {
        if (changingSlide) return;
        if (modalAction == 0 && stage == 0)
        {
            slideRoutine = StartCoroutine(ShowCoinSlide());
            return;
        }
        if (modalAction == 3 && stage == 0)
        {
            SetStage(1);
            GameHud.Instance?.ShowToast("Boa! 300 HZ Coins de teste pra começar o corre");
            slideRoutine = StartCoroutine(ShowPrefeituraSlide());
            return;
        }
        HideModal();
    }

    IEnumerator ShowCoinSlide()
    {
        changingSlide = true;
        float elapsed = 0f;
        const float duration = .28f;
        while (elapsed < duration)
        {
            if (!modalOpen) { changingSlide = false; yield break; }
            elapsed += Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(elapsed / duration);
            introArt.style.opacity = 1f - fade;
            narration.volume = 1f - fade;
            yield return null;
        }
        narration.Stop();
        introImage.image = coinArt;
        introText.text = CoinNarration;
        introStart.text = "RECEBER 300 HZ COINS";
        introLater.text = "AGORA NÃO";
        introStart.tooltip = "Receber 300 HZ Coins de teste";
        modalAction = 3;
        LayoutIntroArt();
        narration.clip = coinClip;
        narration.volume = 0f;
        narration.Play();
        elapsed = 0f;
        while (elapsed < duration)
        {
            if (!modalOpen) { changingSlide = false; yield break; }
            elapsed += Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(elapsed / duration);
            introArt.style.opacity = fade;
            narration.volume = fade;
            yield return null;
        }
        introArt.style.opacity = 1f;
        narration.volume = 1f;
        changingSlide = false;
        slideRoutine = null;
    }

    IEnumerator ShowPrefeituraSlide()
    {
        changingSlide = true;
        float elapsed = 0f;
        const float duration = .28f;
        while (elapsed < duration)
        {
            if (!modalOpen) { changingSlide = false; yield break; }
            elapsed += Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(elapsed / duration);
            introArt.style.opacity = 1f - fade;
            narration.volume = 1f - fade;
            yield return null;
        }
        narration.Stop();
        modalAction = 4;
        introText.text = PrefeituraNarration;
        introStart.text = "PARTIU PREFEITURA";
        introLater.text = "FECHAR";
        LayoutIntroArt();
        elapsed = 0f;
        while (elapsed < duration)
        {
            if (!modalOpen) { changingSlide = false; yield break; }
            elapsed += Time.unscaledDeltaTime;
            introArt.style.opacity = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        introArt.style.opacity = 1f;
        changingSlide = false;
        slideRoutine = null;
    }

    void HideModal()
    {
        if (!modalOpen) return;
        modalOpen = false;
        if (slideRoutine != null) { StopCoroutine(slideRoutine); slideRoutine = null; }
        changingSlide = false;
        modal.style.display = DisplayStyle.None;
        if (narration && narration.isPlaying) narration.Stop();
        if (player) player.CombatLocked = combatWasLocked;
    }

    void BuildMarkers()
    {
        var anchor = player.transform.position;
        var rotation = Quaternion.Euler(0, player.transform.eulerAngles.y, 0);
        var civicDirection = rotation * new Vector3(1, 0, .35f).normalized;
        var schoolDirection = rotation * new Vector3(-.8f, 0, 1f).normalized;
        var civic = WalkableSpot(anchor, civicDirection, 11f, Vector3.positiveInfinity);
        var school = WalkableSpot(anchor, schoolDirection, 19f, civic);
        prefeitura = Marker("Prefeitura provisória · RG", civic, new Color(.2f, .85f, 1f), out prefeituraName);
        autoescola = Marker("Autoescola provisória · CNH", school, new Color(1f, .7f, .18f), out autoescolaName);
        RefreshUI();
    }

    static Vector3 WalkableSpot(Vector3 anchor, Vector3 direction, float radius, Vector3 avoid)
    {
        for (int sweep = 0; sweep < 9; sweep++)
            for (int step = 0; step < 3; step++)
            {
                float yaw = (sweep % 2 == 0 ? 1 : -1) * ((sweep + 1) / 2) * 22f;
                var candidate = anchor + Quaternion.Euler(0, yaw, 0) * direction * (radius + step * 3f);
                if (Vector3.Distance(candidate, avoid) < 7f) continue;
                var hits = Physics.RaycastAll(candidate + Vector3.up * 4f, Vector3.down, 9f, 1,
                    QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.normal.y < .72f || Mathf.Abs(hit.point.y - anchor.y) > 2.5f) continue;
                    var point = hit.point + Vector3.up * .06f;
                    if (NeighborhoodMap.Active && !NeighborhoodMap.Active.Contains(point)) continue;
                    if (Physics.CheckCapsule(point + Vector3.up * .4f, point + Vector3.up * 1.55f,
                        .35f, 1, QueryTriggerInteraction.Ignore)) continue;
                    return point;
                }
            }
        return anchor + direction * radius;
    }

    static GameObject Marker(string name, Vector3 position, Color color, out TextMesh label)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Ponto de atendimento fictício";
        disc.transform.SetParent(root.transform, false);
        disc.transform.localPosition = new Vector3(0, .04f, 0);
        disc.transform.localScale = new Vector3(1.45f, .035f, 1.45f);
        Destroy(disc.GetComponent<Collider>());
        var material = new Material(Shader.Find("Standard")) { color = color };
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 1.5f);
        disc.GetComponent<Renderer>().sharedMaterial = material;
        var sign = new GameObject("Nome do serviço");
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = Vector3.up * 2.2f;
        label = sign.AddComponent<TextMesh>();
        label.text = name.Contains("Prefeitura") ? "PREFEITURA\nTIRAR RG" : "AUTOESCOLA\nTIRAR CNH";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 64;
        label.characterSize = .055f;
        label.color = Color.white;
        return root;
    }

    static Label Label(string text, int size, Color color, bool bold, VisualElement parent)
    {
        var label = new Label(text);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.whiteSpace = WhiteSpace.Normal;
        if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
        parent.Add(label);
        return label;
    }

    static Button Button(string text, System.Action action, VisualElement parent)
    {
        var button = new Button(action) { text = text };
        button.style.height = 39;
        button.style.marginTop = 8;
        button.style.backgroundColor = new Color(.27f, .77f, 1f);
        button.style.color = new Color(.025f, .075f, .1f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        parent.Add(button);
        return button;
    }

    static void Border(VisualElement element, Color color, int radius)
    {
        element.style.borderTopWidth = element.style.borderBottomWidth = 1;
        element.style.borderLeftWidth = element.style.borderRightWidth = 1;
        element.style.borderTopColor = element.style.borderBottomColor = color;
        element.style.borderLeftColor = element.style.borderRightColor = color;
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }

    public bool BlocksPointer(Vector2 screen)
    {
        if (!document || document.rootVisualElement.panel == null) return false;
        if (modalOpen) return true;
        var point = RuntimePanelUtils.ScreenToPanel(document.rootVisualElement.panel,
            new Vector2(screen.x, Screen.height - screen.y));
        return questCard != null && questCard.resolvedStyle.display != DisplayStyle.None &&
            questCard.worldBound.Contains(point) ||
            prompt != null && prompt.resolvedStyle.display != DisplayStyle.None &&
            prompt.worldBound.Contains(point);
    }

    void OnDestroy()
    {
        if (modalOpen && player) player.CombatLocked = combatWasLocked;
        if (prefeitura) Destroy(prefeitura);
        if (autoescola) Destroy(autoescola);
        if (uiObject) Destroy(uiObject);
        if (Instance == this) Instance = null;
    }
}
