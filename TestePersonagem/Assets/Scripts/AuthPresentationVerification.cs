using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Presentation and invalid-input checks only. No valid credentials are ever submitted.
public sealed class AuthPresentationVerification : MonoBehaviour
{
    const string OutputFolder = @"C:\SistemaAdr\gta\tools\auth-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<Measurement> measurements = new List<Measurement>();
    AccountSession account;
    StartupLoader loader;
    VisualElement root;
    bool finished;

    [Serializable]
    sealed class Measurement
    {
        public string name;
        public float value;
    }

    [Serializable]
    sealed class Report
    {
        public string capturedUtc;
        public string unityVersion;
        public string graphicsDevice;
        public bool passed;
        public List<string> checks;
        public List<string> errors;
        public List<Measurement> measurements;
    }

    struct ImageStats
    {
        public float mean;
        public float variance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-auth-presentation") >= 0 &&
            !FindAnyObjectByType<AuthPresentationVerification>())
            new GameObject("Auth Presentation Verification").AddComponent<AuthPresentationVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        Directory.CreateDirectory(OutputFolder);
        Application.logMessageReceived += OnLog;
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + trace);
    }

    IEnumerator Start()
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            object instruction = null;
            bool moved = false;
            Exception failure = null;
            try
            {
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); Finish(); yield break; }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        if (!finished) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(3f);
        loader = FindAnyObjectByType<StartupLoader>();
        Check(StartupLoader.IsLoading && loader,
            "Tela de carregamento permanece ativa após três segundos");
        Check(!AccountSession.Instance || !AccountSession.Instance.AuthVisible,
            "Apresentação de login aguarda o carregamento inicial");
        CheckMusic("durante o carregamento inicial");
        Check(!AccountSession.Instance || AccountSession.Instance.RequestCount == 0,
            "Carregamento inicial não faz requisições de conta");
        yield return Capture("loading-1280x720");
        float deadline = Time.realtimeSinceStartup + 35;
        while ((!AccountSession.Instance || !AccountSession.Instance.AuthVisible) &&
               Time.realtimeSinceStartup < deadline) yield return null;
        account = AccountSession.Instance;
        if (!Check(account && account.AuthVisible && account.Document, "Interface de entrada aparece após o carregamento")) yield break;
        root = account.Document.rootVisualElement;
        loader = FindAnyObjectByType<StartupLoader>();
        yield return new WaitForSecondsRealtime(.5f);
        Check(Time.realtimeSinceStartup >= 9.5f, "Carregamento inicial respeita aproximadamente dez segundos");
        Check(account.RequestCount == 0 && !AccountSession.Authenticated,
            "Tela inicial não autentica nem faz requisições de conta");
        Check(IsVisible(root.Q("auth-landing")) && !IsVisible(root.Q("auth-form-panel")),
            "Entrada começa na apresentação com formulário oculto");
        Check(root.Q<Button>("auth-register-email") != null && root.Q<Button>("auth-login-email") != null &&
              root.Q<Button>("auth-facebook") != null && root.Q<Button>("auth-google") != null,
            "Apresentação oferece cadastro, login, Google e Facebook");
        ValidateArtwork("entrada inicial");
        CheckMusic("na entrada");

        foreach (string social in new[] { "auth-facebook", "auth-google" })
        {
            Button button = root.Q<Button>(social);
            if (button != null && button.enabledInHierarchy) Submit(button);
            yield return null;
            Check(IsVisible(root.Q("auth-landing")) && !IsVisible(root.Q("auth-form-panel")) &&
                  account.RequestCount == 0 && !AccountSession.Authenticated,
                "Atalho " + social + " permanece sem ação de autenticação");
        }

        Submit(RequiredButton("auth-register-email"));
        yield return new WaitForSecondsRealtime(.25f);
        Check(IsVisible(root.Q("auth-form-panel")) && !IsVisible(root.Q("auth-landing")),
            "Cadastrar por e-mail abre o formulário dedicado");
        Check(RegisterFieldsVisible(), "Cadastro exibe e-mail, senha e confirmação");
        Check(PasswordFieldsMasked(), "Senha e confirmação começam mascaradas");

        // All fields are invalid, so this remains safe even when more than one validator regresses.
        SetFields("", "", "");
        Submit(RequiredButton("auth-submit"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(HasFieldError("auth-email") && HasFieldError("auth-password") && HasFieldError("auth-confirm"),
            "Envio vazio mostra erros individuais em todos os campos obrigatórios");
        Check(HasMessage() && account.RequestCount == 0, "Campos vazios são bloqueados localmente sem POST");
        yield return Capture("validation-empty-fields");

        // Contract: formatted email, password >= 10 characters, matching confirmation.
        SetFields("invalido", "curta", "diferente");
        Submit(RequiredButton("auth-submit"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(HasFieldError("auth-email"), "E-mail sem formato válido recebe erro local");
        Check(HasFieldError("auth-password"), "Senha com menos de dez caracteres recebe erro local");
        Check(HasFieldError("auth-confirm"), "Confirmação diferente recebe erro local");
        Check(account.RequestCount == 0 && !AccountSession.Authenticated,
            "Validações de e-mail, senha e confirmação acontecem antes de qualquer requisição");

        SetFields("ui-presentation@example.invalid", "Password12345", "Password54321");
        Submit(RequiredButton("auth-submit"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(HasFieldError("auth-confirm") && account.RequestCount == 0 && !AccountSession.Authenticated,
            "Senhas válidas mas diferentes bloqueiam o cadastro localmente sem POST");
        yield return Capture("validation-password-mismatch");

        VisualElement showPassword = root.Q("auth-show-password");
        if (Check(showPassword != null, "Controle para mostrar e ocultar senha disponível"))
        {
            Activate(showPassword);
            yield return null;
            Check(!Field("auth-password").isPasswordField && !Field("auth-confirm").isPasswordField,
                "Mostrar senha revela senha e confirmação");
            Activate(showPassword);
            yield return null;
            Check(PasswordFieldsMasked(), "Ocultar senha volta a mascarar ambos os campos");
        }

        Submit(RequiredButton("auth-toggle"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(LoginFieldsVisible(), "Alternar para login exibe somente e-mail e senha");
        SetFields("", "", "");
        Submit(RequiredButton("auth-submit"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(HasFieldError("auth-email") && HasFieldError("auth-password") && account.RequestCount == 0,
            "Login vazio mostra erros de e-mail e senha sem requisição");
        Submit(RequiredButton("auth-toggle"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(RegisterFieldsVisible(), "Alternar novamente restaura os três campos de cadastro");
        Submit(RequiredButton("auth-back"));
        yield return new WaitForSecondsRealtime(.15f);
        Check(IsVisible(root.Q("auth-landing")) && !IsVisible(root.Q("auth-form-panel")),
            "Voltar retorna à apresentação inicial");

        while (Time.realtimeSinceStartup < 15.5f) yield return null;
        CheckMusic("depois de quinze segundos sem autenticar");

        foreach (Vector2Int resolution in new[]
        {
            new Vector2Int(1280, 720), new Vector2Int(960, 540),
            new Vector2Int(1600, 900), new Vector2Int(1024, 768)
        })
        {
            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
            deadline = Time.realtimeSinceStartup + 3;
            while ((Screen.width != resolution.x || Screen.height != resolution.y) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.35f);
            string suffix = resolution.x + "x" + resolution.y;
            Check(Screen.width == resolution.x && Screen.height == resolution.y, "Resolução aplicada: " + suffix);
            ValidateArtwork(suffix);
            Check(IsVisible(root.Q("auth-landing")), "Apresentação disponível em " + suffix);
            CheckWithinPanel(root.Q<Button>("auth-register-email"), "Cadastro na apresentação " + suffix);
            CheckWithinPanel(root.Q<Button>("auth-login-email"), "Login na apresentação " + suffix);
            yield return Capture("landing-" + suffix);

            Submit(RequiredButton("auth-register-email"));
            yield return new WaitForSecondsRealtime(.2f);
            SetFields("", "", "");
            ScrollToTop();
            yield return new WaitForSecondsRealtime(.2f);
            Check(RegisterFieldsVisible(), "Cadastro disponível em " + suffix);
            CheckFormViewport("cadastro " + suffix);
            yield return Capture("register-" + suffix);
            Submit(RequiredButton("auth-back"));
            yield return new WaitForSecondsRealtime(.15f);

            Submit(RequiredButton("auth-login-email"));
            yield return new WaitForSecondsRealtime(.2f);
            SetFields("", "", "");
            ScrollToTop();
            yield return new WaitForSecondsRealtime(.2f);
            Check(LoginFieldsVisible() && Field("auth-password").isPasswordField,
                "Login com senha mascarada disponível em " + suffix);
            CheckFormViewport("login " + suffix);
            yield return Capture("login-" + suffix);
            Submit(RequiredButton("auth-back"));
            yield return new WaitForSecondsRealtime(.15f);
        }

        CheckMusic("ao finalizar a navegação e os testes de resolução");
        Measure("accountRequestCount", account.RequestCount);
        Check(account.RequestCount == 0 && !AccountSession.Authenticated && account.AuthVisible,
            "Toda a verificação termina sem requisições de conta, autenticação ou salvamento de credenciais");
        Check(errors.Count == 0, "Execução sem erros, exceções ou asserts do Unity");
    }

    void ValidateArtwork(string state)
    {
        Image artwork = root.Q<Image>("auth-artwork");
        bool ready = artwork != null && artwork.image && IsVisible(artwork);
        Check(ready && loader && artwork.image == loader.loginArtwork && artwork.scaleMode == ScaleMode.ScaleAndCrop,
            "Imagem amigos preenche o fundo com ScaleAndCrop: " + state);
        if (!ready) return;
        Rect panel = root.worldBound;
        Rect bounds = artwork.worldBound;
        Check(panel.width > 500 && panel.height > 300 && Mathf.Abs(bounds.xMin - panel.xMin) < 2 &&
              Mathf.Abs(bounds.yMin - panel.yMin) < 2 && Mathf.Abs(bounds.width - panel.width) < 2 &&
              Mathf.Abs(bounds.height - panel.height) < 2,
            "Fundo cobre toda a área do painel: " + state);
        ImageStats stats = TextureStats(artwork.image);
        Check(stats.mean > .01f && stats.variance > .0001f, "Arte de fundo carregada contém cores: " + state);
    }

    void CheckMusic(string stage)
    {
        AudioSource music = loader ? loader.Music : null;
        Check(music && music.clip && music.isPlaying && music.volume > .01f && !music.mute &&
              !AudioListener.pause && AudioListener.volume > 0,
            "Música continua tocando " + stage);
        if (music) Measure("musicVolume " + stage, music.volume);
    }

    void CheckWithinPanel(VisualElement element, string description)
    {
        Rect panel = root.worldBound;
        Rect bounds = element != null ? element.worldBound : default;
        Check(IsVisible(element) && bounds.width > 30 && bounds.height > 15 &&
              bounds.xMin >= panel.xMin - 2 && bounds.yMin >= panel.yMin - 2 &&
              bounds.xMax <= panel.xMax + 2 && bounds.yMax <= panel.yMax + 2, description);
    }

    void CheckFormViewport(string state)
    {
        VisualElement form = root.Q("auth-form-panel");
        Rect panel = root.worldBound;
        Rect bounds = form != null ? form.worldBound : default;
        bool fitsWidth = bounds.width >= 260 && bounds.xMin >= panel.xMin - 2 && bounds.xMax <= panel.xMax + 2;
        ScrollView scroll = form as ScrollView ?? form?.Q<ScrollView>();
        bool verticalAccess = bounds.height <= panel.height + 2 || scroll != null;
        Check(IsVisible(form) && fitsWidth && verticalAccess, "Formulário tem largura acessível e rolagem quando necessário: " + state);
    }

    bool RegisterFieldsVisible()
    {
        return IsVisible(root.Q("auth-form-panel")) &&
               IsVisible(Field("auth-email")) && IsVisible(Field("auth-password")) && IsVisible(Field("auth-confirm"));
    }

    bool LoginFieldsVisible()
    {
        return IsVisible(root.Q("auth-form-panel")) &&
               IsVisible(Field("auth-email")) && IsVisible(Field("auth-password")) && !IsVisible(Field("auth-confirm"));
    }

    bool PasswordFieldsMasked()
    {
        return Field("auth-password") != null && Field("auth-confirm") != null &&
               Field("auth-password").isPasswordField && Field("auth-confirm").isPasswordField;
    }

    bool HasFieldError(string field)
    {
        Label label = root.Q<Label>(field + "-error");
        return IsVisible(label) && !string.IsNullOrWhiteSpace(label.text);
    }

    bool HasMessage()
    {
        Label label = root.Q<Label>("auth-message");
        return IsVisible(label) && !string.IsNullOrWhiteSpace(label.text);
    }

    static bool IsVisible(VisualElement element)
    {
        if (element == null || element.panel == null) return false;
        for (VisualElement current = element; current != null; current = current.parent)
            if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden ||
                current.resolvedStyle.opacity <= 0) return false;
        return true;
    }

    TextField Field(string name) { return root.Q<TextField>(name); }
    Button RequiredButton(string name)
    {
        Button button = root.Q<Button>(name);
        if (button == null) throw new InvalidOperationException("Botão não encontrado: " + name);
        return button;
    }

    void SetFields(string email, string password, string confirmation)
    {
        Field("auth-email").value = email;
        Field("auth-password").value = password;
        Field("auth-confirm").value = confirmation;
    }

    static void Submit(Button button)
    {
        if (!button.enabledInHierarchy) throw new InvalidOperationException("Botão desabilitado: " + button.name);
        button.Focus();
        using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
    }

    static void Activate(VisualElement control)
    {
        if (control is Button button) Submit(button);
        else if (control is Toggle toggle) toggle.value = !toggle.value;
        else throw new InvalidOperationException("Controle de senha precisa ser Button ou Toggle.");
    }

    void ScrollToTop()
    {
        VisualElement form = root.Q("auth-form-panel");
        ScrollView scroll = form as ScrollView ?? form?.Q<ScrollView>();
        if (scroll != null) scroll.scrollOffset = Vector2.zero;
        root.Focus();
    }

    ImageStats TextureStats(Texture source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(128, 128, TextureFormat.RGB24, false);
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            texture.Apply();
            return PixelStats(texture.GetPixels32());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Destroy(texture);
        }
    }

    static ImageStats PixelStats(Color32[] pixels)
    {
        double sum = 0, squareSum = 0;
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 29)
        {
            Color32 pixel = pixels[i];
            float value = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
            sum += value; squareSum += value * value; count++;
        }
        if (count == 0) return default;
        double mean = sum / count;
        return new ImageStats { mean = (float)mean, variance = (float)(squareSum / count - mean * mean) };
    }

    IEnumerator Capture(string name)
    {
        string path = Path.Combine(OutputFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 4;
        long previousLength = -1;
        int stableFrames = 0;
        while (stableFrames < 2 && Time.realtimeSinceStartup < deadline)
        {
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            stableFrames = length > 2000 && length == previousLength ? stableFrames + 1 : 0;
            previousLength = length;
            yield return null;
        }
        if (!Check(stableFrames >= 2, "Captura gravada: " + name)) yield break;
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool decoded = texture.LoadImage(File.ReadAllBytes(path));
        ImageStats stats = decoded ? PixelStats(texture.GetPixels32()) : default;
        Destroy(texture);
        Measure(name + "Mean", stats.mean);
        Check(decoded && stats.mean > .003f && stats.variance > .00005f,
            "Captura contém arte renderizada sem tela preta: " + name);
    }

    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }

    void Measure(string name, float value) { measurements.Add(new Measurement { name = name, value = value }); }

    void Finish()
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        var report = new Report
        {
            capturedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceName, passed = errors.Count == 0,
            checks = checks, errors = errors, measurements = measurements
        };
        File.WriteAllText(Path.Combine(OutputFolder, "validation.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(OutputFolder, "validation.txt"), string.Join("\n", checks) +
            "\nERRORS=" + errors.Count + "\n" + string.Join("\n", errors));
        // No PlayerPrefs writes, successful authentication or AccountSession.Save calls occur here.
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
