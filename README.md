# Keeper244

**EMG-controlled football goalkeeper in Unity.** ES244 Sensor Systems · SSRP Project #15 · Team 20.

*Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled VR Football Goalkeeping Using Unity*

Two surface-EMG channels on one arm drive a goalkeeper: biceps flex → dive right, triceps flex → dive left, relaxed → stay centre. In the 5-zone levels, how hard you flex decides near or far. A Python bridge filters the EMG, extracts the envelope, decides the keeper's slot, and streams it to Unity over UDP.

```
BioRadio (2 ch sEMG) ─► Python bridge: band-pass → notch → RMS envelope → %MVC → decision ─► UDP ─► Unity game
                                                                                      ◄── game events ──┘
```

## Team

| Name | Roll no. | Role (fill in) |
|---|---|---|
| Aditya Priyadarshi | 25110013 | |
| Toshika Kumawat | 25110326 | |
| Pratham Gupta | 25110246 | |
| Niv Mehta | 25110199 | |

Roles to split (brief §9.2): acquisition lead · DSP/analysis lead · game lead · integration + presentation lead.

## Status

| Phase | What | Status |
|---|---|---|
| P0 | Setup: repo, Unity 6.3 LTS project, Python env, TA questions | in progress |
| P1 | Game core: scene, round loop, keyboard input, 3-zone L1 | |
| P2 | Game complete: L0–L3, HUD, scoring, menus, shot logging | |
| P3 | Game networking: UDP in/out, NO SIGNAL, fake sender, Windows build | |
| P4 | Python bridge on the simulator | |
| P5 | BioRadio bring-up + first dataset | |
| P6 | Offline analysis; lock DSP parameters | |
| P7 | Real-time play with a real arm; latency | |
| P8 | 5-zone + experiments (4 participants) | |
| P9 | Results, figures, demo video, report | |

## Repo layout

```
docs/          PROJECT_BRIEF.md (full design), SETUP.md, TA_QUESTIONS.md, source_docs/ (course files)
python/        emg_bridge/ (DSP + decision + UDP), config.yaml, tests/, tools/, analysis/
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

Unity: open `unity/Keeper244` in Unity Hub with **Unity 6.3 LTS** (exact patch in `ProjectSettings/ProjectVersion.txt`).

## Key rules

- Python decides the keeper's slot; Unity only renders it.
- Online filters are causal (`sosfilt` with carried state); never `filtfilt` in real time.
- Always keep raw data.
- Tunable numbers live in `python/config.yaml` or Unity `LevelConfig` assets, never hard-coded.
- See [CONTRIBUTING.md](CONTRIBUTING.md) for git and Unity scene rules.
