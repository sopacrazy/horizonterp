using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

// Presentation only: membership, stock and rewards come from the authenticated API.
public sealed class CriminalEconomyPilotPanel : MonoBehaviour
{
    static CriminalEconomyPilotPanel instance;
    public static bool IsOpen => instance && instance.open;
    public static CriminalEconomyState LastState { get; private set; }
    public static event Action<CriminalEconomyState> StateChanged;
    VisualElement root, modal, body;
    Label organization, status;
    Button launcher, refresh, retry, adminTab;
    GameHud attachedHud;
    StyleSheet sheet;
    string section = "activity", selectedActivity, sessionKey, message = "Consultando o servidor...";
    string pendingPath, pendingJson, pendingRequestId;
    bool open, busy, fetched, synchronized;
    int generation;
    float nextLiveUpdate, nextAutomaticRefresh;
    CriminalEconomyHistory history;
    CriminalEconomyAdminOrganizations adminOrganizations;
    readonly List<Button> actionButtons = new List<Button>();
    readonly List<PointView> pointViews = new List<PointView>();
    readonly List<TabView> tabs = new List<TabView>();
    Label activeCountdown;
    Button activeComplete;
    CriminalEconomyActiveActivity displayedActivity;
    Label productionCountdown;
    Button productionComplete;
    double serverOffset;

