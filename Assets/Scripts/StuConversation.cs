// StuConversation.cs — Simmi's lane: CHARACTER COMMUNICATION (HackGT 13)
// Wraps the ElevenLabs Agents SDK for Unity (io.elevenlabs.agents).
//
// What it does:
//   - Starts/stops a voice conversation with the Stu agent (mic -> STT -> LLM -> TTS
//     is ALL handled inside the SDK; you don't wire Whisper/Gemini/TTS separately).
//   - Lets the agent drive Stu's ACTIONS through CLIENT TOOLS (team-agreed names):
//       get_question_info {none}          -> returns JSON: question_text, diagram_key,
//                                            diagram_description, available_diagrams
//       show_diagram      {diagram_key}   -> DiagramManager shows the AR diagram (XR lane)
//       hide_diagram      {none}          -> DiagramManager hides the AR diagram
//       perform_diagram_action {action_tag} -> DiagramManager runs a scripted action
//                                            e.g. ZOOM_ON_SUN (tag vocabulary is XR lane's)
//       set_mood          {mood}          -> idle-working | nudge | curious | happy
//   - Fires C# events the Stu state machine / UI can subscribe to.
//   - Writes everything Stu says (+ silent [ACTION_TAG]s) to an AR text box.
//
// DASHBOARD SETUP (ElevenLabs dashboard -> your Stu agent):
//   1. System prompt = base persona + behavior blocks. Base:
//
//      "You are Stu, a friendly AR study companion for students of any age.
//      At the START of every conversation, call get_question_info to learn the
//      current question and available diagrams. Wait for its response before
//      explaining anything."
//
//      Then append: PACING, HINT LADDER, TURN-TAKING, SILENT ACTIONS (below),
//      OPEN-ENDED PROBLEM SOLVING — DEBUG THEIR THINKING.
//
//      SILENT ACTIONS — COMMANDS ARE NEVER SPOKEN:
//      Tools show_diagram / hide_diagram / perform_diagram_action / set_mood are
//      SILENT — use them for ALL diagram/stage actions. To zoom on the sun, call
//      perform_diagram_action with action_tag "ZOOM_ON_SUN" and say only the
//      natural-language sentence. NEVER write bracketed commands like [ZOOM_ON_SUN]
//      in reply text: everything written gets spoken aloud. The app writes the
//      action tag into the on-screen text box when the tool fires.
//
//   2. Add these 5 CLIENT tools (names MUST match this script exactly):
//       get_question_info    | "Get the current study question and available diagrams
//                              from the app. Call at the start of every conversation
//                              (and when the topic changes). Returns JSON." 
//                              | no params | WAIT FOR RESPONSE: ON
//       show_diagram         | "Display an AR diagram for the student." 
//                              | param: diagram_key (string, required; enum: solar_system,
//                                plant_life_cycle, butterfly_life_cycle, division,
//                                fraction_map) | wait: OFF
//       hide_diagram         | "Hide the currently displayed AR diagram." 
//                              | no params | wait: OFF
//       perform_diagram_action | "Run a scripted action on the current diagram,
//                              e.g. zoom into the sun." 
//                              | param: action_tag (string, required, e.g. ZOOM_ON_SUN)
//                              | wait: OFF
//       set_mood             | "Change Stu's expression/body language." 
//                              | param: mood (string, required; enum: idle-working,
//                                nudge, curious, happy) | wait: OFF
//   3. Security -> Authentication: OFF (public agent, simplest for the demo).
//      Security -> Overrides: first-message override ON (Unity sends the greeting).
//   4. Copy the Agent ID into the inspector field below. Publish.
//
// UNITY SETUP:
//   1. Empty GameObject "StuConversation" + this script (+ optional AudioSource on Stu's head).
//   2. XR lane subscribes and routes to DiagramManager / Stu's animator. Example:
//        StuConversation.Instance.OnShowDiagram += key => DiagramManager.Instance.Show(key);
//        StuConversation.Instance.OnHideDiagram += () => DiagramManager.Instance.Hide();
//        StuConversation.Instance.OnPerformDiagramAction += tag => DiagramManager.Instance.PerformAction(tag);
//        StuConversation.Instance.OnSetMood += mood => StuAnimator.SetMood(mood);
//      ProblemsManager (optional) overrides the question source:
//        StuConversation.Instance.GetQuestionInfoProvider = () => ProblemsManager.Instance.GetQuestionInfo();
//   3. Sensing lane: call StartConversation(questionText, diagramKey) when the state
//      machine enters ASKING/CONVERSATION; call EndConversation() when done.
//   4. demoMode = true lets every lane integrate with zero network.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
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

    [Header("Subtitles (optional)")]
    [Tooltip("Optional AR text box. Shows what Stu says. The XR lane drags the AR screen's text here (or subscribes to OnStuSaid).")]
    public TextMeshProUGUI subtitleText;
    [Tooltip("If ON, silent tool calls also write a tag like [ZOOM_ON_SUN] into the text box. Tags are written, never spoken.")]
    public bool showActionTags = true;

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
    public event Action<string> OnShowDiagram;        // diagram_key
    public event Action OnHideDiagram;                // no args
    public event Action<string> OnPerformDiagramAction; // action_tag, e.g. ZOOM_ON_SUN
    public event Action<string> OnSetMood;           // idle-working | nudge | curious | happy

    // ---- Question source (ProblemsManager overrides; default = StartConversation args) ----
    // The agent pulls this via the get_question_info client tool (wait-for-response ON).
    public Func<QuestionInfoResult> GetQuestionInfoProvider;

    string _questionText = "";
    string _diagramKey = "";

    // Tool calls can arrive off the main thread -> marshal to Update().
    readonly Queue<Action> mainThreadQueue = new Queue<Action>();

    // Subtitle state: Stu's latest line + written action tags for this exchange.
    string currentSubtitleLine = "";
    readonly List<string> pendingActionTags = new List<string>();

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }
    }

    System.Collections.IEnumerator Start()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

        // Solo testing: auto-start a conversation on Play.
        // In the real app the Sensing lane calls StartConversation() instead (and unticks Auto Start).
        if (autoStartDemoOnPlay)
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

    /// <summary>Start talking to Stu about a question. The agent pulls the
    /// question/diagram via get_question_info (default built from these args).</summary>
    public async void StartConversation(string questionText, string diagramKey, string firstMessageOverride = null)
    {
        if (IsInConversation) return;
        _questionText = questionText ?? "";
        _diagramKey = diagramKey ?? "";

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
                Overrides = new ConversationConfigOverride
                {
                    Agent = new ConversationConfigOverrideAgent
                    {
                        FirstMessage = !string.IsNullOrWhiteSpace(firstMessageOverride) ? firstMessageOverride
                            : "Hello! I'm Stu, your study buddy. Let's study together — what do you want to start with?"
                    }
                }
            };

            ActiveConversation = await Conversation.StartSessionAsync(options);

            // Client tools: the agent invokes these, we forward to the game on the main thread.
            // Names MUST match the dashboard tools exactly.
            ActiveConversation.RegisterTool<NoParams, QuestionInfoResult>("get_question_info", _ =>
            {
                return GetQuestionInfo();
            });
            ActiveConversation.RegisterTool<ShowDiagramParams, ToolResult>("show_diagram", p =>
            {
                Enqueue(() => { OnShowDiagram?.Invoke(p.diagram_key); AppendActionTag("SHOW_" + CleanTag(p.diagram_key)); });
                return new ToolResult { ok = true, message = "Showing diagram " + p.diagram_key };
            });
            ActiveConversation.RegisterTool<NoParams, ToolResult>("hide_diagram", _ =>
            {
                EnquoteHideDiagram();
                return new ToolResult { ok = true, message = "Diagram hidden" };
            });
            ActiveConversation.RegisterTool<PerformDiagramActionParams, ToolResult>("perform_diagram_action", p =>
            {
                Enqueue(() => { OnPerformDiagramAction?.Invoke(p.action_tag); AppendActionTag(CleanTag(p.action_tag)); });
                return new ToolResult { ok = true, message = "Performed " + p.action_tag };
            });
            ActiveConversation.RegisterTool<SetMoodParams, ToolResult>("set_mood", p =>
            {
                Enqueue(() => { OnSetMood?.Invoke(p.mood); AppendActionTag("MOOD_" + CleanTag(p.mood)); });
                return new ToolResult { ok = true, message = "Mood: " + p.mood };
            });

            ActiveConversation.ModeChanged += mode => Enqueue(() => OnStuSpeaking?.Invoke(mode == Mode.Speaking));
            ActiveConversation.UserTranscriptReceived += args => Enqueue(() => { OnUserSaid?.Invoke(args.UserTranscript); ClearActionTags(); });
            ActiveConversation.AgentResponded += args => Enqueue(() => { OnStuSaid?.Invoke(args.AgentResponse); SetSubtitle(args.AgentResponse); });
            ActiveConversation.ErrorOccurred += err => Debug.LogError("[Stu] conversation error: " + err);
            ActiveConversation.Disconnected += d => { Debug.LogWarning("[Stu] disconnected: " + d); Enqueue(EndConversationLocal); };

            Debug.Log("[Stu] conversation started.");
            OnConversationStarted?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError("[Stu] failed to start conversation: " + e.Message);
            ActiveConversation = null;
        }
    }

    void EnquoteHideDiagram()
    {
        Enqueue(() => { OnHideDiagram?.Invoke(); AppendActionTag("HIDE_DIAGRAM"); });
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
        currentSubtitleLine = "";
        pendingActionTags.Clear();
        if (subtitleText != null) subtitleText.text = "";
        Debug.Log("[Stu] conversation ended.");
        OnConversationEnded?.Invoke();
    }

    // ---------------- Question info (get_question_info tool) ----------------

    static readonly string[] KnownDiagrams = new[]
    {
        "solar_system", "plant_life_cycle", "butterfly_life_cycle", "division", "fraction_map"
    };

    QuestionInfoResult GetQuestionInfo()
    {
        if (GetQuestionInfoProvider != null)
        {
            try
            {
                var r = GetQuestionInfoProvider();
                if (r != null) return r;
            }
            catch (Exception e) { Debug.LogWarning("[Stu] GetQuestionInfoProvider failed: " + e.Message); }
        }
        return new QuestionInfoResult
        {
            question_text = _questionText,
            diagram_key = _diagramKey,
            diagram_description = "",
            available_diagrams = KnownDiagrams
        };
    }

    // ---------------- Subtitles ----------------
    // What Stu says appears in the AR text box. Silent tool actions append a
    // written tag like [ZOOM_ON_SUN] — written into the box, never spoken.

    void SetSubtitle(string line)
    {
        currentSubtitleLine = line ?? "";
        RefreshSubtitle();
    }

    void AppendActionTag(string tag)
    {
        if (!showActionTags || string.IsNullOrWhiteSpace(tag)) return;
        pendingActionTags.Add(tag.Trim());
        RefreshSubtitle();
    }

    void ClearActionTags()
    {
        pendingActionTags.Clear();
        RefreshSubtitle();
    }

    void RefreshSubtitle()
    {
        if (subtitleText == null) return;
        // <break> tags create the spoken pause; strip them so they don't show in text.
        string t = Regex.Replace(currentSubtitleLine, @"<break[^>]*>", "");
        foreach (var tag in pendingActionTags) t += " [" + tag + "]";
        subtitleText.text = t;
    }

    static string CleanTag(string s)
    {
        return Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"\s+", "_");
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
        SetSubtitle(fake);

        yield return new WaitForSeconds(2f);
        OnStuSpeaking?.Invoke(false);

        // Demo: fire sample diagram requests so the XR lane can test wiring.
        Enqueue(() => { OnShowDiagram?.Invoke("solar_system"); AppendActionTag("SHOW_SOLAR_SYSTEM"); });
        Enqueue(() => { OnPerformDiagramAction?.Invoke("ZOOM_ON_SUN"); AppendActionTag("ZOOM_ON_SUN"); });
        Debug.Log("[Stu demo] (demo) show_diagram(solar_system) + perform_diagram_action(ZOOM_ON_SUN) — conversation stays open until EndConversation().");
    }

    // ---------------- Client tool parameter/result shapes ----------------
    // Tool NAMES must match the client tools defined in the ElevenLabs dashboard.

    [Serializable] public class NoParams { }
    [Serializable] public class ShowDiagramParams { [JsonProperty("diagram_key")] public string diagram_key = ""; }
    [Serializable] public class PerformDiagramActionParams { [JsonProperty("action_tag")] public string action_tag = ""; }
    [Serializable] public class SetMoodParams { [JsonProperty("mood")] public string mood = ""; }
    [Serializable] public class ToolResult { public bool ok = true; public string message = ""; }

    [Serializable]
    public class QuestionInfoResult
    {
        public bool ok = true;
        public string question_text = "";
        public string diagram_key = "";
        public string diagram_description = "";
        public string[] available_diagrams = new string[0];
    }
}
