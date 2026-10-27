# Keeper244

**EMG-controlled football goalkeeper in Unity.** ES244 Sensor Systems · SSRP Project #15 · Team 20.

*Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled VR Football Goalkeeping Using Unity*

Two surface-EMG channels on one arm drive a goalkeeper: biceps flex → dive right, triceps flex → dive left, relaxed → stay centre. In the 5-zone levels, how hard you flex decides near or far. A Python bridge filters the EMG, extracts the envelope, decides the keeper's slot, and streams it to Unity over UDP.

```
BioRadio (2 ch sEMG) ─► Python bridge: band-pass → notch → RMS envelope → %MVC → decision ─► UDP ─► Unity game
                                                                                      ◄── game events ──┘
```



## Status

| Phase | What | Status |
|---|---|---|
| P0 | Setup: repo, Unity 6.3 LTS project, Python env, TA questions | done (TA questions pending) |
| P1 | Game core: scene, round loop, keyboard input, 3-zone L1 | done — playable on keyboard |
| P2 | Game complete: L0–L3, HUD, scoring, menus, shot logging | done — plus look pass and two cameras (C to switch) |
| P3 | Game networking: UDP in/out, NO SIGNAL, fake sender, Windows build, VR | in progress — UDP tested end to end; VR mode (Oculus Rift S) built, to test with the Windows build on the lab PC |
| P4 | Python bridge on the simulator | |
| P5 | BioRadio bring-up + first dataset | |
| P6 | Offline analysis; lock DSP parameters | |
| P7 | Real-time play with a real arm; latency | |
| P8 | 5-zone + experiments (4 participants) | |
| P9 | Results, figures, demo video, report | |

## Repo layout

```
docs/          PROJECT_BRIEF.md (full design), SETUP.md, PROTOCOL.md (UDP contract), GAME_LOGS.md (shots.csv columns), source_docs/ (course files)
protocol/      udp_examples.json: one example per UDP message, tested by Python and Unity
python/        emg_bridge/ (DSP + decision + UDP), config.yaml, tests/, tools/ (fake_sender.py), analysis/
unity/         Keeper244/ (Unity 6.3 LTS project, created in P0)
data/sessions/ recorded sessions (git-ignored; share via Drive)
```

## Quick start

See **[docs/SETUP.md](docs/SETUP.md)**. Short version:

```bash
git clone https://github.com/adityapriyadarshi2025/Keeper244.git
cd Keeper244/python
python3 -m venv .venv && source .venv/bin/activate   # Windows: .venv\Scripts\Activate.ps1
pip install -r requirements.txt
pytest
```

Unity: open `unity/Keeper244` in Unity Hub with **Unity 6000.3.26f1** (6.3 LTS). Install exactly this patch.

## Play without electrodes

Unity → Play → INPUT **EMG over UDP**, then in `python/`: `python tools/fake_sender.py` (keys) or `--mode auto`. Details: [docs/SETUP.md §G](docs/SETUP.md).

## Key rules

- Python decides the keeper's slot; Unity only renders it.
- Online filters are causal (`sosfilt` with carried state); never `filtfilt` in real time.
- Always keep raw data.
- Tunable numbers live in `python/config.yaml` or Unity `LevelConfig` assets, never hard-coded.
- See [CONTRIBUTING.md](CONTRIBUTING.md) for git and Unity scene rules.
