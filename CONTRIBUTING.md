# Working on Keeper244

## Git

- `main` always works: Python tests pass and the Unity project opens without errors.
- Do work on a branch: `p1-round-loop`, `p4-filters`, etc. Open a pull request into `main`; one teammate glances at it.
- Pull before you start, push when you stop. Small commits with clear messages (`P1: keeper moves to slot`).
- Never commit `data/sessions/` (recordings). Share them on Drive.

## Unity (mixed macOS + Windows team)

- **Same editor everywhere:** the exact patch in `unity/Keeper244/ProjectSettings/ProjectVersion.txt`. Never "upgrade" the project unless the team agrees.
- **Always commit `.meta` files** together with their asset. A missing `.meta` breaks references for everyone else.
- **One scene owner.** Scene files merge badly. One person owns `Main.unity`; everyone else builds prefabs and scripts. If you must touch the scene, say so in the group chat first.
- Tunable numbers (timings, shot counts, speeds) live in `LevelConfig` ScriptableObject assets, not in code.
- Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` (the `.gitignore` handles it; check `git status` anyway).
- Art and audio over a few MB go through Git LFS automatically (`.gitattributes`). Run `git lfs install` once per machine.

## Python

- Code in `python/emg_bridge/`, tests in `python/tests/`, every tunable in `python/config.yaml`.
- Run `pytest` before pushing. GitHub Actions runs it on Windows, macOS and Linux.
- Use `pathlib` for paths; never hard-code `C:\...` or `/Users/...`.

## The UDP contract

Frozen in P1 (see `docs/PROJECT_BRIEF.md` §5.3). Changing a field means changing the Python sender, the Unity receiver and the brief in the same pull request.
