// StuConversation.cs — Simmi's lane: CHARACTER COMMUNICATION (HackGT 13)
// Wraps the ElevenLabs Agents SDK for Unity (io.elevenlabs.agents).

using System;
using System.Collections.Generic;
using UnityEngine;
using ElevenLabs.Agents;
using ElevenLabs.Protocol;
using Newtonsoft.Json;

[DefaultExecutionOrder(-100)] // before DiagramManager.OnEnable, which subscribes to Instance
public class StuConversation : MonoBehaviour
{
    public static StuConversation Instance;

    [Header("Agent")]
    [Tooltip("ElevenLabs dashboard -> Agents -> Stu -> Agent ID")]
    public string agentId = "PASTE_AGENT_ID_HERE";

    [Header("Audio")]
    [Tooltip("Optional: put on Stu's head so his voice is spatial.")]
    public AudioSource agentAudioSource;

    [Header("Active Context")]
    public string currentQuestionText = "Solve 2x + 5 = 17";
    public string currentDiagramDescription = "A linear equation balancing scale.";
    public string currentIntendedAnswer = "x = 6";
    public string currentIntendedSteps = "1. Subtract 5 from both sides to get 2x = 12. 2. Divide both sides by 2 to get x = 6.";
    public string[] currentDiagramActions = new string[] { "ADD_WEIGHT", "REMOVE_WEIGHT", "BALANCE" };

    [Header("Demo")]
    [Tooltip("ON = no network. Simulates Stu talking so other lanes can integrate.")]
    public bool demoMode = true;
    [Tooltip("If ON, pressing Play starts a fake conversation automatically (for solo testing).")]
    public bool autoStartDemoOnPlay = true;

    public Conversation ActiveConversation { get; private set; }
    bool demoActive;
    public bool IsInConversation => ActiveConversation != null || demoActive;

    // ---- Events for the Stu state machine / UI ----
    public event Action OnConversationStarted;
    public event Action OnConversationEnded;
    public event Action<string> OnUserSaid;   // student speech-to-text
    public event Action<string> OnStuSaid;    // Stu's reply text
    public event Action<bool> OnStuSpeaking;  // true = Stu has the audio floor

    // ---- Action requests from the agent (XR / Diagram lane subscribes) ----
    public event Action OnShowDiagram;                   // show active question's diagram
    public event Action OnHideDiagram;                   // hide active diagram
    public event Action<string> OnPerformDiagramAction;  // action_tag (e.g. GROW_SPHERE, ROTATE_MARS)
    public event Action OnPoint;                         // trigger pointing gesture
    public event Action OnCheer;                         // trigger cheer animation
    public event Action OnThink;                         // trigger thinking animation

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

        if (demoMode && autoStartDemoOnPlay)
            StartConversation(currentQuestionText);
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

    public void SetContext(string questionText, string diagramDescription, string intendedAnswer, string intendedSteps, string[] actionTags)
    {
        currentQuestionText = questionText;
        currentDiagramDescription = diagramDescription;
        currentIntendedAnswer = intendedAnswer;
        currentIntendedSteps = intendedSteps;
        if (actionTags != null) currentDiagramActions = actionTags;
    }

    /// <summary>Start talking to Stu about the current question.</summary>
    public async void StartConversation(string questionText)
    {
        if (IsInConversation) return;

        currentQuestionText = questionText ?? currentQuestionText;

        if (demoMode) { StartCoroutine(DemoConversation(currentQuestionText)); return; }

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
                    ["question_text"] = currentQuestionText ?? "",
                },
                Overrides = new ConversationConfigOverride
                {
                    Agent = new ConversationConfigOverrideAgent
                    {
                        FirstMessage = $"Hey! I see you're working on this: {currentQuestionText}. Want a hint, or should we talk it through?"
                    }
                }
            };

            ActiveConversation = await Conversation.StartSessionAsync(options);

            // 1. CLIENT TOOL: get_question_info
            ActiveConversation.RegisterTool<GetQuestionInfoParams, QuestionInfoResult>("get_question_info", p =>
            {
                Debug.Log("[Stu Tool] AI requested problem info.");
                return new QuestionInfoResult
                {
                    question_text = currentQuestionText,
                    diagram_description = currentDiagramDescription,
                    intended_answer = currentIntendedAnswer,
                    intended_steps = currentIntendedSteps,
                    available_actions = currentDiagramActions
                };
            });

            // 2. CLIENT TOOL: show_diagram
            ActiveConversation.RegisterTool<ShowDiagramParams, ToolResult>("show_diagram", p =>
            {
                Enqueue(() => OnShowDiagram?.Invoke());
                return new ToolResult { ok = true, message = "Diagram shown." };
            });

            // 3. CLIENT TOOL: hide_diagram
            ActiveConversation.RegisterTool<HideDiagramParams, ToolResult>("hide_diagram", p =>
            {
                Enqueue(() => OnHideDiagram?.Invoke());
                return new ToolResult { ok = true, message = "Diagram hidden." };
            });

            // 4. CLIENT TOOL: perform_diagram_action
            ActiveConversation.RegisterTool<PerformActionParams, ToolResult>("perform_diagram_action", p =>
            {
                Enqueue(() => OnPerformDiagramAction?.Invoke(p.action_tag));
                return new ToolResult { ok = true, message = "Performed action: " + p.action_tag };
            });

            // 5. CLIENT TOOL: point
            ActiveConversation.RegisterTool<PointParams, ToolResult>("point", p =>
            {
                Enqueue(() => OnPoint?.Invoke());
                return new ToolResult { ok = true, message = "Pointed." };
            });

            // 6. CLIENT TOOL: cheer
            ActiveConversation.RegisterTool<CheerParams, ToolResult>("cheer", p =>
            {
                Enqueue(() => OnCheer?.Invoke());
                return new ToolResult { ok = true, message = "Cheered." };
            });

            // 7. CLIENT TOOL: think
            ActiveConversation.RegisterTool<ThinkParams, ToolResult>("think", p =>
            {
                Enqueue(() => OnThink?.Invoke());
                return new ToolResult { ok = true, message = "Thinking." };
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

        // Demo test calls
        Enqueue(() => OnShowDiagram?.Invoke());
        Enqueue(() => OnThink?.Invoke());
        Enqueue(() => OnPerformDiagramAction?.Invoke("ADD_WEIGHT"));
        Enqueue(() => OnCheer?.Invoke());
    }

    // ---------------- Client tool parameter & return shapes ----------------

    [Serializable] public class GetQuestionInfoParams { }

    [Serializable]
    public class QuestionInfoResult
    {
        [JsonProperty("question_text")] public string question_text = "";
        [JsonProperty("diagram_description")] public string diagram_description = "";
        [JsonProperty("intended_answer")] public string intended_answer = "";
        [JsonProperty("intended_steps")] public string intended_steps = "";
        [JsonProperty("available_actions")] public string[] available_actions = new string[0];
    }

    [Serializable] public class ShowDiagramParams { }
    [Serializable] public class HideDiagramParams { }
    [Serializable] public class PerformActionParams { [JsonProperty("action_tag")] public string action_tag = ""; [JsonProperty("actionTag")] public string actionTag { set => action_tag = value; } }
    [Serializable] public class PointParams { }
    [Serializable] public class CheerParams { }
    [Serializable] public class ThinkParams { }
    [Serializable] public class ToolResult { public bool ok = true; public string message = ""; }
}