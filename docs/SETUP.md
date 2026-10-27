# Keeper244 — Setup (Phase 0)

Everyone does **A–C**. Only Aditya does **D** (creating the Unity project, once). Then everyone does **E**.

## A. Install

| Tool | macOS | Windows 11 |
|---|---|---|
| Git + Git LFS | `brew install git git-lfs` then `git lfs install` | [Git for Windows](https://git-scm.com/download/win) (LFS included) then `git lfs install` |
| Python | 3.11 or 3.12 (`brew install python@3.12`) | 3.11 or 3.12 from [python.org](https://www.python.org/downloads/windows/) — tick **Add to PATH** |
| Unity Hub | [unity.com/download](https://unity.com/download) | same |
| Unity Editor | **Unity 6000.3.26f1** (6.3 LTS) exactly, via Hub → Installs | same |
| Unity modules | Mac Build Support (Mono) is default; **add Windows Build Support (Mono)** | Windows Build Support is default; Mac support optional |
| Code editor | VS Code + C# Dev Kit, or Rider | Visual Studio 2022 (Hub offers it), VS Code, or Rider |

**Why Unity 6.3 LTS:** 6.0 LTS support ends this month (Oct 2026); 6.3 LTS is supported until Dec 2027. Lab PCs do **not** need Unity: they only run the built `.exe`.

**Exact patch rule:** the project is pinned to **6000.3.26f1** (see `unity/Keeper244/ProjectSettings/ProjectVersion.txt`). Everyone installs **that exact patch**. If Hub → Installs doesn't list it, open the project anyway: Hub offers to install the matching version. Never let Unity "upgrade" the project.

## B. Clone

```bash
git clone https://github.com/adityapriyadarshi2025/Keeper244.git
cd Keeper244
git lfs pull
```

## C. Python environment

macOS / Linux:

```bash
cd python
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
pytest
```

Windows (PowerShell):

```powershell
cd python
py -3.12 -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pytest
```

If PowerShell blocks the activate script: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

`pytest` must print all tests passed. The same tests run on GitHub Actions on Windows, macOS and Linux for every push.

## D. Create the Unity project (Aditya, once) — DONE 2026-10-19 (commit c935391)

Kept for reference. **Gotcha:** in Hub's New Project dialog, leave any "Create Git repository" / "Unity Version Control" / "Connect to Unity Cloud" option **off**. Otherwise Hub makes a second git repo inside `unity/Keeper244/`, and the outer repo can't add the files.

1. Unity Hub → **New project** → template **Universal 3D** → Editor **6000.3.26f1**.
2. Project name `Keeper244`, location `<repo>/unity/`. The folder `unity/Keeper244` must not exist yet; Hub creates it.
3. In the editor: **Edit → Project Settings**:
   - **Editor** page → Asset Serialization → Mode: **Force Text**
   - **Version Control** page (its own entry in the left list in Unity 6) → Mode: **Visible Meta Files**
   (Both are Unity 6 defaults; confirm them.)
4. **Edit → Project Settings → Player**: Company Name `Team20`, Product Name `Keeper244`. Leave Active Input Handling at **Input System Package** (the game scripts use it).
5. Close Unity, then commit:

```bash
git add unity/Keeper244
git status          # must NOT list Library/, Temp/, Logs/, UserSettings/
git commit -m "P0: create Unity 6.3 LTS project"
git push
```

## E. Open the project (everyone else)

`git pull` and `git lfs pull` → Unity Hub → **Add → Add project from disk** → pick `unity/Keeper244` → use editor **6000.3.26f1**. First open takes a few minutes while Unity rebuilds `Library/`.

Then in the editor: **Unity → Settings → External Tools** (macOS) or **Edit → Preferences → External Tools** (Windows) → External Script Editor → your code editor.

## F. Optional: Unity Smart Merge (fewer broken scene merges)

Tell git where Unity's YAML merge tool is (once per machine; path depends on your editor version):

macOS:

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/<VERSION>/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```

Windows:

```powershell
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/<VERSION>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
```

Without this, git falls back to a normal text merge, which is why we also keep the one-scene-owner rule (CONTRIBUTING.md).

## G. Play with fake EMG (Phase 3)

The game runs over the real UDP link (docs/PROTOCOL.md) before any electrodes or DSP exist. `tools/fake_sender.py` plays the bridge.

1. Unity → Play → menu → **INPUT: EMG over UDP**. The line under it reads "Listening on port 5005…".
2. In a terminal, in `python/` with the venv active:

   ```bash
   python tools/fake_sender.py               # keys: q a s d e = slot -2 … +2, space = relax, p = pause, o = stale packet, f = flatline, x = quit
   python tools/fake_sender.py --mode auto   # a keeper that plays itself from the game's shot_cue events
   ```

   The menu line turns green ("Receiving on port 5005 · 100 Hz · ok · calibrated"). Pick a level.
3. What to check (P3 "done when"):
   - Same game as on the keyboard, L0–L3.
   - Pause the sender (`p`, or Ctrl-C) during a shot → **NO SIGNAL** within 250 ms; the shot is voided (not a goal) and replayed after the rest gate.
   - `o` sends a stale packet → the F1 panel's "out-of-order" count goes up and the keeper doesn't twitch.
   - The terminal prints each game event, and the summary on exit says `invalid events: 0`.
   - F1 shows packet rate (≈ 100 Hz), interval jitter, lost packets, and the round trip from `slot_applied`.
   - `M` in the game sends a `mark` command (trial label) to Python.

The game keeps running when the terminal has focus (it sets Run In Background on start).

Hands-free soak test: `python tools/fake_sender.py --mode auto --miss 0.3 --dropout-at 30 --dropout-for 2 --shuffle-every 200`.

## H. Windows build (lab PCs)

Lab PCs run the built game; they don't need Unity.

- **Keeper244 → Build Game → Windows (64-bit)**. Output: `unity/Keeper244/Builds/Windows/` (git-ignored). Copy or zip the **whole folder**, not just the `.exe`.
- On a Mac this needs the **Windows Build Support (Mono)** module for 6000.3.26f1 (Hub → Installs → ⚙ → Add modules); the menu says so if it is missing. Teammates on Windows have it already.
- On the lab PC: run `Keeper244.exe` and start the bridge (or `fake_sender.py`) on the same PC. The game listens on 127.0.0.1 only, so Windows Firewall does not prompt. Logs: `%USERPROFILE%\AppData\LocalLow\Team20\Keeper244\sessions`.
- **Build Game → macOS** makes a Mac app the same way (demos).

## I. VR: Oculus Rift S

The game shows in the lab's Rift S through OpenXR. The participant only watches: no controllers, because gripping one tenses the forearm and pollutes the EMG. The operator runs everything from the desktop window, which keeps the normal menus and HUD. The headset shows a fixed coach seat behind the goal (head tracking only, never moved by the game) with floating signs for the call-out, scoreboard and muscle bars.

**One-time Unity setup (on the Mac, then commit):**

1. **Window → Package Manager → Unity Registry →** search **OpenXR Plugin → Install**. It brings XR Plug-in Management with it.
2. **Edit → Project Settings → XR Plug-in Management**, desktop tab (Windows, Mac, Linux): tick **OpenXR**, and **untick "Initialize XR on Startup"**. The game starts the headset itself, and only on Windows, so the Mac never tries.
3. **XR Plug-in Management → OpenXR**, desktop tab: *Enabled Interaction Profiles* **+ → Oculus Touch Controller Profile**; *Render Mode* **Single Pass Instanced**.
4. **XR Plug-in Management → Project Validation → Fix All** (ignore Android items).
5. Press Play on the Mac: the menu's **VR HEADSET** line should say *VR needs the Windows PC*. That means the XR code is installed. If it says *VR packages not installed*, step 1 didn't finish.
6. Commit `Packages/manifest.json`, `Packages/packages-lock.json`, `Assets/XR/` and `ProjectSettings/`.

If step 2 won't let you tick OpenXR on the Mac, do steps 2–4 on a teammate's Windows machine and commit from there.

**Lab PC (Windows 10/11), once:**

- Rift S cable: **DisplayPort into the graphics card** (not the motherboard) and **USB 3.0**. The GPU needs at least a GTX 1050 Ti (GTX 1060 recommended).
- Install Meta's PC app (**Meta Horizon Link**, formerly the Oculus app), pair the Rift S, and set up a stationary (seated) boundary.
- In the Meta app: **Settings → General → OpenXR Runtime → set Meta Horizon Link as active**, and turn on **Unknown Sources** so a game from outside the Meta store can start.
- If the headset shows only black after a Meta app update, a clean reinstall of the app fixes it (Rift S is a legacy headset in that app).

**Each session:**

1. Start the Meta app with the Rift S connected, then the game (the Windows build, or Unity Play on a Windows machine).
2. The menu's **VR HEADSET** line turns green: *In the headset*. If not, press **START VR** and read the reason it gives.
3. Sit the participant down, have them look at the striker, press **V** to recentre the view, then start the level from the desktop menu.
4. **F1** shows `display VR · … · 80 Hz`; the fps must hold **80** (the Rift S refresh rate). `shots.csv` logs `camera = vr` for these shots.

**STOP VR** in the menu returns to the desktop view; pulling the cable does the same within a second.
