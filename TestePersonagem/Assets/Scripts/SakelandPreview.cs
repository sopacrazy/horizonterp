using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Local standalone scene. No account, checkpoint, or multiplayer transport leaves this process.
public sealed class SakelandPreview : MonoBehaviour
{
    public Transform[] walkCheckpoints = Array.Empty<Transform>();
    public Transform[] entryCheckpoints = Array.Empty<Transform>();
    public Transform[] supportAnchors = Array.Empty<Transform>();
    public Renderer sandRenderer;
    public Collider collisionObstacle;
    public Camera[] reviewCameras = Array.Empty<Camera>();
    public bool startAtNight;

    const string ReviewFolder = @"C:\SistemaAdr\gta\tools\sakeland\review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> captures = new List<string>();
    Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> localTransport;
    ThirdPersonPlayer player;
    CharacterController controller;
    CityDayNight lighting;
    bool verify, finished, capturing;
    int rejectedRequests;

    [Serializable]
    sealed class Report
    {
        public bool passed;
        public string capturedUtc;
        public List<string> checks, errors, captures;
        public int rejectedRequests, remoteRequests;
    }

    void Awake()
    {
        verify = Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-sakeland") >= 0;
        localTransport = RejectRequest;
        AccountSession.VerificationTransport = localTransport;
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
    }

    IEnumerator RejectRequest(string method,string path,string body,bool authenticated,Action<ApiReply,string> done)
    {
        rejectedRequests++;
        errors.Add("Requisição bloqueada no teste local: " + method + " " + path);
        done(null,"Cena Sakeland local, sem acesso à API.");
        yield break;
    }

