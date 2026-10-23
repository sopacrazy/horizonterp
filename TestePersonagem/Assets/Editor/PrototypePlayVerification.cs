using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PrototypePlayVerification
{
    static double stageStart;
    static int stage;
    static Vector3 startPosition;
    static ThirdPersonPlayer player;
    static readonly List<string> results = new List<string>();
    static readonly List<string> errors = new List<string>();
    const string ActiveKey = "PrototypePlayVerification.Active";

    static PrototypePlayVerification()
    {
        EditorApplication.playModeStateChanged += OnPlayState;
        if (SessionState.GetBool(ActiveKey, false)) Application.logMessageReceived += OnLog;
    }

    public static void Start()
    {
        SessionState.SetBool(ActiveKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");
        // Give the Editor its startup frames to create the default Search index before Play.
        // Entering Play immediately in -executeMethod skips that initialization in Unity 6.6.
        stageStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += WaitForEditor;
    }

    static void WaitForEditor()
    {
        if (EditorApplication.timeSinceStartup - stageStart < 3 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitForEditor;
        EditorApplication.isPlaying = true;
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            player = UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>();
            player.DemoMode = true;
            stage = 0; stageStart = EditorApplication.timeSinceStartup;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        results.Add("OK: " + message);
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || !player) return;
            double elapsed = EditorApplication.timeSinceStartup - stageStart;
            if (elapsed < (stage == 0 ? 1.5 : stage == 3 ? 1 : 2)) return;
            if (stage == 0)
            {
                Require(player.CurrentSpeed < .1f, "Personagem parado sem entrada");
                Require(player.GetComponent<CharacterController>().isGrounded, "Personagem apoiado no piso");
                startPosition = player.transform.position;
                player.DemoInput = Vector2.up;
            }
            else if (stage == 1)
            {
                Require(Vector3.Distance(player.transform.position, startPosition) > 1.5f, "Caminhada desloca personagem");
                Require(Mathf.Abs(player.CurrentSpeed - player.walkSpeed) < .35f, "Velocidade de caminhada correta");
                Capture("gameplay-andar");
                startPosition = player.transform.position; player.DemoRun = true;
            }
            else if (stage == 2)
            {
                Require(Vector3.Distance(player.transform.position, startPosition) > 4f, "Corrida desloca mais que caminhada");
                Require(Mathf.Abs(player.CurrentSpeed - player.runSpeed) < .5f, "Velocidade de corrida correta");
                Capture("gameplay-correr"); player.DemoInput = Vector2.zero;
            }
            else
            {
                Require(player.CurrentSpeed < .1f, "Soltar entrada volta a parar");
                Require(player.animator.GetFloat("Speed") < .15f, "Animator retorna ao estado parado");
                Require(errors.Count == 0, "Sem erros durante Play Mode");
                Capture("gameplay-parado");
                Finish(0); return;
            }
            stage++; stageStart = EditorApplication.timeSinceStartup;
        }
        catch (Exception ex)
        {
            errors.Add(ex.ToString()); Finish(1);
        }
    }

    static void Capture(string filename)
    {
        var camera = Camera.main;
        var rt = new RenderTexture(1280, 720, 24);
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        File.WriteAllBytes("../tools/" + filename + ".png", texture.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(ActiveKey, false);
        File.WriteAllText("../tools/unity-play-validation.txt", string.Join("\n", results) + "\n" + string.Join("\n", errors));
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
