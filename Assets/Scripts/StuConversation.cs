// StuConversation.cs — Simmi's lane: CHARACTER COMMUNICATION (HackGT 13)
// Wraps the ElevenLabs Agents SDK for Unity (io.elevenlabs.agents).
//
// What it does:
//   - Starts/stops a voice conversation with the Stu agent (mic -> STT -> LLM -> TTS
//     is ALL handled inside the SDK; you don't wire Whisper/Gemini/TTS separately).
//   - Lets the agent drive Stu's ACTIONS through CLIENT TOOLS:
//       spawn_diagram {diagram_key} -> DiagramManager spawns the AR diagram (XR lane)
//       point_at      {target}      -> Stu points at a part of the diagram
//       set_mood      {mood}        -> idle-working | nudge | curious | happy
//   - Fires C# events the Stu state machine / UI can subscribe to.
//
// DASHBOARD SETUP (ElevenLabs dashboard -> your Stu agent):
//   1. Create the agent. System prompt suggestion:
//      "You are Stu, a friendly AR study companion for a student doing homework.
//       Give SHORT Socratic hints (1-2 sentences), NEVER the full answer.
//       The student is working on: {{question_text}}.
//       When a concept has a visual, call spawn_diagram with the matching key,
//       then use point_at to highlight parts as you explain.
//       Available diagram keys: solar_system, plant_life_cycle,
//       butterfly_life_cycle, division, fraction_map."
//   2. Add these 3 CLIENT tools (same names as below):
//       spawn_diagram | "Show the AR diagram for a topic" | param: diagram_key (string, required)
//       point_at      | "Point at a part of the current diagram" | param: target (string, required)
//       set_mood      | "Change Stu's mood/animation" | param: mood (string: idle-working, nudge, curious, happy)
//   3. Security -> Authentication: OFF (public agent, simplest for the demo).
//   4. Copy the Agent ID into the inspector field below.
//
// UNITY SETUP:
//   1. Empty GameObject "StuConversation" + this script (+ optional AudioSource on Stu's head).
//   2. XR lane: subscribe to OnSpawnDiagram / OnPointAt / OnSetMood and route to
//      DiagramManager / Stu's animator. Example:
//        StuConversation.Instance.OnSpawnDiagram += key => DiagramManager.Instance.Show(key);
//   3. Sensing lane: call StartConversation(questionText, diagramKey) when the state
//      machine enters ASKING/CONVERSATION; call EndConversation() when done.
//   4. demoMode = true lets every lane integrate with zero network.

using System;
using System.Collections.Generic;
using UnityEngine;
using ElevenLabs.Agents;
using ElevenLabs.Protocol;
using Newtonsoft.Json;

public class StuConversation : MonoBehaviour
{
    public static StuConversation Instance;

    [Header("Agent")]
    [Tooltip("ElevenLabs dashboard -> Agents -> Stu -> Agent ID")]
    public string agentId = "PASTE_AGENT_ID_HERE";

    [Header("Audio")]
    [Tooltip("Optional: put on Stu's head so his voice is spatial.")]
    public AudioSource agentAudioSource;

    [Header("Demo")]
    [Tooltip("ON = no network. Simulates Stu talking so other lanes can integrate.")]
    public bool demoMode = true;
    [Tooltip("If ON, pressing Play starts a fake conversation automatically (for solo testing).")]
    public bool autoStartDemoOnPlay = true;
    [Tooltip("Question used for the auto demo.")]
    public string demoQuestion = "Solve 2x + 5 = 17";
    [Tooltip("Diagram key used for the auto demo.")]
    public string demoDiagramKey = "division";

    public Conversation ActiveConversation { get; private set; }
    bool demoActive;
    public bool IsInConversation => ActiveConversation != null || demoActive;

    // ---- Events for the Stu state machine / UI ----
    public event Action OnConversationStarted;
    public event Action OnConversationEnded;
    public event Action<string> OnUserSaid;   // student speech-to-text
    public event Action<string> OnStuSaid;    // Stu's reply text
    public event Action<bool> OnStuSpeaking;  // true = Stu has the audio floor

    // ---- Action requests from the agent (XR lane subscribes) ----
    public event Action<string> OnSpawnDiagram; // diagram_key
    public event Action<string> OnPointAt;      // target label
    public event Action<string> OnSetMood;     // idle-working | nudge | curious | happy

    // Tool calls can arrive off the main thread -> marshal to Update().
    readonly Queue<Action> mainThreadQueue = new Queue<Action>();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    System.Collections.IEnumerator Start()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