    IEnumerator Start()
    {
        yield return null;
        player = FindAnyObjectByType<ThirdPersonPlayer>();
        controller = player ? player.GetComponent<CharacterController>() : null;
        lighting = FindAnyObjectByType<CityDayNight>();
        if (lighting) lighting.SetNight(startAtNight);
        if (!verify) yield break;
        Directory.CreateDirectory(ReviewFolder);
        var pending = new Stack<IEnumerator>(); pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            bool moved = false; object instruction = null; Exception failure = null;
            try { moved = pending.Peek().MoveNext(); if (moved) instruction = pending.Peek().Current; }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); Finish(); yield break; }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested); else yield return instruction;
        }
        Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(.4f);
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance,
            "Cena independente sem conta ou multiplayer");
        Check(AccountSession.VerificationTransport == localTransport,"Transporte online bloqueado localmente");
        if (!Check(player && controller && player.cameraTransform,"Personagem, controle e câmera presentes")) yield break;
        player.DemoMode = true; player.DemoInput = Vector2.zero; player.DemoRun = false;

        Bounds visualBounds = default; bool hasVisual = false;
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || !skin.sharedMesh) continue;
            var bounds = CharacterGeometry.GetWorldBounds(skin);
            if (!hasVisual) { visualBounds = bounds; hasVisual = true; } else visualBounds.Encapsulate(bounds);
        }
        Check(hasVisual && visualBounds.size.y >= 1.75f && visualBounds.size.y <= 1.86f,
            "Altura visual próxima de 1,80 m: " + visualBounds.size.y.ToString("F3"));
        Check(Mathf.Abs(controller.height-1.8f) < .04f,"Cápsula do personagem de 1,80 m");
        VerifyScenerySupport();
        VerifySandMaterial();

        Check(walkCheckpoints.Length >= 2,"Pelo menos dois pontos de percurso definidos");
        for (int i=0;i<walkCheckpoints.Length;i++)
            Check(walkCheckpoints[i] && TryFloor(walkCheckpoints[i].position,out _),"Piso caminhável no ponto " + i);
        yield return VerifyVisibleFootContact();
        if (walkCheckpoints.Length >= 2 && walkCheckpoints[0] && walkCheckpoints[1]) yield return Walk();
        yield return VerifyDeckEntry();
        if (collisionObstacle) yield return VerifyObstacle();
        else Check(false,"Obstáculo de colisão configurado");
        player.DemoInput = Vector2.zero;
        player.ResetPosition();
        yield return new WaitForSecondsRealtime(.3f);

        if (Check(lighting,"Ciclo de iluminação presente"))
        {
            Check(reviewCameras.Length > 0,"Câmeras de revisão configuradas");
            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            var displays = new List<StyleEnum<DisplayStyle>>();
            foreach (var document in documents)
            { displays.Add(document.rootVisualElement.style.display); document.rootVisualElement.style.display = DisplayStyle.None; }
            capturing = true;
            try
            {
                foreach (bool night in new[] { false,true })
                {
                    lighting.SetNight(night); yield return new WaitForSecondsRealtime(.3f);
                    Check(lighting.IsNight == night,"Alternância para " + (night?"noite":"dia"));
                    for (int i=0;i<reviewCameras.Length;i++)
                        if (reviewCameras[i]) Capture(reviewCameras[i],"view-" + i + (night?"-night":"-day"));
                        else Check(false,"Câmera de revisão ausente: " + i);
                }
            }
            finally
            {
                capturing = false; lighting.SetNight(startAtNight);
                for (int i=0;i<documents.Length;i++) if (documents[i]) documents[i].rootVisualElement.style.display = displays[i];
            }
        }
        Check(!AccountSession.Instance && !MultiplayerWorld.Instance && rejectedRequests==0,
            "Teste encerrado sem conta, multiplayer ou requisições de rede");
    }

    bool TryFloor(Vector3 point,out RaycastHit floor)
    {
        var hits = Physics.RaycastAll(point+Vector3.up*2.5f,Vector3.down,6,~0,QueryTriggerInteraction.Ignore);
        Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (!hit.collider.transform.IsChildOf(player.transform) && hit.normal.y >= .65f && Mathf.Abs(hit.point.y-point.y) <= 1.5f)
            { floor=hit; return true; }
        floor=default; return false;
    }

    void VerifyScenerySupport()
    {
        var failures = new List<string>(); float maxGap = 0;
        if (supportAnchors == null || supportAnchors.Length == 0)
        { Check(false,"Pontos de apoio do cenário configurados"); return; }
        Physics.SyncTransforms();
        foreach (var anchor in supportAnchors)
        {
            if (!anchor || !anchor.parent) { failures.Add("Ponto de apoio ausente ou sem objeto pai"); continue; }
            var hits = Physics.RaycastAll(anchor.position+Vector3.up*.08f,Vector3.down,.25f,1,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            bool supported = false, measured = false;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(anchor.parent) || hit.normal.y <= .6f) continue;
                measured = true;
                float gap = anchor.position.y-hit.point.y; maxGap = Mathf.Max(maxGap,Mathf.Abs(gap));
                supported = Mathf.Abs(gap) <= .04f;
                if (!supported) failures.Add(anchor.parent.name+" em "+anchor.position+": folga="+gap.ToString("F4")+" m");
                break;
            }
            if (!measured)
                failures.Add(anchor.parent.name+" em "+anchor.position+": piso externo ausente sob a base");
        }
        Check(failures.Count == 0,"Objetos apoiados no chão: "+supportAnchors.Length+" bases, folga máxima="+maxGap.ToString("F4")+" m");
        foreach (string failure in failures) errors.Add("Apoio do cenário: "+failure);
    }

    void VerifySandMaterial()
    {
        var material = sandRenderer ? sandRenderer.sharedMaterial : null;
        bool textured = material && material.shader && material.shader.name == "Standard" && material.mainTexture &&
            material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") &&
            material.mainTextureScale.x >= 20 && material.mainTextureScale.y >= 20;
        Check(textured,"Areia tem textura repetida, mapa normal e material Standard");
    }

    IEnumerator VerifyVisibleFootContact()
    {
        for (int i=0;i<walkCheckpoints.Length;i++)
        {
            if (!walkCheckpoints[i] || !TryFloor(walkCheckpoints[i].position,out _)) continue;
            TeleportToFloor(walkCheckpoints[i].position);
            player.DemoInput = Vector2.zero;
            yield return new WaitForSecondsRealtime(.35f);
            float minimum = float.PositiveInfinity,maximum = float.NegativeInfinity;
            int samples = 0; bool supported = true;
            float deadline = Time.realtimeSinceStartup+1;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool visible = false; float lowest = float.PositiveInfinity;
                foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (skin.enabled && skin.sharedMesh)
                    { lowest = Mathf.Min(lowest,CharacterGeometry.GetWorldBounds(skin).min.y); visible = true; }
                if (visible && TryFloor(player.transform.position,out var floor))
                {
                    float gap = lowest-floor.point.y;
                    minimum = Mathf.Min(minimum,gap); maximum = Mathf.Max(maximum,gap);
                    supported &= gap >= -.035f && gap <= .025f && controller.isGrounded;
                    samples++;
                }
                else supported = false;
                yield return new WaitForSecondsRealtime(.05f);
            }
            Check(supported && samples >= 3,"Pés visíveis apoiados no ponto "+i+" durante 1 s de repouso: folga="+
                (samples > 0 ? minimum.ToString("F4")+" a "+maximum.ToString("F4") : "sem amostras")+" m");
        }
    }

    void TeleportToFloor(Vector3 point)
    {
        if (!TryFloor(point,out var floor)) throw new InvalidOperationException("Piso ausente em " + point);
        controller.enabled = false;
        player.transform.position = floor.point+Vector3.up*(.04f-controller.center.y+controller.height*.5f);
        controller.enabled = true; Physics.SyncTransforms();
    }

    IEnumerator Walk()
    {
        TeleportToFloor(walkCheckpoints[0].position);
        yield return new WaitForSecondsRealtime(.3f);
        Vector3 before = player.transform.position;
        Vector3 target = walkCheckpoints[1].position;
        float initialDistance = Vector3.ProjectOnPlane(target-before,Vector3.up).magnitude;
        if (!Check(initialDistance >= .75f && initialDistance <= 6,"Trecho de caminhada curto de 0,75 a 6 m")) yield break;
        float deadline = Time.realtimeSinceStartup+initialDistance/Mathf.Max(.2f,player.walkSpeed)+2;
        while (Time.realtimeSinceStartup < deadline)
        {
            Vector3 direction = Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up);
            if (direction.magnitude < .25f) break;
            direction.Normalize();
            Vector3 forward = Vector3.ProjectOnPlane(player.cameraTransform.forward,Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(player.cameraTransform.right,Vector3.up).normalized;
            player.DemoInput = new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward));
            yield return null;
        }
        player.DemoInput = Vector2.zero;
        yield return new WaitForSecondsRealtime(.2f);
        Check(Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).magnitude < .4f,
            "Personagem atravessa o trecho caminhando com o controlador real");
        Check(Mathf.Abs(player.transform.position.y-before.y) < .8f && controller.isGrounded,
            "Caminhada permanece apoiada sem queda");
    }

    IEnumerator VerifyDeckEntry()
    {
        const string description = "Entrada da areia ao deck caminhável sem salto";
        if (entryCheckpoints == null || entryCheckpoints.Length < 2 || !entryCheckpoints[0] ||
            !TryFloor(entryCheckpoints[0].position,out _))
        { Check(false,description+": percurso de entrada ausente"); yield break; }
        player.DemoInput = Vector2.zero;
        TeleportToFloor(entryCheckpoints[0].position);
        yield return new WaitForSecondsRealtime(.3f);
        string failure = null;
        for (int i=1;i<entryCheckpoints.Length;i++)
        {
            if (!entryCheckpoints[i]) { failure="ponto "+i+" ausente"; break; }
            Vector3 target=entryCheckpoints[i].position;
            float distance=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).magnitude;
            float deadline=Time.realtimeSinceStartup+distance/Mathf.Max(.2f,player.walkSpeed)+1.2f;
            bool jumped=false;
            while (Time.realtimeSinceStartup < deadline)
            {
                jumped |= player.IsJumping;
                Vector3 direction=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up);
                if (direction.magnitude <= .18f) break;
                direction.Normalize();
                Vector3 forward=Vector3.ProjectOnPlane(player.cameraTransform.forward,Vector3.up).normalized;
                Vector3 right=Vector3.ProjectOnPlane(player.cameraTransform.right,Vector3.up).normalized;
                player.DemoInput=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward));
                yield return null;
            }
            player.DemoInput=Vector2.zero;
            yield return new WaitForSecondsRealtime(.2f);
            float remaining=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).magnitude;
            float heightError=Mathf.Abs(player.transform.position.y-target.y);
            if (remaining > .18f || heightError > .15f || !controller.isGrounded || jumped)
            {
                failure="ponto "+i+", distância="+remaining.ToString("F3")+" m, desnível="+heightError.ToString("F3")+
                    " m, apoiado="+controller.isGrounded+", salto="+jumped;
                break;
            }
        }
        player.DemoInput=Vector2.zero;
        Check(failure == null,description+(failure == null ? " ("+entryCheckpoints.Length+" pontos)" : ": "+failure));
    }

    IEnumerator VerifyObstacle()
    {
        if (!Check(collisionObstacle.enabled && !collisionObstacle.isTrigger,"Obstáculo físico sólido habilitado")) yield break;
        var bounds = collisionObstacle.bounds;
        Vector3 direction = bounds.size.x <= bounds.size.z ? Vector3.right : Vector3.forward;
        float extent = bounds.size.x <= bounds.size.z ? bounds.extents.x : bounds.extents.z;
        Vector3 start = bounds.center-direction*(extent+controller.radius+.55f);
        start.y = bounds.min.y;
        if (!Check(TryFloor(start,out _),"Piso de aproximação ao obstáculo")) yield break;
        TeleportToFloor(start); yield return new WaitForSecondsRealtime(.2f);
        Vector3 before = player.transform.position; float attempted=0; bool sideHit=false;
        float deadline = Time.realtimeSinceStartup+.65f;
        while (Time.realtimeSinceStartup < deadline)
        {
            float step = Mathf.Min(Time.unscaledDeltaTime,.05f)*3;
            attempted += step;
            var flags = controller.Move(direction*step+Vector3.down*.015f);
            sideHit |= (flags & CollisionFlags.Sides) != 0;
            yield return null;
        }
        float advanced = Vector3.Dot(player.transform.position-before,direction);
        Check(sideHit && advanced < attempted*.8f,"Colisão bloqueia a cápsula ao avançar contra o obstáculo");
    }

    void Capture(Camera camera,string name)
    {
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var rt = new RenderTexture(1280,720,24); var texture = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply();
            string path=Path.Combine(ReviewFolder,name+".png"); File.WriteAllBytes(path,texture.EncodeToPNG()); captures.Add(path);
            var pixels=texture.GetPixels32(); double sum=0,square=0; int samples=0;
            for (int i=0;i<pixels.Length;i+=31)
            { double value=(pixels[i].r+pixels[i].g+pixels[i].b)/(3.0*255); sum+=value; square+=value*value; samples++; }
            double mean=sum/samples,variance=square/samples-mean*mean;
            Check(mean>.005 && variance>.00001,"Captura renderizada: " + name + " (média=" + mean.ToString("F3") + ")");
        }
        finally
        {
            camera.targetTexture=previousTarget; RenderTexture.active=previousActive;
            rt.Release(); Destroy(rt); Destroy(texture);
        }
    }

    bool Check(bool passed,string description)
    { checks.Add((passed?"OK: ":"FAIL: ")+description); if (!passed) errors.Add(description); return passed; }

    void OnLog(string message,string trace,LogType type)
    { if (verify && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert)) errors.Add(message+"\n"+trace); }

    void Finish()
    {
        if (finished) return; finished=true;
        WriteReport();
        Debug.Log("SAKELAND_VERIFICATION " + (errors.Count==0?"PASS":"FAIL") + " checks="+checks.Count+" errors="+errors.Count);
        Application.Quit(errors.Count==0?0:1);
    }

    void WriteReport()
    {
        var report = new Report { passed=errors.Count==0,capturedUtc=DateTime.UtcNow.ToString("o"),
            checks=checks,errors=errors,captures=captures,rejectedRequests=rejectedRequests,remoteRequests=0 };
        Directory.CreateDirectory(ReviewFolder);
        File.WriteAllText(Path.Combine(ReviewFolder,"validation.json"),JsonUtility.ToJson(report,true));
    }

    void OnApplicationQuit()
    {
        if (!verify || finished) return;
        errors.Add("Verificação interrompida antes de concluir.");
        WriteReport();
    }

    void OnGUI()
    {
        if (capturing) return;
        GUI.depth=-5000; GUI.color=new Color(0,0,0,.82f); GUI.DrawTexture(new Rect(10,10,490,32),Texture2D.whiteTexture);
        GUI.color=new Color(1,.86f,.34f); GUI.Label(new Rect(20,17,480,25),"SAKELAND · TESTE LOCAL · SEM SALVAMENTO ONLINE"); GUI.color=Color.white;
    }

    void OnDestroy()
    {
        Application.logMessageReceived-=OnLog;
        if (AccountSession.VerificationTransport==localTransport) AccountSession.VerificationTransport=null;
    }
}
