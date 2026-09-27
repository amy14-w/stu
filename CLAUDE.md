# Stu: AR study companion (HackGT 13, Immersive track)

Read this first. It is the shared context for anyone (human or Claude Code) working in this repo.

## The project

Stu is a small AR character that "body doubles" with a student while they study.
The phone sits on a stand: the BACK camera shows the desk on the other side of the phone
(the screen is a window onto the desk, Stu stands there), while the FRONT camera is hidden
and feeds MediaPipe so Stu reacts to the student's face.

- Impact goal: UN SDG 4 (Quality Education), target 4.5: support students who study alone
  (first-gen, parents working evenings, students with ADHD). Never claim Stu treats ADHD.
- Core idea: tell DISTRACTED apart from STUCK and respond differently.
  - Distracted (looking away, no face, phone picked up) -> gentle nudge.
  - Stuck (eyes on the page, brow furrowed, no progress for a while) -> Stu asks
    "Stuck on a concept? Want a hint?" It always ASKS, never assumes.
- Privacy (non-negotiable, also a pitch point): face processing stays on the phone.
  No video or images of the student leave the device. Only numbers/states are shared.
- Team of 4, 36-hour hackathon. Submission Sunday morning. Keep things simple and demoable.

## Tech setup

- Unity 6000.3.15f1 (Unity 6.3 LTS), AR Mobile template, URP. Everyone must use this exact version.
- Repo root = Unity project root (`Assets/`, `Packages/`, `ProjectSettings/`). Never commit `Library/`.
- Target device: Samsung Galaxy S22 (SM-S901U), Android 12, API 31, arm64.
- Player settings: IL2CPP, ARM64 only, Minimum API Level 31 (was 34, lowered for the S22).
- Input: new Input System (`UnityEngine.InputSystem`), not the old `Input` class.
- Plugins in the project:
  - MediaPipe Unity Plugin 0.16.3 (homuler), embedded at `Packages/com.github.homuler.mediapipe`,
    samples in `Assets/MediaPipeUnity`. Android builds need `libc++_shared.so` in `Assets/Plugins/Android`.
  - ElevenLabs Unity SDK (`io.elevenlabs.agents`) is EMBEDDED at `Packages/io.elevenlabs.agents` (commit 375e152,
    see its EMBEDDED.md). Installing from the git URL pulled in the repo's `TestProject/` (no .meta files), which
    aborted Android/iOS builds. No local manifest workaround is needed anymore.
- Mac disk space is tight. Android builds need ~10-15 GB free ("No space left on device" = disk full).

## What we learned about the cameras (important)

- `Assets/Scripts/StuWindowDemo.cs` + `Assets/Scenes/StuWindowDemo.unity` is the working test scene:
  back camera as full-screen background (WebCamTexture), placeholder Stu (capsule + eyes) placed by
  tapping the desk, on-screen debug text, and a `Camera Test` mode (Both / BackOnly / FrontOnly).
  - Stu uses a URP Lit material (`StuMat`) assigned in the Inspector; without it, Stu renders pink on Android.
- Unity's `WebCamTexture` can NOT run two cameras at once on the S22. When the front camera opens,
  Android closes the back one (Logcat: `Camera 0 ... CAMERA_STATE_CLOSED`, `Camera2: Capture session failed`).
- BUT the phone officially supports concurrent streaming: `CameraManager.getConcurrentCameraIds()`
  returned `{0, 1}, {0, 3}`. Camera 0 = main back, camera 1 = front. So back + front at the same time
  IS possible with native Camera2 code.
- Phone camera list: 0 back, 1 FRONT, 2 back, 3 FRONT, 4 back, 5 back.
- The phone does not move (it sits on a stand), so we do not need ARCore world tracking.
  Stu is placed on a virtual desk plane and stays put.

## The plan: back camera always live + hidden front camera -> MediaPipe -> Stu reacts

