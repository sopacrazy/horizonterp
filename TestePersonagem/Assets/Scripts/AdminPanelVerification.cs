using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Offline fixture checks the read-only roster opened by /apanel.
internal static class AdminPanelVerification
{
    static readonly List<AdminPlayer> rows = new List<AdminPlayer>();
    static int mutations;
    static IEnumerator Reply(string method, string path, string json, Action<AdminReply> done)
    {
        yield return new WaitForSecondsRealtime(.05f);
        if (method != "GET") { mutations++; done(new AdminReply { code = "READ_ONLY", error = "Use o chat." }); yield break; }
        string query = "";
        foreach (var part in new Uri("https://fixture.invalid" + path).Query.TrimStart('?').Split('&'))
            if (part.StartsWith("q=")) query = Uri.UnescapeDataString(part.Substring(2));
        var found = rows.Where(p => string.IsNullOrEmpty(query) || p.uid == query ||
            p.firstName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        done(new AdminReply { players = found, total = found.Length, pageSize = 25,
            viewerAdminLevel = AccountSession.Instance.State.adminLevel });
    }
    static void Press(Button button)
    {
        button.Focus();
        using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
    }
    internal static IEnumerator Run(Func<bool, string, bool> check,
        Func<string, bool, IEnumerator> capture, Action<string, int> setRole)
    {
        var state = AccountSession.Instance.State;
        string oldRole = state.accountRole;
        int oldLevel = state.adminLevel;
        setRole("admin", 2);
        rows.Clear();
        mutations = 0;
        rows.Add(new AdminPlayer { uid = state.uid, firstName = state.firstName,
            adminLevel = 2, accountRole = "admin", isSelf = true });
        rows.Add(new AdminPlayer { uid = "42", firstName = "Lucas" });
        rows.Add(new AdminPlayer { uid = "43", firstName = "Marina" });
        AccountSession.AdminVerificationTransport = Reply;
        try
        {
            yield return null;
            AdminPlayerPanel.Open(GameHud.Instance);
            yield return new WaitForSecondsRealtime(.3f);
            var ui = GameHud.Instance.Root;
            check(AdminPlayerPanel.Instance && AdminPlayerPanel.Instance.IsOpen &&
                GameHud.Instance.IsPanelOpen, "/apanel abre a lista de jogadores");
            check(ui.Q("admin-row-42") != null && ui.Q("admin-row-43") != null,
                "Lista carrega jogadores da API simulada");
            check(ui.Q("admin-actions-card") == null && ui.Q("admin-command-preview") == null &&
                ui.Q<Button>("admin-role-42") == null && ui.Q<Button>("admin-block-43") == null,
                "Painel não oferece comandos nem ações administrativas");
            foreach (var resolution in new[] { new Vector2Int(960, 540),
                new Vector2Int(1280, 720), new Vector2Int(1600, 720) })
            {
                Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.5f);
                var panel = ui.Q(className: "admin-modal").worldBound;
                var bounds = ui.worldBound;
                check(panel.xMin >= bounds.xMin && panel.xMax <= bounds.xMax &&
                    panel.yMin >= bounds.yMin && panel.yMax <= bounds.yMax,
                    "Lista dentro da tela em " + resolution.x + "x" + resolution.y);
                yield return capture("admin-table-" + resolution.x + "x" + resolution.y, false);
            }
            var search = ui.Q<TextField>("admin-search");
            search.value = "43";
            yield return new WaitForSecondsRealtime(.55f);
            check(ui.Q("admin-row-43") != null && ui.Q("admin-row-42") == null,
                "Pesquisa por ID filtra a lista");
            check(mutations == 0, "Painel não enviou alterações");
            Press(ui.Q<Button>("admin-close"));
            yield return null;
            check(!AdminPlayerPanel.Instance.IsOpen && !GameHud.Instance.IsPanelOpen,
                "Fechar lista devolve controles do jogo");
        }
        finally
        {
            AdminPlayerPanel.Instance?.Close(true);
            AccountSession.AdminVerificationTransport = null;
            setRole(oldRole, oldLevel);
        }
    }
}