        // Solo testing: auto-start a fake conversation on Play.
        // In the real app the Sensing lane calls StartConversation() instead.
        if (demoMode && autoStartDemoOnPlay)
            StartConversation(demoQuestion, demoDiagramKey);
    }

    void Update()
    {
        while (mainThreadQueue.Count > 0)
        {
            var a = mainThreadQueue.Dequeue();
            try { a(); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    void Enqueue(Action a) { lock (mainThreadQueue) mainThreadQueue.Enqueue(a); }

    // ---------------- Public API ----------------

    /// <summary>Start talking to Stu about a question. diagramKey comes from ProblemManager.</summary>
    public async void StartConversation(string questionText, string diagramKey)
    {
        if (IsInConversation) return;

        if (demoMode) { StartCoroutine(DemoConversation(questionText)); return; }

        if (string.IsNullOrWhiteSpace(agentId) || agentId.Contains("PASTE"))
        {
            Debug.LogError("[Stu] Set your agent ID in the inspector (or enable demoMode).");
            return;
        }

        try
        {
            var options = new ConversationOptions
            {
                AgentId = agentId.Trim(),
                OutputAudioSource = agentAudioSource,
                DynamicVariables = new Dictionary<string, object>
                {
                    ["question_text"] = questionText ?? "",
                    ["diagram_key"] = diagramKey ?? "",
                },
                Overrides = new ConversationConfigOverride
                {
                    Agent = new ConversationConfigOverrideAgent
                    {
                        FirstMessage = $"Hey! I see you're working on this: {questionText}. Want a hint, or should we talk it through?"
                    }
                }
            };

            ActiveConversation = await Conversation.StartSessionAsync(options);

            // Client tools: the agent invokes these, we forward to the game on the main thread.
            ActiveConversation.RegisterTool<SpawnDiagramParams, ToolResult>("spawn_diagram", p =>
            {
                Enqueue(() => OnSpawnDiagram?.Invoke(p.diagram_key));
                return new ToolResult { ok = true, message = "Showing diagram " + p.diagram_key };
            });
            ActiveConversation.RegisterTool<PointAtParams, ToolResult>("point_at", p =>
            {
                Enqueue(() => OnPointAt?.Invoke(p.target));
                return new ToolResult { ok = true, message = "Pointing at " + p.target };
            });
            ActiveConversation.RegisterTool<SetMoodParams, ToolResult>("set_mood", p =>
            {
                Enqueue(() => OnSetMood?.Invoke(p.mood));
                return new ToolResult { ok = true, message = "Mood: " + p.mood };
            });

            ActiveConversation.ModeChanged += mode => Enqueue(() => OnStuSpeaking?.Invoke(mode == Mode.Speaking));
            ActiveConversation.UserTranscriptReceived += args => Enqueue(() => OnUserSaid?.Invoke(args.UserTranscript));
            ActiveConversation.AgentResponded += args => Enqueue(() => OnStuSaid?.Invoke(args.AgentResponse));
            ActiveConversation.ErrorOccurred += err => Debug.LogError("[Stu] conversation error: " + err);
            ActiveConversation.Disconnected += _ => Enqueue(EndConversationLocal);

            Debug.Log("[Stu] conversation started.");
            OnConversationStarted?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError("[Stu] failed to start conversation: " + e.Message);
            ActiveConversation = null;
        }
    }

    public async void EndConversation()
    {
        if (demoActive) { demoActive = false; StopAllCoroutines(); EndConversationLocal(); return; }
        var c = ActiveConversation;
        ActiveConversation = null;
        if (c != null) await c.EndSession();
        EndConversationLocal();
    }

    void EndConversationLocal()
    {
        ActiveConversation = null;
        demoActive = false;
        Debug.Log("[Stu] conversation ended.");
        OnConversationEnded?.Invoke();
    }

    // ---------------- Demo mode (no network) ----------------

    System.Collections.IEnumerator DemoConversation(string questionText)
    {
        demoActive = true;
        Debug.Log("[Stu demo] conversation started. Q: " + questionText);
        OnConversationStarted?.Invoke();
        yield return new WaitForSeconds(1.5f);

        string fake = "Good question! Before I say anything — what do you think the first step is?";
        Debug.Log("[Stu demo] Stu says: " + fake);
        OnStuSpeaking?.Invoke(true);
        OnStuSaid?.Invoke(fake);

        yield return new WaitForSeconds(2f);
        OnStuSpeaking?.Invoke(false);

        // Demo: also fire a sample diagram request so the XR lane can test wiring.
        Enqueue(() => OnSpawnDiagram?.Invoke("solar_system"));
        Debug.Log("[Stu demo] (demo) requested diagram: solar_system — conversation stays open until EndConversation().");
    }

    // ---------------- Client tool parameter shapes ----------------
    // Names MUST match the client tools defined in the ElevenLabs dashboard.

    [Serializable] public class SpawnDiagramParams { [JsonProperty("diagram_key")] public string diagram_key = ""; }
    [Serializable] public class PointAtParams { [JsonProperty("target")] public string target = ""; }
    [Serializable] public class SetMoodParams { [JsonProperty("mood")] public string mood = ""; }
    [Serializable] public class ToolResult { public bool ok = true; public string message = ""; }
}
