using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud
{
    [System.Serializable]
    sealed class ActionPlacement
    {
        public float x, y, size;
    }

    static readonly string[] ActionNames = { "Soco", "Pular", "Microfone", "Correr" };
    VisualElement[] actionControls;
    VisualElement actionEditor;
    VisualElement[] actionHandles;
    ActionPlacement[] actionPlacements;
    Slider actionSize;
    int selectedAction;
    string actionLayoutKey;

    void SetupActionLayout(params VisualElement[] controls)
    {
        actionControls = controls;
        actionPlacements = new ActionPlacement[controls.Length];
        root.RegisterCallback<GeometryChangedEvent>(_ => RefreshActionLayout());
        controls[3].parent.RegisterCallback<GeometryChangedEvent>(_ => RefreshActionLayout());
        root.schedule.Execute(RefreshActionLayout);
    }

    void RefreshActionLayout()
    {
        string key = "hud.action-layout.v1." + (AccountSession.Instance?.State?.uid ?? "local");
        if (actionLayoutKey != key)
        {
            actionLayoutKey = key;
            for (int i = 0; i < actionControls.Length; i++)
            {
                ClearActionStyle(actionControls[i]);
                var saved = PlayerPrefs.GetString(key + "." + i, "");
                actionPlacements[i] = string.IsNullOrEmpty(saved) ? null : JsonUtility.FromJson<ActionPlacement>(saved);
            }
        }
        if (!(root.layout.width > 0 && root.layout.height > 0)) return;
        for (int i = 0; i < actionControls.Length; i++)
        {
            if (actionPlacements[i] != null) ApplyActionPlacement(i);
        }
        if (actionEditor != null) PositionActionHandles();
    }

    static void ClearActionStyle(VisualElement control)
    {
        control.style.position = StyleKeyword.Null;
        control.style.left = control.style.right = control.style.top = control.style.bottom = StyleKeyword.Null;
        control.style.width = control.style.height = control.style.marginLeft = control.style.marginTop = StyleKeyword.Null;
        control.style.borderTopLeftRadius = control.style.borderTopRightRadius =
            control.style.borderBottomLeftRadius = control.style.borderBottomRightRadius = StyleKeyword.Null;
        if (control.childCount > 0)
            control[0].style.width = control[0].style.height = StyleKeyword.Null;
        var caption = control.Q(className: "realcity-weapon");
        if (caption != null) caption.style.display = StyleKeyword.Null;
    }

    Rect ActionRect(ActionPlacement placement)
    {
        float width = root.layout.width, height = root.layout.height;
        float size = Mathf.Clamp(placement.size * height, 32f, Mathf.Min(width, height) * .7f);
        return new Rect(Mathf.Clamp(placement.x * width - size / 2, 0, width - size),
            Mathf.Clamp(placement.y * height - size / 2, 0, height - size), size, size);
    }

    void ApplyActionPlacement(int index)
    {
        var control = actionControls[index];
        var rect = ActionRect(actionPlacements[index]);
        var local = control.parent.WorldToLocal(root.LocalToWorld(rect.position));
        control.style.position = Position.Absolute;
        control.style.left = local.x; control.style.top = local.y;
        control.style.right = control.style.bottom = StyleKeyword.Auto;
        control.style.marginLeft = control.style.marginTop = 0;
        control.style.width = control.style.height = rect.width;
        control.style.borderTopLeftRadius = control.style.borderTopRightRadius =
            control.style.borderBottomLeftRadius = control.style.borderBottomRightRadius = rect.width / 2;
        if (control.childCount > 0)
            control[0].style.width = control[0].style.height = rect.width;
        var caption = control.Q(className: "realcity-weapon");
        if (caption != null) caption.style.display = DisplayStyle.None;
    }

    void OpenActionEditor()
    {
        if (!(root.layout.width > 0 && root.layout.height > 0)) return;
        ClosePanel();
        CurrentPanel = "hud-layout";
        actionEditor = new VisualElement { name = "hud-action-editor" };
        actionEditor.style.position = Position.Absolute;
        actionEditor.style.left = actionEditor.style.right = actionEditor.style.top = actionEditor.style.bottom = 0;
        actionHandles = new VisualElement[actionControls.Length];
        for (int i = 0; i < actionControls.Length; i++)
        {
            int index = i;
            var rect = actionControls[i].ChangeCoordinatesTo(root, actionControls[i].contentRect);
            if (actionPlacements[i] == null)
            {
                actionPlacements[i] = new ActionPlacement
                {
                    x = rect.center.x / root.layout.width, y = rect.center.y / root.layout.height,
                    size = Mathf.Max(48f, rect.height) / root.layout.height
                };
            }
            var handle = new Label(ActionNames[i]);
            handle.style.position = Position.Absolute;
            handle.style.unityTextAlign = TextAnchor.MiddleCenter;
            handle.style.color = Color.white;
            handle.style.backgroundColor = new Color(.05f,.12f,.18f,.78f);
            handle.style.borderTopWidth = handle.style.borderBottomWidth =
                handle.style.borderLeftWidth = handle.style.borderRightWidth = 2;
            int pointer = -1;
            Vector2 offset = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (pointer != -1 || evt.button != 0) return;
                SelectAction(index);
                pointer = evt.pointerId;
                offset = ActionRect(actionPlacements[index]).center - root.WorldToLocal((Vector2)evt.position);
                handle.CapturePointer(pointer);
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != pointer) return;
                Vector2 center = root.WorldToLocal((Vector2)evt.position) + offset;
                var placement = actionPlacements[index];
                float half = ActionRect(placement).width / 2;
                placement.x = Mathf.Clamp(center.x, half, root.layout.width - half) / root.layout.width;
                placement.y = Mathf.Clamp(center.y, half, root.layout.height - half) / root.layout.height;
                ApplyActionPlacement(index);
                PositionActionHandles();
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != pointer) return;
                handle.ReleasePointer(pointer);
                pointer = -1;
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => pointer = -1);
            actionHandles[i] = handle;
            actionEditor.Add(handle);
        }
        var toolbar = new VisualElement();
        toolbar.style.alignSelf = Align.Center;
        toolbar.style.marginTop = 8;
        toolbar.style.width = new Length(85, LengthUnit.Percent);
        toolbar.style.maxWidth = 400;
        toolbar.style.paddingLeft = toolbar.style.paddingRight = 12;
        toolbar.style.backgroundColor = new Color(.04f,.08f,.13f,.96f);
        actionEditor.Add(toolbar);
        var hint = new Label("Arraste um botão. Use o controle abaixo para mudar o tamanho.");
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.color = Color.white;
        toolbar.Add(hint);
        actionSize = new Slider(.05f,.7f);
        actionSize.RegisterValueChangedCallback(evt =>
        {
            actionPlacements[selectedAction].size = evt.newValue;
            ApplyActionPlacement(selectedAction);
            PositionActionHandles();
        });
        toolbar.Add(actionSize);
        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.flexWrap = Wrap.Wrap;
        buttons.Add(new Button(() => FinishActionEditor(false)) { text = "Cancelar" });
        buttons.Add(new Button(ResetActionLayout) { text = "Restaurar padrão" });
        buttons.Add(new Button(() => FinishActionEditor(true)) { text = "Salvar" });
        bool toolbarAtBottom = false;
        buttons.Add(new Button(() =>
        {
            toolbarAtBottom = !toolbarAtBottom;
            actionEditor.style.justifyContent = toolbarAtBottom ? Justify.FlexEnd : Justify.FlexStart;
        }) { text = "Mover painel" });
        toolbar.Add(buttons);
        root.Add(actionEditor);
        RefreshActionLayout();
        SelectAction(0);
    }

    void SelectAction(int index)
    {
        selectedAction = index;
        actionSize.label = ActionNames[index];
        actionSize.SetValueWithoutNotify(actionPlacements[index].size);
        PositionActionHandles();
    }

    void PositionActionHandles()
    {
        for (int i = 0; i < actionHandles.Length; i++)
        {
            var rect = ActionRect(actionPlacements[i]);
            var handle = actionHandles[i];
            handle.style.left = rect.x; handle.style.top = rect.y;
            handle.style.width = rect.width; handle.style.height = rect.height;
            Color edge = i == selectedAction ? new Color(1,.77f,.3f) : Color.white;
            handle.style.borderTopColor = handle.style.borderBottomColor =
                handle.style.borderLeftColor = handle.style.borderRightColor = edge;
        }
    }

    void FinishActionEditor(bool save)
    {
        actionEditor.RemoveFromHierarchy();
        actionEditor = null;
        if (save)
        {
            for (int i = 0; i < actionPlacements.Length; i++)
                PlayerPrefs.SetString(actionLayoutKey + "." + i, JsonUtility.ToJson(actionPlacements[i]));
            PlayerPrefs.Save();
        }
        actionLayoutKey = null;
        RefreshActionLayout();
        ClosePanel();
    }

    void ResetActionLayout()
    {
        for (int i = 0; i < actionPlacements.Length; i++)
            PlayerPrefs.DeleteKey(actionLayoutKey + "." + i);
        PlayerPrefs.Save();
        FinishActionEditor(false);
    }
}
