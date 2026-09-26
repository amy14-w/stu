# Stu

Mobile AR app built in Unity, starting from Unity's **AR Mobile** template (AR Foundation, ARKit for iOS, ARCore for Android).

Stu puts an AR character and diagrams into the real world. You ask the character a question out loud. It answers in its own voice and can change the diagrams while it explains.

> **TODO:** Add who Stu is for and one example of how someone would use it.

---

## Features and tech stack

### 1. AR character and diagrams

| Feature | How | Status |
|---|---|---|
| Plane detection (placing the character and diagrams) | AR Foundation `ARPlaneManager`, already in the template | ✅ in template |
| QR code scanning | Not built into AR Foundation. Options: ARKit/ARCore **image tracking** with a printed marker, or decode camera frames with a library such as ZXing.Net | ⬜ to do |
| Face detection | [homuler/MediaPipeUnityPlugin](https://github.com/homuler/MediaPipeUnityPlugin) (runs MediaPipe in Unity) | ⬜ to do |

### 2. Character communication

The flow is: **mic → speech-to-text → LLM → text-to-speech → character speaks and diagrams update**

| Step | How | Status |
|---|---|---|
| **Voice to text** (understand the user's question) | Unity `Microphone` records audio, then `UnityWebRequest` sends it to the **OpenAI Whisper API** | ⬜ to do |
| ↳ Detect when speech starts and ends | Voice activity detection: watch the mic volume, start recording when it rises above a threshold, and stop after about 1 s of silence. Then pack the audio into a WAV file for the API call. | ⬜ to do |
| **LLM** (answer questions and change diagrams) | `UnityWebRequest` calls the **Gemini or OpenAI API**, using the fastest model available. To let the model change diagrams, have it return structured output (JSON / tool calls) that our code turns into diagram actions. | ⬜ to do. Pick a provider. |
| **Text to speech** (character's voice) | [ElevenLabs](https://elevenlabs.io/use-cases/unity) | ⬜ to do |

**Latency:** each question makes three network calls in a row (Whisper, then the LLM, then ElevenLabs). Stream the responses wherever the APIs support it, so the character starts talking sooner.

---

## What you need

### Software

| Tool | Version | Notes |
|---|---|---|
| **Unity Hub** | latest | [unity.com/download](https://unity.com/download) |
| **Unity Editor** | **6000.3.15f1** (Unity 6.3) | Install this exact version so project files don't get upgraded by accident. |
| ↳ Android Build Support | same | Add as a module in Unity Hub. Include **OpenJDK** and **Android SDK & NDK Tools**. |
| ↳ iOS Build Support | same | Add as a module in Unity Hub (Mac only). |
| **Git** + **Git LFS** | any recent | Images, audio and 3D models are stored in LFS. You need it before you clone. |
| **Xcode** | latest | Only needed to build for iPhone/iPad (Mac only). |
| Code editor | — | Rider or VS Code (with the C# Dev Kit and Unity extensions). |

### Hardware

- An **AR-capable phone** to test on:
  - iOS: iPhone/iPad that supports ARKit (iPhone XS or newer is safe).
  - Android: a device on the [ARCore supported devices list](https://developers.google.com/ar/devices).
- A USB cable for that phone.
- To build for iOS you need a Mac.

### Accounts

- **Unity account.** A free Personal license is fine.
- **GitHub access** to [amy14-w/stu](https://github.com/amy14-w/stu). Ask Amy to add you.
- **Apple ID.** A free one can install builds on your own iPhone. A paid Apple Developer account ($99/yr) is only needed for TestFlight or the App Store.
- **API keys** (only for the parts you work on):
  - **OpenAI**, for Whisper speech-to-text (and the LLM if we choose OpenAI)
  - **Google AI Studio / Gemini**, if we choose Gemini for the LLM
  - **ElevenLabs**, for the character's voice

> ⚠️ **Never commit API keys.** Keep them in a local file that is gitignored (for example `Assets/StreamingAssets/secrets.json`), and share a `secrets.example.json` with empty values instead. Keys built into a shipped app can be pulled out of it, so before a public release, send the calls through a small backend server.

---

## Getting set up

```bash
# 1. Install Git LFS once per machine (macOS shown; see git-lfs.com for other OSes)
brew install git-lfs
git lfs install

# 2. Clone
git clone https://github.com/amy14-w/stu.git
cd stu
git lfs pull   # makes sure the real image/model files are downloaded
```

3. In Unity Hub, click **Add → Add project from disk** and pick the `stu` folder.
4. Open it with Unity **6000.3.15f1**. The first import takes a few minutes because it builds the `Library/` folder.
5. Open `Assets/Scenes/SampleScene.unity`.

### Try it without a phone

AR Foundation includes **XR Simulation**, so you can press Play in the editor and move around a fake room.
- **Edit → Project Settings → XR Plug-in Management → Desktop/Standalone tab**: make sure **XR Simulation** is checked.
- Press Play. Use right-click + WASD to move the camera.

### Build to a phone

**Android**
1. **File → Build Profiles**, choose **Android**, then **Switch Platform**.
2. Turn on Developer Options and USB debugging on the phone, then plug it in.
3. Click **Build And Run**.

**iOS**
1. **File → Build Profiles**, choose **iOS**, then **Switch Platform**.
2. Click **Build**. This creates an Xcode project in a folder you choose. Put it outside `Assets/`. `Builds/` is gitignored.
3. Open the generated `.xcodeproj`, set your Team under *Signing & Capabilities*, then run it on your plugged-in device.

---

## Working together (please read)

- **Don't commit** `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`, or `.csproj`/`.sln` files. The `.gitignore` already excludes them.
- **Always commit `.meta` files** together with the asset they belong to. If a `.meta` file is missing, references break for everyone else.
- **Scenes and prefabs are hard to merge.** Say in the group chat before you edit a shared scene. Where you can, build your features in your own prefab or scene.
- Pull often and keep branches short-lived: `git checkout -b yourname/feature`, then open a PR into `main`.
- Big binary files (`.png .jpg .psd .fbx .obj .glb .wav .mp3`) go through LFS automatically. If you add a new binary type (for example `.tif`, `.tga`, `.mp4`), add it to `.gitattributes` first.

---

## Project layout

```
Assets/
  Scenes/SampleScene.unity    # current main scene (from the template)
  MobileARTemplateAssets/     # template prefabs, UI, materials (safe to replace over time)
  XR/                         # ARKit / ARCore / Simulation settings
  XRI/, Samples/              # XR Interaction Toolkit assets
Packages/manifest.json        # package list (AR Foundation 6.5, XRI 3.5, URP 17.3, Input System)
ProjectSettings/              # shared project settings
```

---

## Still to do before a real build

- [ ] Finish the project description above.
- [ ] Choose the LLM provider (Gemini or OpenAI) and model.
- [ ] Set up secrets handling: add the gitignored secrets file and the example file.
- [ ] Add permission descriptions for **camera** and **microphone** (iOS: Player Settings → *Camera Usage Description* / *Microphone Usage Description*; Android asks for these at runtime).
- [ ] Decide on the QR approach: image tracking or a decoder library.
- [ ] Set **Company Name** and **Bundle Identifier** (Project Settings → Player). They are still the template defaults (`com.unity.template.*`).
- [ ] Rename or replace `SampleScene` with Stu's real main scene.
- [ ] Decide who owns which part (scenes, UI, AR interactions, art).
- [ ] Add app icon and splash screen.
