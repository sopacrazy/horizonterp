using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    VisualElement entryScreen;
    bool welcomeAccepted;

    IEnumerator WelcomeToBeta()
    {
        if (VerificationTransport != null) yield break;
        welcomeAccepted = false;
        var screen = EntryScreen("beta-welcome");
        var scroll = new ScrollView();
        scroll.AddToClassList("entry-welcome");
        screen.Add(scroll);
        scroll.Add(HorizonteEntryVisuals.Image(HorizonteEntryVisuals.Logo, "entry-small-logo"));
        LabelAt("VERSÃO BETA", "entry-kicker", scroll);
        LabelAt("BEM-VINDO AO BETA", "entry-title", scroll);
        var grid = new VisualElement();
        grid.AddToClassList("entry-notes"); scroll.Add(grid);
        AddNotice(grid, "settings", "JOGO EM DESENVOLVIMENTO", "Sistemas e interfaces podem mudar durante os testes.");
        AddNotice(grid, "chat", "CONTE COMO FOI", "Ao encontrar um erro, descreva o que aconteceu para a equipe.");
        AddNotice(grid, "clock", "O PROGRESSO PODE MUDAR", "Atualizações do beta podem alterar dados do personagem.");
        AddNotice(grid, "safe", "RECURSOS EM TESTE", "Algumas funções podem ficar temporariamente indisponíveis.");
        AddNotice(grid, "users", "A CIDADE TAMBÉM ESTÁ EM TESTE", "A equipe acompanha como o jogo se comporta com mais jogadores.");
        LabelAt("IMPORTANTE   ·   O acesso ao beta é liberado pela equipe.", "entry-important", scroll);
        var betaContinue = EntryButton("beta-continue", "CONTINUAR   »", () =>
        {
            welcomeAccepted = true;
            HideEntryScreen();
        }, scroll);
        betaContinue.schedule.Execute(() => betaContinue.Focus());
        while (!welcomeAccepted) yield return null;
    }

    void AddNotice(VisualElement grid, string icon, string title, string detail)
    {
        var item = new VisualElement(); item.AddToClassList("entry-note"); grid.Add(item);
        item.Add(HorizonteEntryVisuals.Image(Resources.Load<Texture2D>("HudMinimal/" + icon), "entry-icon"));
        var words = new VisualElement(); words.AddToClassList("entry-note-copy"); item.Add(words);
        LabelAt(title, "entry-note-title", words);
        LabelAt(detail, "entry-note-detail", words);
    }

    VisualElement EntryScreen(string name)
    {
        HideEntryScreen(); HideRemembered(); HideCharacterLobby();
        landing.style.display = DisplayStyle.None; scroll.style.display = DisplayStyle.None;
        AuthVisible = true; ReadyToEnterCity = false; root.style.display = DisplayStyle.Flex;
        entryScreen = new VisualElement { name = name };
        entryScreen.AddToClassList("entry-screen");
        root.Add(entryScreen);
        return entryScreen;
    }

    void HideEntryScreen()
    {
        entryScreen?.RemoveFromHierarchy();
        entryScreen = null;
    }

    void ShowCitySelection()
    {
        if (VerificationTransport != null)
        {
            ShowAccountCharacter();
            return;
        }
        BuildCitySelection(EntryScreen("city-selection"), State?.uid, () =>
        {
            if (busy || !Authenticated) return;
            HideEntryScreen();
            ShowAccountCharacter();
        }, Logout);
    }

    internal static void BuildCitySelection(VisualElement screen, string uid, System.Action enter, System.Action logout)
    {
        screen.AddToClassList("entry-server-screen");
        var header = new VisualElement(); header.AddToClassList("entry-city-header"); screen.Add(header);
        var exit = new Button(logout) { name = "city-logout", text = "‹  TROCAR CONTA" };
        exit.AddToClassList("entry-secondary"); header.Add(exit);
        header.Add(HorizonteEntryVisuals.Image(HorizonteEntryVisuals.Logo, "entry-small-logo"));
        LabelAt(string.IsNullOrEmpty(uid) ? "CONTA CONECTADA" : "UID  " + uid, "entry-account", header);
        var browser = new ScrollView(ScrollViewMode.Vertical); browser.AddToClassList("entry-server-browser"); screen.Add(browser);
        LabelAt("CENTRAL DE SERVIDORES", "entry-kicker", browser);
        LabelAt("ESCOLHA A SUA CIDADE", "entry-title", browser);
        LabelAt("Escolha um servidor para entrar na cidade.", "entry-note-detail", browser);
        var countrySelect = new Button { name = "city-country-select" };
        countrySelect.AddToClassList("entry-country-select"); browser.Add(countrySelect);
        var selectedFlag = HorizonteEntryVisuals.Image(Resources.Load<Texture2D>("Startup/FlagBrazil"), "entry-country-flag"); countrySelect.Add(selectedFlag);
        var selectedName = LabelAt("Brasil", "entry-country-name", countrySelect);
        LabelAt("▾", "entry-country-arrow", countrySelect);
        var countries = new VisualElement(); countries.AddToClassList("entry-countries"); browser.Add(countries);
        countries.style.display = DisplayStyle.None;
        var brazil = CountryButton(countries, "br", "Brasil", "FlagBrazil");
        var spain = CountryButton(countries, "es", "Espanha", "FlagSpain", comingSoon: true);
        var unitedStates = CountryButton(countries, "us", "Estados Unidos", "FlagUnitedStates", comingSoon: true);

        var servers = new VisualElement(); servers.AddToClassList("entry-servers"); browser.Add(servers);
        servers.style.display = DisplayStyle.Flex;
        ServerCard(servers, "01", "Horizonte Brasil", "Sua cidade de RP", "ServidorHorizonte", true);
        ServerCard(servers, "02", "Próxima cidade", "Ainda não disponível para jogar", "ServidorEmBreve", false);
        var footer = new VisualElement(); footer.AddToClassList("entry-city-footer"); screen.Add(footer);
        var selection = new VisualElement(); selection.AddToClassList("entry-city-selection"); footer.Add(selection);
        var selectionTitle = LabelAt("PAÍS", "entry-kicker", selection);
        var selectionDetail = LabelAt("Selecione a bandeira acima.", "entry-note-detail", selection);
        var dock = new VisualElement(); dock.AddToClassList("entry-city-dock"); footer.Add(dock);
        Button selectedCountry = brazil;
        brazil.AddToClassList("entry-country-selected");
        countrySelect.clicked += () => countries.style.display = countries.style.display.value == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        var cityPlay = EntryButton("city-select-enter", "ENTRAR NA CIDADE  ›", () =>
        {
            enter();
        }, dock);
        cityPlay.SetEnabled(true);
        void SelectCountry(Button country, string detail)
        {
            selectedCountry?.RemoveFromClassList("entry-country-selected");
            selectedCountry = country;
            selectedCountry.AddToClassList("entry-country-selected");
            bool available = country == brazil;
            selectedFlag.image = Resources.Load<Texture2D>("Startup/" + (country == brazil ? "FlagBrazil" : country == spain ? "FlagSpain" : "FlagUnitedStates"));
            selectedName.text = available ? "Brasil" : country == spain ? "Espanha · em breve" : "Estados Unidos · em breve";
            countries.style.display = DisplayStyle.None;
            servers.style.display = DisplayStyle.Flex;
            selectionTitle.text = available ? "CIDADE SELECIONADA" : "PAÍS EM PREPARAÇÃO";
            selectionDetail.text = detail;
            cityPlay.SetEnabled(true);
        }
        brazil.clicked += () => SelectCountry(brazil, "Brasil  /  Horizonte Brasil  /  Servidor 01");
        spain.clicked += () => SelectCountry(spain, "Servidores da Espanha em breve.");
        unitedStates.clicked += () => SelectCountry(unitedStates, "Servidores dos Estados Unidos em breve.");
        selectionTitle.text = "CIDADE SELECIONADA";
        selectionDetail.text = "Brasil  /  Horizonte Brasil  /  Servidor 01";
    }

    static Button CountryButton(VisualElement parent, string code, string label, string flag, bool comingSoon = false)
    {
        var button = new Button { name = "city-country-" + code, tooltip = comingSoon ? label + " · Em breve" : label };
        button.AddToClassList("entry-country"); parent.Add(button);
        button.Add(HorizonteEntryVisuals.Image(Resources.Load<Texture2D>("Startup/" + flag), "entry-country-flag"));
        LabelAt(label, "entry-country-name", button);
        if (comingSoon) LabelAt("EM BREVE", "entry-country-status", button);
        return button;
    }

    static void ServerCard(VisualElement parent, string number, string title, string detail, string artwork, bool active)
    {
        var card = new VisualElement { name = "city-server-" + number };
        card.AddToClassList("entry-city-card");
        card.AddToClassList(active ? "entry-server-active" : "entry-server-locked");
        parent.Add(card);
        var cover = new VisualElement(); cover.AddToClassList("entry-server-cover"); card.Add(cover);
        var image = new Image { image = Resources.Load<Texture2D>("Startup/" + artwork), scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
        image.AddToClassList("entry-server-art"); cover.Add(image);
        var overlay = new VisualElement(); overlay.AddToClassList("entry-server-overlay"); cover.Add(overlay);
        LabelAt("SERVIDOR  " + number, "entry-server-number", overlay);
        LabelAt(active ? "●  SELECIONADO" : "EM BREVE", "entry-server-status", overlay);
        var copy = new VisualElement(); copy.AddToClassList("entry-server-copy"); card.Add(copy);
        LabelAt(title, "entry-city-name", copy);
        LabelAt(detail, "entry-note-detail", copy);
    }

    static Button EntryButton(string name, string text, System.Action action, VisualElement parent)
    {
        var button = new Button(action) { name = name, text = text };
        button.AddToClassList("entry-action"); parent.Add(button);
        return button;
    }

    static string EntryMoney(string cents) => long.TryParse(cents, out long amount)
        ? (amount / 100m).ToString("C2", CultureInfo.GetCultureInfo("pt-BR")) : "—";
}
