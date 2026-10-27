# Keeper244 — Setup (Phase 0)

Everyone does **A–C**. Only Aditya does **D** (creating the Unity project, once). Then everyone does **E**.

## A. Install

| Tool | macOS (Aditya) | Windows 11 (Toshika, Pratham, Niv) |
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

## Phase 0 done when

- [ ] All 4 members cloned the repo and ran `pytest` green.
- [x] `unity/Keeper244` committed (Unity 6000.3.26f1).
- [ ] All 4 opened it in 6000.3.26f1 without errors.
- [ ] Roles filled in the README.
- [ ] TA questions asked (in person).
