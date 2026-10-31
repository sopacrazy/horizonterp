using UnityEngine;
using UnityEngine.UIElements;

public sealed class WantedListPanel : MonoBehaviour
{
    static WantedListPanel instance;
    GameHud hud;
    VisualElement overlay;
    ScrollView list;

    public static void Open(GameHud owner, WantedEntry[] entries)
    {
        if (!instance) owner.gameObject.AddComponent<WantedListPanel>().Build(owner);
        instance.Show(entries);
    }

    void Awake() { instance = this; }

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
        var styles = Resources.Load<StyleSheet>("WantedStyles");
        if (styles) hud.Root.styleSheets.Add(styles);
        overlay = Box("wanted-backdrop", hud.Root);
        overlay.name = "wanted-overlay";
        overlay.style.display = DisplayStyle.None;
        hud.RegisterPointerBlocker(overlay);
        var paper = Box("wanted-paper", overlay);
        var header = Box("wanted-header", paper);
        var heading = Box("wanted-heading", header);
        Text("POLÍCIA MILITAR · HORIZONTE BRASIL", "wanted-kicker", heading);
        Text("Lista de procurados", "wanted-title", heading);
        var close = new Button(Close) { name = "wanted-close", text = "×", tooltip = "Fechar lista" };
        close.AddToClassList("wanted-close"); header.Add(close);
        Text("REGISTROS ATIVOS", "wanted-caption", paper);
        list = new ScrollView(ScrollViewMode.Vertical) { name = "wanted-list" };
        list.AddToClassList("wanted-list"); paper.Add(list);
        Text("Documento de consulta policial · atualizado pelo servidor", "wanted-footer", paper);
    }

    void Show(WantedEntry[] entries)
    {
        InGameChat.Instance?.Close(); hud.ClosePanel();
        if (AdminPlayerPanel.Instance && AdminPlayerPanel.Instance.IsOpen) AdminPlayerPanel.Instance.Close();
        list.Clear();
        if (entries == null || entries.Length == 0)
            Text("Nenhum procurado no momento.", "wanted-empty", list);
        else foreach (var entry in entries)
        {
            var row = Box("wanted-row", list);
            var top = Box("wanted-row-top", row);
            Text(entry.name ?? "Jogador", "wanted-name", top);
            Text("ID " + (entry.uid ?? "—"), "wanted-id", top);
            Text("NÍVEL DE PROCURA  " + Mathf.Clamp(entry.stars, 0, 6) + "/6", "wanted-stars", row);
            Text("Motivo: " + (entry.reason ?? "Não informado"), "wanted-reason", row);
        }
        overlay.style.display = DisplayStyle.Flex;
        hud.Root.AddToClassList("hud-panel-open");
    }

    void Close()
    {
        if (overlay != null) overlay.style.display = DisplayStyle.None;
        if (hud) hud.Root.RemoveFromClassList("hud-panel-open");
    }

    void Update()
    {
        if (overlay != null && overlay.resolvedStyle.display != DisplayStyle.None && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void OnDestroy() { Close(); if (instance == this) instance = null; }
}
