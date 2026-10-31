using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AdminHelpPanel : MonoBehaviour
{
    public static AdminHelpPanel Instance { get; private set; }
    public bool IsOpen => overlay != null && overlay.resolvedStyle.display != DisplayStyle.None;

    GameHud hud;
    VisualElement overlay;
    ScrollView list;
    TextField search;
    Label count;
    AdminHelpEntry[] entries = Array.Empty<AdminHelpEntry>();
    string selectedGroup = "Todos";

    public static void Open(GameHud owner, AdminHelpEntry[] data)
    {
        if (!Instance) owner.gameObject.AddComponent<AdminHelpPanel>().Build(owner);
        Instance.Show(data);
    }

    void Awake() { Instance = this; }

    static VisualElement Box(string css, VisualElement parent)
    {
        var element = new VisualElement(); element.AddToClassList(css); parent.Add(element); return element;
    }

    static Label Text(string value, string css, VisualElement parent)
    {
        var label = new Label(value) { enableRichText = false, pickingMode = PickingMode.Ignore };
        label.AddToClassList(css); parent.Add(label); return label;
    }

    void Build(GameHud owner)
    {
        hud = owner;
        var styles = Resources.Load<StyleSheet>("AdminHelpStyles");
        if (styles) hud.Root.styleSheets.Add(styles);
        overlay = Box("help-backdrop", hud.Root);
        overlay.name = "admin-help-overlay";
        overlay.style.display = DisplayStyle.None;
        hud.RegisterPointerBlocker(overlay);
        var panel = Box("help-panel", overlay);
        var header = Box("help-header", panel);
        var title = Box("help-heading", header);
        Text("HORIZONTE BRASIL · CENTRAL DE COMANDOS", "help-kicker", title);
        Text("Guia de comandos", "help-title", title);
        var close = new Button(Close) { text = "×", tooltip = "Fechar guia" };
        close.AddToClassList("help-close"); header.Add(close);
        Text("Consulte a função, a permissão e o estado de cada comando.", "help-subtitle", panel);
        var tools = Box("help-tools", panel);
        search = new TextField("Pesquisar comando") { name = "help-search" };
        search.AddToClassList("help-search");
        search.value = "";
        search.RegisterValueChangedCallback(_ => Render());
        tools.Add(search);
        var groups = Box("help-groups", panel);
        foreach (string group in new[] { "Todos", "Jogador", "Administração", "Polícia", "Atendimento" })
        {
            string choice = group;
            var button = new Button(() => { selectedGroup = choice; Render(); }) { text = group };
            button.AddToClassList("help-group"); button.name = "help-group-" + group;
            groups.Add(button);
        }
        count = Text("", "help-count", panel);
        list = new ScrollView(ScrollViewMode.Vertical) { name = "admin-help-list" };
        list.AddToClassList("help-list"); panel.Add(list);
        Text("Disponibilidade e permissões informadas pelo servidor.", "help-footer", panel);
    }

    void Show(AdminHelpEntry[] data)
    {
        InGameChat.Instance?.Close();
        hud.ClosePanel();
        if (AdminPlayerPanel.Instance && AdminPlayerPanel.Instance.IsOpen) AdminPlayerPanel.Instance.Close();
        entries = data ?? Array.Empty<AdminHelpEntry>();
        selectedGroup = "Todos";
        search.value = "";
        overlay.style.display = DisplayStyle.Flex;
        hud.Root.AddToClassList("hud-panel-open");
        Render();
    }

    void Render()
    {
        if (list == null) return;
        list.Clear();
        string query = search.value?.Trim() ?? "";
        int visible = 0;
        foreach (var item in entries)
        {
            if (item == null) continue;
            if (selectedGroup != "Todos" && item.group != selectedGroup) continue;
            if (query.Length > 0 && (item.usage ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                (item.description ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            visible++;
            var row = Box("help-row", list);
            var top = Box("help-row-top", row);
            Text(item.usage ?? "/?", "help-usage", top);
            bool locked = item.level > AccountSession.Instance.State.adminLevel;
            bool offDuty = item.group == "Polícia" && !AdminPlayerPanel.PoliceDuty;
            string state = !item.available ? "EM PREPARAÇÃO" : locked ? "NÍVEL INSUFICIENTE" : offDuty ? "FORA DE SERVIÇO" : "DISPONÍVEL";
            var badge = Text(state, "help-badge", top);
            if (item.available && !locked && !offDuty) badge.AddToClassList("help-badge-ready");
            Text(item.description ?? "", "help-description", row);
            Text((item.permission ?? "") + (string.IsNullOrEmpty(item.note) ? "" : " · " + item.note), "help-permission", row);
        }
        count.text = visible + " comando(s) · " + selectedGroup;
        if (visible == 0) Text("Nenhum comando encontrado.", "help-empty", list);
    }

    public void Close()
    {
        if (overlay != null) overlay.style.display = DisplayStyle.None;
        if (hud) hud.Root.RemoveFromClassList("hud-panel-open");
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void OnDestroy() { Close(); if (Instance == this) Instance = null; }
}