### Step 1 (cheapest experiment first): native plugin opens ONLY the front camera
Keep Unity's `WebCamTexture` for the back camera (camera 0, 1280x720 max). Write a small Android
plugin (Java is safest with Unity's Gradle setup; source files go in `Assets/Plugins/Android/`) that:
1. Opens camera "1" (front) with Camera2 at a small size (640x480) using an `ImageReader`.
2. Runs Google's official MediaPipe Tasks Vision `FaceLandmarker` for Android on those frames:
   running mode LIVE_STREAM, `outputFaceBlendshapes = true`, `numFaces = 1`.
   - Gradle dependency `com.google.mediapipe:tasks-vision` added via Player Settings ->
     Publishing Settings -> Custom Main Gradle Template.
   - Model `face_landmarker.task` (Google's official model file) placed in `Assets/StreamingAssets/`
     so it ends up in the APK assets; load it with `setModelAssetPath("face_landmarker.task")`.
3. Sends results to Unity about 10-15 times per second with
   `UnityPlayer.UnitySendMessage("FaceReceiver", "OnFace", json)`.
   JSON example: `{"face":true,"browDownL":0.4,"browDownR":0.38,"eyeLookDownL":0.6,"eyeLookDownR":0.6,
   "eyeLookOutL":0.1,"eyeLookOutR":0.05,"eyeBlinkL":0.1,"eyeBlinkR":0.1,"jawOpen":0.05,
   "mouthSmileL":0.0,"mouthSmileR":0.0,"t":123456}`
4. Exposes `start()` / `stop()` callable from C# via `AndroidJavaObject`.
Test: back feed must keep running (BACK fps stays ~30) while face JSON arrives.

### Step 2 (only if Step 1 kills the back camera): plugin owns BOTH cameras
Open cameras "0" and "1" together through the concurrent camera API, using concurrent-safe stream
sizes (check `isConcurrentSessionConfigurationSupported`). Back frames go to Unity as a texture
(simplest: YUV -> RGBA bytes -> `Texture2D.LoadRawTextureData`, ~720p), front frames go to MediaPipe.

### Step 3: Unity side
- `FaceReceiver` GameObject + `FaceSignals.cs`: parses the JSON, smooths every value over ~0.5 s
  (exponential moving average), tracks "no face" time.
- `StuMoodController.cs`: state machine driving Stu's reactions.
  - FOCUSED: eyes down (eyeLookDown avg > 0.4), brows relaxed.
  - DISTRACTED: no face > 3 s, or eyeLookOut/Up high > 3 s. Stu waves / leans in.
  - MAYBE_STUCK -> ASKING: browDown avg > 0.35 AND eyes on the page for > 60 s
    (DEMO MODE: 15 s). Stu tilts its head and a speech bubble asks
    "Stuck on a concept? Want a hint?" with Yes / Not now buttons. Cooldown 2 min after "Not now".
  - TIRED: eyeBlink avg > 0.6 for > 2 s, or jawOpen (yawn) spikes. Stu suggests a short break.
  - HAPPY: mouthSmile avg > 0.5. Stu celebrates.
  - Thresholds are guesses: show live values in the debug overlay and tune them on the real phone.
  - Include a hidden demo-mode toggle with short timers and a way to force each state for judging.
- Emotion-from-face is unreliable: Stu always asks and never labels the student.

## iPhone (iOS) path

- iOS can't run a native front-camera plugin next to WebCamTexture, so iPhone uses ARKit instead:
  world tracking (back camera AR view) + user face tracking (front TrueDepth camera) in one session.
  Needs a Face ID iPhone (XS/XR or newer); target test device is an iPhone 15.
- `ARKitFaceSource.cs` fills the same `FaceFrame` as the Android plugin via `FaceReceiver.Publish()`,
  so FaceSignals / StuckDetector / StuSpeechBubble are shared. `StuARPlacement.cs` puts Stu on the desk plane.
- Scene `Assets/Scenes/StuARDemo.unity` is generated by menu Stu > iOS > Create AR Scene
  (`Assets/Editor/StuIOSSetup.cs`). Switching build platform auto-selects the scene (iOS: StuARDemo,
  Android: StuWindowDemo).
- ARKit settings: Face Tracking ON. iOS bundle id `com.hackgt13.stu`. Building needs Unity iOS Build Support,
  Xcode, and an Apple ID (free personal team is fine); tune stuck thresholds again on the iPhone.

## Integrated scene (semi-finalist): AR desk + Pip + face detection + AI tutor, iPhone

- Menu Stu > Integrate > Create Integrated Scene copies ARAndAII.unity (Quinton's AR scene + the AI tutor
  StuConversation; falls back to Quinton.unity) to StuIntegrated.unity, which becomes the build scene. Adds Pip
  (StuPipAR = StuPip + Stu component + DiagramAnchor, 13 cm), face detection and `StuARFaceBridge`.
  Buttons (Spawn Stu / Change Surface / Test Problem) bottom right. Re-run it after the source scene changes.
- Flow: Change Surface -> tap a plane -> Spawn Stu -> face detection starts (pauses while changing surface).
  Test Problem loads the problem and starts the AI conversation. Stu's "Want a hint?" -> Yes also starts it.
  AI tools point / cheer / think drive Pip; the stuck question is paused while the AI is talking.
- iPhone only for face detection here (ARKit tracks desk + face together). Android: face detection off in this
  scene (ARCore + front plugin deadlocked the S22 camera service); StuWindowDemo still has Android face detection.
- iOS needs camera + microphone usage descriptions (set in Player Settings).

## Conventions

- One owner per `.unity` scene file; prefer prefabs and scripts to avoid merge conflicts.
- `git pull` before starting work and before pushing (pull.rebase=false, merge).
- Keep `DualCameraTest`/`StuWindowDemo` as test tools; build new features in new scripts.
- After changing code: save, let Unity recompile, check Console for red errors, then
  File -> Build Profiles -> Build And Run. Read errors in Window -> Analysis -> Android Logcat.
- Build log: `~/Library/Logs/Unity/Editor.log`.