    bool CanUse => AccountSession.HasCharacter && AccountSession.Instance.ReadyToEnterCity &&
        !AccountSession.InputBlocked && !StartupLoader.IsLoading && GameHud.Instance;
    bool CanAdmin => AccountSession.HasCharacter && AccountSession.Instance.State.accountRole == "admin" &&
        (AccountSession.Instance.State.adminLevel == 1 || AccountSession.Instance.State.adminLevel == 3);
    bool CanMutate => CanUse && !busy && pendingJson == null && synchronized && LastState != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!instance) new GameObject("Economia criminal").AddComponent<CriminalEconomyPilotPanel>();
    }
    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        StartCoroutine(AttachWhenHudIsReady());
    }
    IEnumerator AttachWhenHudIsReady()
    {
        var document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = Resources.Load<PanelSettings>("HudPanel");
        if (!document.panelSettings) { Debug.LogWarning("CRIMINAL_ECONOMY_DISABLED: HudPanel ausente."); yield break; }
        document.sortingOrder = 86;
        root = document.rootVisualElement;
        root.name = "criminal-economy-pilot";
        root.pickingMode = PickingMode.Ignore;
        sheet = Resources.Load<StyleSheet>("CriminalEconomyPilot");
        if (sheet) root.styleSheets.Add(sheet);
        Build();
        while (true)
        {
            var hud = GameHud.Instance;
            if (hud && hud.Root != null && (hud != attachedHud || launcher == null || launcher.parent != hud.Root))
            {
                Close();
                if (launcher != null) launcher.RemoveFromHierarchy();
                attachedHud = hud;
                if (sheet && !hud.Root.styleSheets.Contains(sheet)) hud.Root.styleSheets.Add(sheet);
                launcher = new Button(Toggle) { text = "ECONOMIA", tooltip = "Organização e inventário [F7]" };
                launcher.name = "criminal-economy-pilot-launcher";
                launcher.AddToClassList("economy-launcher");
                hud.Root.Add(launcher);
                hud.RegisterPointerBlocker(launcher);
                hud.RegisterPointerBlocker(modal);
            }
            yield return null;
        }
    }
    void OnDestroy()
    {
        if (launcher != null) launcher.RemoveFromHierarchy();
        if (instance == this) { instance = null; Publish(null); }
    }
    void Update()
    {
        string currentSession = AccountSession.Instance ? AccountSession.Instance.CriminalEconomySessionKey : null;
        if (currentSession != sessionKey)
        {
            sessionKey = currentSession; generation++;
            busy = fetched = synchronized = false; nextAutomaticRefresh = 0;
            history = null; adminOrganizations = null;
            pendingPath = pendingJson = pendingRequestId = null;
            message = "Consultando o servidor...";
            Close(); Publish(null);
        }
        if (launcher != null) launcher.style.display = CanUse ? DisplayStyle.Flex : DisplayStyle.None;
        if (!CanUse) { if (open) Close(); return; }
        if (!fetched && !busy && Time.unscaledTime >= nextAutomaticRefresh)
        { fetched = true; StartCoroutine(RefreshRoutine(false)); }
        if (!InGameChat.Editing && Input.GetKeyDown(KeyCode.F7)) Toggle();
        if (open && Input.GetKeyDown(KeyCode.Escape)) Close();
        if (open && Time.unscaledTime >= nextLiveUpdate)
        { nextLiveUpdate = Time.unscaledTime + .25f; UpdateLiveViews(); }
    }
    static void Publish(CriminalEconomyState value)
    { LastState = value; StateChanged?.Invoke(value); }
    public static void OpenSection(string kind, string activityCode = null)
    {
        if (!instance) Bootstrap();
        if (!instance) return;
        instance.section = kind == "warehouse" || kind == "craft" || kind == "shop" ? kind : "activity";
        instance.selectedActivity = activityCode;
        instance.Open();
    }
    void Build()
    {
        modal = Element("economy-overlay", root);
        modal.name = "criminal-economy-modal"; modal.pickingMode = PickingMode.Position;
        var panel = Element("economy-panel", modal);
        var header = Element("economy-header", panel);
        var titles = Element("economy-titles", header);
        Text("ORGANIZAÇÕES • CIDADE", "economy-eyebrow", titles);
        Text("Economia criminal", "economy-title", titles);
        organization = Text("Consultando vínculo...", "economy-organization", titles);
        var close = new Button(Close) { text = "×", tooltip = "Fechar [Esc]" };
        close.AddToClassList("economy-close"); header.Add(close);
        var tabRow = Element("economy-tabs", panel);
        Tab("ATIVIDADES", "activity", tabRow); Tab("DEPÓSITO", "warehouse", tabRow);
        Tab("BANCADA", "craft", tabRow); Tab("HISTÓRICO", "history", tabRow);
        Tab("LOJAS", "shop", tabRow);
        adminTab = Tab("MEMBROS", "admin", tabRow);
        body = new ScrollView(ScrollViewMode.Vertical); body.AddToClassList("economy-body"); panel.Add(body);
        var footer = Element("economy-footer", panel);
        refresh = new Button(() => { if (!busy) StartCoroutine(RefreshRoutine(true)); }) { text = "ATUALIZAR" };
        refresh.AddToClassList("economy-secondary"); footer.Add(refresh);
        retry = new Button(() => { if (!busy && pendingJson != null) StartCoroutine(MutateRoutine(pendingPath, pendingJson, pendingRequestId)); })
            { text = "CONFIRMAR SOLICITAÇÃO PENDENTE", tooltip = "Reenvia o mesmo identificador; o servidor não duplica a operação." };
        retry.AddToClassList("economy-secondary"); footer.Add(retry);
        status = Text(message, "economy-status", panel);
        modal.style.display = DisplayStyle.None; Render();
    }
    Button Tab(string title, string kind, VisualElement parent)
    {
        var button = new Button(() =>
        {
            section = kind; selectedActivity = null; Render();
            if (!busy && (kind == "history" || kind == "admin")) StartCoroutine(RefreshRoutine(true));
        }) { text = title };
        button.AddToClassList("economy-tab"); parent.Add(button);
        tabs.Add(new TabView { button = button, section = kind }); return button;
    }
    IEnumerator RefreshRoutine(bool includeSection)
    {
        if (busy || !CanUse) yield break;
        int capturedGeneration = generation;
        busy = true; Render();
        yield return LoadState(capturedGeneration);
        if (capturedGeneration != generation) yield break;
        if (includeSection && section == "admin" && CanAdmin) yield return LoadAdmin(capturedGeneration);
        else if ((includeSection && section == "history") || pendingJson != null) yield return LoadHistory(capturedGeneration);
        if (capturedGeneration != generation) yield break;
        busy = false; Render();
    }
    IEnumerator LoadState(int capturedGeneration)
    {
        synchronized = false;
        CriminalEconomyResponse response = null;
        yield return AccountSession.Instance.CriminalEconomyRequest("GET", "", null, r => response = r);
        if (capturedGeneration != generation || response == null || response.stale) yield break;
        if (!response.Success)
        {
            message = response.error ?? "Não foi possível atualizar a economia.";
            if (LastState == null) { fetched = false; nextAutomaticRefresh = Time.unscaledTime + 10; }
            yield break;
        }
        var incoming = Parse<CriminalEconomyState>(response.json);
        if (incoming == null || incoming.activities == null || incoming.inventory == null || incoming.locations == null)
        { message = "O servidor enviou um estado inválido. Atualize novamente."; yield break; }
        // JsonUtility can materialize JSON null as an empty serializable object.
        // Only records with their server identifiers represent existing state.
        if (incoming.activeActivity != null && string.IsNullOrEmpty(incoming.activeActivity.activityRunId)) incoming.activeActivity = null;
        if (incoming.organization != null && string.IsNullOrEmpty(incoming.organization.code)) incoming.organization = null;
        if (incoming.activeProduction != null && string.IsNullOrEmpty(incoming.activeProduction.productionId)) incoming.activeProduction = null;
        DateTimeOffset serverTime;
        serverOffset = DateTimeOffset.TryParse(incoming.serverTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out serverTime) ? (serverTime - DateTimeOffset.UtcNow).TotalSeconds : 0;
        synchronized = true;
        AccountSession.Instance.State.cashCents = incoming.cashCents.ToString(CultureInfo.InvariantCulture);
        if (GameHud.Instance && GameHud.Instance.Player) GameHud.Instance.Player.Cash = incoming.cashCents / 100;
        AccountSession.Instance.State.organization = incoming.organization == null ? null : new ApiOrganization
            { code = incoming.organization.code, name = incoming.organization.name, rank = incoming.organization.rank };
        Publish(incoming);
        message = pendingJson != null ? "Estado atualizado. Confirme a solicitação pendente pelo mesmo identificador." : "Dados sincronizados com o servidor.";
    }
    IEnumerator LoadHistory(int capturedGeneration)
    {
        CriminalEconomyResponse response = null;
        yield return AccountSession.Instance.CriminalEconomyRequest("GET", "/history", null, r => response = r);
        if (capturedGeneration != generation || response == null || response.stale) yield break;
        if (!response.Success) { message = response.error ?? "Não foi possível consultar o histórico."; yield break; }
        history = Parse<CriminalEconomyHistory>(response.json);
        if (pendingRequestId != null && history != null && (ContainsRequest(history.activities, pendingRequestId) || ContainsRequest(history.operations, pendingRequestId) || ContainsRequest(history.commerce, pendingRequestId)))
        { pendingPath = pendingJson = pendingRequestId = null; message = "Solicitação confirmada pelo histórico do servidor."; }
    }
    IEnumerator LoadAdmin(int capturedGeneration)
    {
        CriminalEconomyResponse response = null;
        yield return AccountSession.Instance.CriminalEconomyRequest("GET", "/admin/organizations", null, r => response = r);
        if (capturedGeneration != generation || response == null || response.stale) yield break;
        if (response.Success) adminOrganizations = Parse<CriminalEconomyAdminOrganizations>(response.json);
        else message = response.error ?? "Não foi possível consultar os membros.";
    }
    void Mutate(string path, object command, string requestId)
    { if (CanMutate) StartCoroutine(MutateRoutine(path, JsonUtility.ToJson(command), requestId)); }
    IEnumerator MutateRoutine(string path, string json, string requestId)
    {
        if (busy || !CanUse) yield break;
        int capturedGeneration = generation;
        busy = true; pendingPath = path; pendingJson = json; pendingRequestId = requestId;
        message = "Aguardando confirmação do servidor..."; Render();
        CriminalEconomyResponse response = null;
        yield return AccountSession.Instance.CriminalEconomyRequest("POST", path, json, r => response = r);
        if (capturedGeneration != generation || response == null || response.stale) yield break;
        bool uncertain = response.uncertain;
        string resultMessage = response.Success ? "Operação confirmada." : response.error ?? "A operação não foi autorizada.";
        if (!uncertain) pendingPath = pendingJson = pendingRequestId = null;
        // Reconcile reads first. Manual retries retain the exact JSON/requestId.
        yield return LoadState(capturedGeneration);
        if (capturedGeneration != generation) yield break;
        if (uncertain || section == "history") yield return LoadHistory(capturedGeneration);
        if (section == "admin" && CanAdmin) yield return LoadAdmin(capturedGeneration);
        if (capturedGeneration != generation) yield break;
        if (synchronized)
            message = pendingJson != null ? "Resposta incerta. Estado consultado; confirme a solicitação pendente para evitar duplicação." : resultMessage;
        else message = resultMessage + " Não foi possível atualizar os saldos. Atualize antes de uma nova operação.";
        busy = false; Render();
    }
    void Render()
    {
        if (body == null) return;
        body.Clear(); actionButtons.Clear(); pointViews.Clear();
        activeCountdown = null; activeComplete = null; displayedActivity = null;
        productionCountdown = null; productionComplete = null;
        var org = LastState?.organization;
        organization.text = org != null ? org.name + "  •  " + org.rank : "SEM ORGANIZAÇÃO";
        adminTab.style.display = CanAdmin ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var tab in tabs) tab.button.EnableInClassList("economy-selected", tab.section == section);
        refresh.SetEnabled(!busy); retry.style.display = pendingJson != null ? DisplayStyle.Flex : DisplayStyle.None; retry.SetEnabled(!busy);
        status.text = busy ? "Sincronizando com o servidor..." : message;
        if (section == "admin" && CanAdmin) { RenderAdmin(); return; }
        if (LastState == null) { Text("Economia indisponível", "economy-section-title", body); Text("Atualize para consultar seu inventário e as atividades da cidade.", "economy-copy", body); return; }
        if (section == "warehouse") RenderWarehouse();
        else if (section == "craft") RenderCraft();
        else if (section == "history") RenderHistory();
        else if (section == "shop") RenderShops();
        else RenderActivities();
        UpdateLiveViews();
    }
    void RenderActivities()
    {
        Text("Atividades da cidade", "economy-section-title", body);
        Text("Vá ao ponto de trabalho para iniciar. A conclusão e os itens são confirmados pelo servidor.", "economy-copy", body);
        var active = LastState.activeActivity;
        if (active != null && !string.IsNullOrEmpty(active.activityRunId))
        {
            displayedActivity = active;
            var card = Element("economy-recipe", body);
            Text("EM ANDAMENTO • " + ActivityName(active.activityCode), "economy-card-title", card);
            activeCountdown = Text("", "economy-copy", card); AddPoint(active.location, card);
            activeComplete = ActionButton("CONCLUIR ATIVIDADE", () =>
            {
                string id = Guid.NewGuid().ToString();
                Mutate("/activities/complete", new CriminalEconomyCompleteBody { activityCode = active.activityCode, activityRunId = active.activityRunId, requestId = id }, id);
            }, card);
            ActionButton("CANCELAR ATIVIDADE", () =>
            {
                string id = Guid.NewGuid().ToString();
                Mutate("/activities/cancel", new CriminalEconomyCancelBody { activityRunId = active.activityRunId, requestId = id }, id);
            }, card);
            Text("A atividade expira 15 minutos após ficar pronta. Cancelar não entrega materiais.", "economy-copy", card);
        }
        foreach (var activity in LastState.activities ?? new CriminalEconomyActivity[0])
        {
            if (!string.IsNullOrEmpty(selectedActivity) && selectedActivity != activity.code) continue;
            var current = activity; var card = Element("economy-recipe", body);
            Text(current.name, "economy-card-title", card);
            Text("Recompensa: " + current.reward_quantity + " × " + ItemName(current.reward_item_code) + " • duração: " + current.duration_seconds + " s • intervalo: " + current.cooldown_seconds + " s", "economy-copy", card);
            var point = FindPoint("activity", current.code); AddPoint(point, card);
            var start = ActionButton("INICIAR", () =>
            {
                string id = Guid.NewGuid().ToString();
                Mutate("/activities/start", new CriminalEconomyStartBody { activityCode = current.code, requestId = id }, id);
            }, card);
            start.userData = new ActionGate { point = point, allowed = active == null || string.IsNullOrEmpty(active.activityRunId) };
        }
        InventorySummary("SEU INVENTÁRIO", LastState.inventory, body);
    }
    void RenderWarehouse()
    {
        Text("Depósito da organização", "economy-section-title", body);
        if (LastState.organization == null) { Text("Sua conta não possui vínculo com uma organização.", "economy-copy", body); return; }
        var point = FindPoint("warehouse"); AddPoint(point, body);
        Text("Defina a quantidade por item. Seu cargo determina quais operações estão disponíveis.", "economy-copy", body);
        InventorySummary("ESTOQUE DO DEPÓSITO", LastState.warehouse, body);
        var itemCodes = new List<string>();
        foreach (var item in LastState.inventory ?? new CriminalEconomyItem[0]) if (!itemCodes.Contains(item.itemCode)) itemCodes.Add(item.itemCode);
        foreach (var item in LastState.warehouse ?? new CriminalEconomyItem[0]) if (!itemCodes.Contains(item.itemCode)) itemCodes.Add(item.itemCode);
        foreach (var code in itemCodes)
        {
            string itemCode = code;
            var row = Element("economy-stock-row", body); var copy = Element("economy-stock-copy", row);
            Text(ItemName(itemCode), "economy-stock-name", copy);
            Text("Com você: " + Quantity(LastState.inventory, code) + " • depósito: " + Quantity(LastState.warehouse, code), "economy-stock-value", copy);
            var quantity = new IntegerField { value = 1 }; quantity.AddToClassList("economy-quantity"); row.Add(quantity);
            var put = ActionButton("DEPOSITAR", () => Transfer("deposit", itemCode, quantity.value), row);
            put.userData = new ActionGate { point = point, allowed = LastState.organization.permissions != null && LastState.organization.permissions.deposit && Quantity(LastState.inventory, code) > 0 };
            var take = ActionButton("RETIRAR", () => Transfer("withdraw", itemCode, quantity.value), row);
            take.userData = new ActionGate { point = point, allowed = LastState.organization.permissions != null && LastState.organization.permissions.withdraw && Quantity(LastState.warehouse, code) > 0 };
        }
    }
    void Transfer(string operation, string itemCode, int quantity)
    {
        if (quantity < 1) { status.text = "Informe uma quantidade inteira positiva."; return; }
        string id = Guid.NewGuid().ToString();
        Mutate("/warehouse/" + operation, new CriminalEconomyTransferBody { itemCode = itemCode, quantity = quantity, requestId = id }, id);
    }
    void RenderCraft()
    {
        Text("Bancada de produção", "economy-section-title", body);
        if (LastState.organization == null) { Text("Sua conta não possui vínculo com uma organização.", "economy-copy", body); return; }
        var point = FindPoint("craft"); AddPoint(point, body);
        Text("Os materiais do depósito são consumidos e a produção retorna ao depósito da organização.", "economy-copy", body);
        var production = LastState.activeProduction;
        bool pending = production != null && !string.IsNullOrEmpty(production.productionId);
        bool allowed = LastState.organization.permissions != null && LastState.organization.permissions.craft;
        if (pending)
        {
            var card = Element("economy-recipe", body);
            Text("PRODUÇÃO EM ANDAMENTO • " + ItemName(production.output?.itemCode), "economy-card-title", card);
            Text("Lotes: " + production.batches + " • " + (production.output?.quantity ?? 0) + " unidades. O prazo continua mesmo fora do jogo.", "economy-copy", card);
            productionCountdown = Text("", "economy-copy", card);
            productionComplete = ActionButton("RECOLHER PRODUÇÃO", () => {
                string id = Guid.NewGuid().ToString();
                Mutate("/production/complete", new CriminalEconomyProductionBody { productionId = production.productionId, requestId = id }, id);
            }, card);
            productionComplete.userData = new ActionGate { point = point, allowed = allowed };
            var cancel = ActionButton("CANCELAR E DEVOLVER MATERIAIS", () => {
                string id = Guid.NewGuid().ToString();
                Mutate("/production/cancel", new CriminalEconomyProductionBody { productionId = production.productionId, requestId = id }, id);
            }, card);
            cancel.userData = new ActionGate { point = point, allowed = allowed };
            Text("Qualquer responsável autorizado da organização pode recolher o lote. Para cancelar, o depósito precisa comportar os materiais devolvidos.", "economy-copy", card);
        }
        foreach (var recipe in LastState.recipes ?? new CriminalEconomyRecipe[0])
        {
            var current = recipe; var card = Element("economy-recipe", body);
            Text(current.name, "economy-recipe-title", card);
            Text("Produz " + current.outputQuantity + " × " + ItemName(current.outputItemCode) + " por lote • " + current.durationSeconds + " segundos por lote", "economy-copy", card);
            foreach (var ingredient in current.ingredients ?? new CriminalEconomyItem[0]) Text(ingredient.quantity + " × " + ItemName(ingredient.itemCode), "economy-recipe-requirements", card);
            var batches = new IntegerField("Lotes") { value = 1 }; batches.AddToClassList("economy-batches"); card.Add(batches);
            var button = ActionButton("INICIAR PRODUÇÃO", () =>
            {
                if (batches.value < 1) { status.text = "Informe uma quantidade positiva de lotes."; return; }
                string id = Guid.NewGuid().ToString();
                Mutate("/production/craft", new CriminalEconomyCraftBody { recipeCode = current.code, batches = batches.value, requestId = id }, id);
            }, card);
            button.userData = new ActionGate { point = point, allowed = allowed && !pending };
        }
        InventorySummary("ESTOQUE DO DEPÓSITO", LastState.warehouse, body);
    }
    void RenderHistory()
    {
        Text("Histórico persistente", "economy-section-title", body);
        Text("Últimas atividades e operações confirmadas pelo servidor.", "economy-copy", body);
        if (history == null) { Text("Atualize para carregar os registros.", "economy-copy", body); return; }
        Text("ATIVIDADES", "economy-summary-title", body);
        foreach (var entry in history.activities ?? new CriminalEconomyHistoryEntry[0])
            Text((entry.completedAt ?? entry.startedAt) + " • " + ActivityName(entry.activityCode) + (entry.reward != null ? " • " + entry.reward.quantity + " × " + entry.reward.itemCode : "") + "\nID: " + entry.requestId, "economy-audit-row", body);
        Text("OPERAÇÕES", "economy-summary-title", body);
        foreach (var entry in history.operations ?? new CriminalEconomyHistoryEntry[0])
            Text(entry.createdAt + " • " + (entry.operation ?? entry.type ?? entry.kind) + " • " + (entry.itemCode ?? entry.recipeCode) + " × " + (entry.batches > 0 ? entry.batches : entry.quantity) + "\nID: " + entry.requestId, "economy-audit-row", body);
        Text("COMÉRCIO E CANCELAMENTOS", "economy-summary-title", body);
        foreach (var entry in history.commerce ?? new CriminalEconomyHistoryEntry[0])
            Text(entry.createdAt + " • " + CommerceName(entry.operation) +
                (string.IsNullOrEmpty(entry.itemCode) ? "" : " • " + ItemName(entry.itemCode) + " × " + entry.quantity) +
                (entry.totalCents > 0 ? " • " + Money(entry.totalCents) : entry.amountCents > 0 ? " • " + Money(entry.amountCents) : entry.priceCents > 0 ? " • " + Money(entry.priceCents) : "") +
                "\nID: " + entry.requestId, "economy-audit-row", body);
    }
    static string CommerceName(string operation)
    {
        switch (operation) {
            case "buy": return "Compra"; case "supply": return "Abastecimento";
            case "recall": return "Devolução ao depósito"; case "price": return "Alteração de preço";
            case "proceeds": return "Retirada do caixa"; case "cancel": return "Atividade cancelada";
            case "production_start": return "Produção iniciada"; case "production_complete": return "Produção recolhida";
            case "production_cancel": return "Produção cancelada e materiais devolvidos";
            default: return operation;
        }
    }
    static string Money(long cents) => "R$ " + (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
    void RenderShops()
    {
        Text("Lojas abastecidas pelas organizações", "economy-section-title", body);
        Text("Sua carteira: " + Money(LastState.cashCents) + " • Compras exigem presença na loja. Nenhum estoque é gerado automaticamente.", "economy-copy", body);
        foreach (var shop in LastState.shops ?? new CriminalEconomyShop[0])
        {
            var current = shop; var card = Element("economy-recipe", body);
            Text(current.name, "economy-card-title", card);
            CriminalEconomyLocation point = null;
            foreach (var location in LastState.locations) if (location.code == current.code) point = location;
            AddPoint(point, card);
            bool manage = LastState.organization != null && current.organization == LastState.organization.code && LastState.organization.permissions != null && LastState.organization.permissions.withdraw;
            if (current.stock == null || current.stock.Length == 0) Text("Loja sem produtos. O responsável precisa abastecer pelo depósito.", "economy-copy", card);
            foreach (var stock in current.stock ?? new CriminalEconomyShopStock[0])
            {
                var product = stock;
                Text(ItemName(product.itemCode) + " • " + product.quantity + " un. • " + Money(product.priceCents), "economy-copy", card);
                var quantity = new IntegerField("Quantidade") { value = 1 }; card.Add(quantity);
                var buy = ActionButton("COMPRAR", () => {
                    string id = Guid.NewGuid().ToString();
                    Mutate("/shops/buy", new CriminalEconomyShopBody { shopCode = current.code, itemCode = product.itemCode, quantity = quantity.value, priceCents = product.priceCents, requestId = id }, id);
                }, card);
                buy.userData = new ActionGate { point = point, allowed = product.quantity > 0 };
                if (manage)
                {
                    var newPrice = new IntegerField("Novo preço unitário (centavos)") { value = product.priceCents }; card.Add(newPrice);
                    var changePrice = ActionButton("ALTERAR PREÇO", () => {
                        string id = Guid.NewGuid().ToString();
                        Mutate("/shops/price", new CriminalEconomyPriceBody { shopCode = current.code, itemCode = product.itemCode, priceCents = newPrice.value, requestId = id }, id);
                    }, card);
                    changePrice.userData = new ActionGate { point = point, allowed = true };
                    var recall = ActionButton("DEVOLVER AO DEPÓSITO", () => {
                        string id = Guid.NewGuid().ToString();
                        Mutate("/shops/recall", new CriminalEconomyRecallBody { shopCode = current.code, itemCode = product.itemCode, quantity = quantity.value, requestId = id }, id);
                    }, card);
                    recall.userData = new ActionGate { point = point, allowed = product.quantity > 0 };
                }
            }
            if (!manage) continue;
            Text("GESTÃO DA LOJA • Caixa: " + Money(current.proceedsCents), "economy-summary-title", card);
            foreach (var recipe in LastState.recipes ?? new CriminalEconomyRecipe[0])
            {
                string itemCode = recipe.outputItemCode;
                Text("Abastecer " + ItemName(itemCode) + " • depósito: " + Quantity(LastState.warehouse, itemCode), "economy-copy", card);
                var quantity = new IntegerField("Unidades") { value = 1 }; card.Add(quantity);
                var price = new IntegerField("Preço unitário em centavos (100 = R$ 1)") { value = 10000 }; card.Add(price);
                var supply = ActionButton("ABASTECER LOJA", () => {
                    string id = Guid.NewGuid().ToString();
                    Mutate("/shops/supply", new CriminalEconomyShopBody { shopCode = current.code, itemCode = itemCode, quantity = quantity.value, priceCents = price.value, requestId = id }, id);
                }, card);
                supply.userData = new ActionGate { point = point, allowed = Quantity(LastState.warehouse, itemCode) > 0 };
            }
            if (LastState.organization.rank == "lider" || LastState.organization.rank == "sublider")
            {
                var amount = new IntegerField("Recolher do caixa (centavos)") { value = 100 }; card.Add(amount);
                var proceeds = ActionButton("RECOLHER CAIXA", () => {
                    string id = Guid.NewGuid().ToString();
                    Mutate("/shops/proceeds", new CriminalEconomyProceedsBody { shopCode = current.code, quantity = amount.value, requestId = id }, id);
                }, card);
                proceeds.userData = new ActionGate { point = point, allowed = current.proceedsCents > 0 };
            }
        }
    }
    void RenderAdmin()
    {
        Text("Vínculos e cargos", "economy-section-title", body);
        Text("Administradores autorizados atribuem organização e cargo por UID. Para remover um vínculo, selecione SEM ORGANIZAÇÃO.", "economy-copy", body);
        if (adminOrganizations?.organizations == null) { Text("Atualize para consultar as organizações.", "economy-copy", body); return; }
        var uid = new TextField("UID do jogador"); body.Add(uid);
        var names = new List<string> { "SEM ORGANIZAÇÃO" };
        foreach (var org in adminOrganizations.organizations) names.Add(org.name);
        var orgChoice = new DropdownField("Organização", names, 0); body.Add(orgChoice);
        var rankChoice = new DropdownField("Cargo", new List<string> { "none" }, 0); body.Add(rankChoice);
        orgChoice.RegisterValueChangedCallback(_ =>
        {
            int index = orgChoice.index - 1;
            var ranks = index >= 0 ? adminOrganizations.organizations[index].ranks : new[] { "none" };
            rankChoice.choices = new List<string>(ranks ?? new[] { "none" });
            rankChoice.index = rankChoice.choices.Count > 0 ? 0 : -1;
        });
        ActionButton("SALVAR VÍNCULO", () =>
        {
            if (!CanMutate || string.IsNullOrWhiteSpace(uid.value)) { status.text = "Informe o UID do jogador."; return; }
            int index = orgChoice.index - 1;
            var assignment = new CriminalEconomyAssignment { uid = uid.value.Trim(), organizationCode = index >= 0 ? adminOrganizations.organizations[index].code : "none", rankCode = index >= 0 ? rankChoice.value : "none" };
            Mutate("/admin/organization", assignment, null);
        }, body);
        foreach (var org in adminOrganizations.organizations)
        {
            Text(org.name, "economy-section-title", body);
            foreach (var member in org.members ?? new CriminalEconomyMember[0]) Text(member.uid + " • " + member.firstName + " • " + member.rank, "economy-audit-row", body);
        }
    }
    void AddPoint(CriminalEconomyLocation point, VisualElement parent)
    {
        if (point == null) { Text("Ponto indisponível para este vínculo.", "economy-copy", parent); return; }
        pointViews.Add(new PointView { label = Text("", "economy-point", parent), point = point });
    }
    void UpdateLiveViews()
    {
        foreach (var view in pointViews)
        {
            var p = view.point; float distance = Distance(p);
            view.label.text = p.label + " • " + p.world + " • (" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", " + p.z.ToString("F1") + ")\n" +
                (float.IsPositiveInfinity(distance) ? "Entre no mundo do ponto." : "Distância: " + distance.ToString("F1") + " m • alcance: " + p.radius.ToString("F1") + " m");
        }
        foreach (var button in actionButtons)
        {
            var gate = button.userData as ActionGate;
            button.SetEnabled(CanMutate && (gate == null || gate.allowed && AtPoint(gate.point)));
        }
        if (activeCountdown != null && displayedActivity != null)
        {
            double seconds = SecondsUntilReady(displayedActivity);
            activeCountdown.text = double.IsPositiveInfinity(seconds) ? "Atualize para consultar o prazo da atividade." : seconds > 0 ? "Conclusão liberada em " + Math.Ceiling(seconds) + " s" : "Atividade pronta. Conclua no ponto de trabalho.";
            activeComplete.SetEnabled(CanMutate && seconds <= 0 && AtPoint(displayedActivity.location));
        }
        if (productionCountdown != null && productionComplete != null && LastState?.activeProduction != null)
        {
            double seconds = SecondsUntil(LastState.activeProduction.readyAt);
            productionCountdown.text = seconds > 0 ? "Pronto em " + Math.Ceiling(seconds) + " s" : "Lote pronto. Recolha na bancada para enviar ao depósito.";
            productionComplete.SetEnabled(CanMutate && seconds <= 0 && LastState.organization?.permissions != null && LastState.organization.permissions.craft && AtPoint(FindPoint("craft")));
        }
    }
    CriminalEconomyLocation FindPoint(string kind, string activityCode = null)
    {
        foreach (var point in LastState?.locations ?? new CriminalEconomyLocation[0]) if (point.kind == kind && (activityCode == null || point.activityCode == activityCode)) return point;
        return null;
    }
    static float Distance(CriminalEconomyLocation point)
    {
        if (point == null || !AccountSession.HasCharacter || (!string.IsNullOrEmpty(point.world) && point.world != AccountSession.Instance.State.world)) return float.PositiveInfinity;
        var player = GameHud.Instance ? GameHud.Instance.Player : null;
        return player ? Vector3.Distance(player.transform.position, new Vector3(point.x, point.y, point.z)) : float.PositiveInfinity;
    }
    static bool AtPoint(CriminalEconomyLocation point) => point != null && Distance(point) <= point.radius;
    double SecondsUntilReady(CriminalEconomyActiveActivity activity) => SecondsUntil(activity.readyAt);
    double SecondsUntil(string readyAt)
    {
        DateTimeOffset ready;
        return DateTimeOffset.TryParse(readyAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out ready) ? Math.Max(0, (ready - DateTimeOffset.UtcNow.AddSeconds(serverOffset)).TotalSeconds) : double.PositiveInfinity;
    }
    string ActivityName(string code)
    { foreach (var activity in LastState?.activities ?? new CriminalEconomyActivity[0]) if (activity.code == code) return activity.name; return code; }
    static int Quantity(CriminalEconomyItem[] items, string code)
    { foreach (var item in items ?? new CriminalEconomyItem[0]) if (item.itemCode == code) return item.quantity; return 0; }
    static bool ContainsRequest(CriminalEconomyHistoryEntry[] entries, string id)
    { foreach (var entry in entries ?? new CriminalEconomyHistoryEntry[0]) if (entry.requestId == id) return true; return false; }
    static T Parse<T>(string json) where T : class
    { try { return JsonUtility.FromJson<T>(json); } catch { return null; } }
    static void InventorySummary(string title, CriminalEconomyItem[] items, VisualElement parent)
    {
        var summary = Element("economy-summary", parent); Text(title, "economy-summary-title", summary);
        if (items == null || items.Length == 0) Text("Sem itens.", "economy-summary-item", summary);
        foreach (var item in items ?? new CriminalEconomyItem[0]) Text(ItemName(item.itemCode) + ": " + item.quantity, "economy-summary-item", summary);
    }
    static string ItemName(string code)
    {
        switch (code)
        {
            case "pilot_scrap": return "Sucata";
            case "rp_parafal": return "Parafal (RP)";
            case "rp_ak47": return "AK-47 (RP)";
            case "rp_m4": return "M4 (RP)";
            case "rp_ammo_parafal": return "Munição de Parafal (RP)";
            case "rp_ammo_ak47": return "Munição de AK-47 (RP)";
            case "rp_ammo_m4": return "Munição de M4 (RP)";
            case "pilot_crystal": return "Cristal";
            case "pilot_logistics_crate": return "Caixa logística";
            default: return code;
        }
    }
    Button ActionButton(string title, Action action, VisualElement parent)
    {
        var button = new Button(action) { text = title }; button.AddToClassList("economy-primary"); parent.Add(button);
        actionButtons.Add(button); button.SetEnabled(CanMutate); return button;
    }
    void Toggle() { if (open) Close(); else Open(); }
    void Open()
    {
        if (modal == null || !CanUse) return;
        if (InGameChat.Editing && InGameChat.Instance) InGameChat.Instance.Close();
        GameHud.Instance.ClosePanel(); open = true; modal.style.display = DisplayStyle.Flex; Render();
        if (!busy) StartCoroutine(RefreshRoutine(true));
    }
    void Close() { open = false; if (modal != null) modal.style.display = DisplayStyle.None; }
    static VisualElement Element(string css, VisualElement parent)
    { var element = new VisualElement(); element.AddToClassList(css); parent.Add(element); return element; }
    static Label Text(string value, string css, VisualElement parent)
    { var label = new Label(value) { pickingMode = PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); return label; }
    sealed class PointView { public Label label; public CriminalEconomyLocation point; }
    sealed class TabView { public Button button; public string section; }
    sealed class ActionGate { public CriminalEconomyLocation point; public bool allowed; }
}
