# Game logs (Unity side)

Every time the game starts a level for a participant, it writes to one folder per session:

```
data/sessions/<YYYYMMDD_HHMMSS>_<participant>_unity/
  session.json   participant, input, start time, Unity version, platform, device
  shots.csv      one row per shot (below)
  levels.csv     one row per finished level
```

- In the Unity Editor the root is the repo's `data/sessions/` (git-ignored; share recordings via Drive).
- In a built game it is `<persistentDataPath>/sessions` (macOS: `~/Library/Application Support/Team20/Keeper244/sessions`; Windows: `%USERPROFILE%\AppData\LocalLow\Team20\Keeper244\sessions`). The results screen has an **Open log folder** button.
- Numbers use a decimal point in every locale. Times are seconds with 3 decimals on the level's own clock (0 = level start). Booleans are `0`/`1`. An empty cell means *not applicable* (pandas reads it as NaN).
- From P4 the Python bridge keeps the authoritative session log on its clock (brief §5.4). These files let the game be tested and analysed on its own.

## shots.csv

| Column | Meaning |
|---|---|
| `participant`, `input`, `level`, `zones` | Who, which input (`Keyboard` / `EMG`), level id (`L1`…), 3 or 5 |
| `shot_id` | 1-based shot number within the level |
| `target_slot` | Target zone slot. 3-zone: −1, 0, +1. 5-zone: −2…+2. Negative = left (triceps), positive = right (biceps) |
| `keeper_slot_at_arrival` | Keeper's zone slot when the ball arrived |
| `keeper_slot5_at_arrival` | The raw 5-zone input slot at arrival (shows a "far" flex even in a 3-zone level) |
| `saved` | Keeper zone slot = target slot at arrival |
| `points`, `streak_before`, `multiplier` | Scoring (brief §3.6): 100 near/centre, 150 far (5-zone ±2), × (1 + 0.1·streak), capped at ×2; streak = saves in a row before this shot |
| `cue_time`, `kick_time`, `arrival_time` | When the target was shown, the kick, and the ball's arrival. `kick − cue` = T_cue, `arrival − kick` = T_flight |
| `reaction_time` | Cue onset → first frame on the correct side (any non-centre slot with the target's sign). Empty for centre shots or if the player never got there |
| `wrong_direction` | First move off centre went the wrong way. Centre shots: any move off centre |
| `overshoot` | Reached a slot further out than the target on the correct side (e.g. +2 for a +1 shot) |
| `undershoot` | At arrival on the correct side but not far enough (e.g. +1 for a +2 shot) |
| `time_in_target_before_arrival` | Continuous time in the target slot ending at arrival (0 if not in it at arrival) |
| `false_activations_in_wait_rest` | Times the player left rest while the game waited for the rest gate before this shot |
| `mean_a_b`, `mean_a_t` | Mean normalised biceps / triceps activation over that final hold. Empty if the target was not held at arrival |
| `camera` | Play view when the ball arrived: `behind` (behind the goal, the keeper's own view) or `front` (TV view from behind the striker). Added after the first P2 logs; older files don't have it. Compare reaction times only within one view |

Slots are always **keeper-relative**, whatever the camera: positive = the keeper's right = biceps. In the `front` view the keeper faces the player, so a positive slot appears on the *left* of the screen (the HUD and the keyboard flip to match the screen; the EMG mapping never flips).

Metrics are measured once per rendered frame (≈ 16 ms at 60 fps). P8 analysis should prefer the bridge's 100 Hz log for timing.

## levels.csv

`participant, input, level, zones, shots, saves, save_fraction, passed, score, best_streak, mean_reaction_time, ended_local`

`passed` = `save_fraction` ≥ the level's unlock fraction (70 % by default), which unlocks the next level for that participant.
