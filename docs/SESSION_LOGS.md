# Session logs (Python bridge)

Every `python -m emg_bridge ...` run writes one folder:

```
data/sessions/<YYYYMMDD_HHMMSS>_<participant>/
  meta.json  calibration.json  raw.csv  chunks.csv  envelope.csv  events.csv  shots.csv  [device_config.xml]
```

`data/sessions/` is git-ignored: share recordings through Drive. The game writes its own logs (`docs/GAME_LOGS.md`); these are the authoritative copy for the analysis because the raw EMG, the decisions and the game events share **one clock**.

- `t` is the bridge clock in seconds since the session started.
- Raw samples are timed by their index: `t = t_first + n / fs`, where `t_first` is the earliest time consistent with every delivery (no sample is ever timed after it arrived). Bluetooth delivers samples in bursts, so this is steadier than the arrival time.
- Numbers use a decimal point. An empty cell means not applicable (pandas reads NaN). `nan` in `raw.csv` is a sample the device reported missing.

## meta.json

Participant, `arm`, `note` (electrode sites, posture...), `command` and `mode`, the source (`kind`, `fs`, `channels`, `units`, `range_v`, and `details`: device name, MAC, firmware, battery, which BioCapture channel is which muscle, the units the SDK reported), `fs_nominal_hz`, **`fs_measured_hz`** (least-squares slope of samples vs PC time over the whole run), the full `config` snapshot, the `git` commit, and `stats` at the end: frames, packets sent, gaps, lost samples, device-reported dropped packets and pads, and the round trip summary (`emg` sent → Unity's `slot_applied`, ms).

## calibration.json

Per muscle (`biceps`, `triceps`): `mu_rest`, `sd_rest`, `e_mvc` (volts, envelope units), `sigma_n`, `th_on`, `th_off`, `th_far`, `a_light`, `a_strong`, and the two validation checks `light_ok`, `gap_ok` (brief §4.4). A run that reuses a calibration (`--calibration`) copies it here.

## raw.csv — every sample

| Column | Meaning |
|---|---|
| `n` | Sample index from the start of acquisition. A jump = samples lost on the link (also in `events.csv` as a `gap`) |
| `t` | Sample time, bridge clock |
| `biceps_uv`, `triceps_uv` | Raw signal in µV, exactly as the device gave it (no filtering) |

At 2000 Hz this is about 4 MB per minute.

## chunks.csv — one row per delivery from the source

`t_arrival` (bridge clock), `n0` (first sample index), `n` (samples), `dropped` (device's cumulative dropped-packet count). Use it to measure the real fs, the clock drift, and how bursty the link is (part of the latency budget, brief §4.6).

## envelope.csv — 100 rows per second

| Column | Meaning |
|---|---|
| `k` | Index of the last sample in the envelope window |
| `t` | Its time, bridge clock |
| `e_b_uv`, `e_t_uv` | Envelope (moving RMS of the filtered signal), µV |
| `a_b`, `a_t` | Activation, fraction of MVC, clipped to [0, 1.5]. Empty before calibration |
| `slot` | Decided slot −2…+2 (brief §4.5). The value sent to Unity |
| `rest` | 1 = both muscles below θ_off (Unity's rest gate) |
| `status` | `ok`, `no_data` (> 25 % of the window missing: the slot is held), `saturated`, `flatline` |
| `seq` | The `emg` packet's sequence number (empty when not streaming). Join with Unity's `slot_applied` for latency (E8) |

## events.csv

`t, type, name, json`. Types:

- `event` — every message from the game (`level_start`, `shot_cue`, `kick`, `arrival`, `level_end`, `shot_void`) with its fields in `json`. `slot_applied` (100+ per second) is not logged; its round trip summary is in `meta.json`.
- `cmd` — `mark` from the game (M key).
- `mark` — operator marks (`k`), BioRadio marker button (`bioradio_button`), and record-mode trials (`start:<trial>`, `stop:<trial>`).
- `calibration` — each calibration step as it starts (`json`: muscle, seconds), then `done` or `failed`.
- `gap` — samples lost (`json`: from, to, lost). `status` — `source_silent` (no samples for 150 ms: the bridge stopped sending), `signal_back`, `play`.

## shots.csv — one row per shot

Same definitions as the game's `shots.csv` (`docs/GAME_LOGS.md`), but computed from the bridge's own slots at 100 Hz:

`shot_id, level, zones, target_slot, unity_keeper_slot, unity_saved, bridge_slot_at_arrival, bridge_saved, cue_time, kick_time, arrival_time, reaction_time, wrong_direction, overshoot, undershoot, time_in_target_before_arrival, mean_a_b, mean_a_t, false_activations_between_shots, voided`

- Times are when the game's events reached the bridge.
- 3-zone levels compare slots by sign.
- `false_activations_between_shots` counts departures from centre between the previous arrival and this cue. That includes the feedback time; the game's own column counts only WAIT_REST.
- A voided shot (NO SIGNAL) gets a row with `voided = 1` and no Unity result; the replayed shot gets its own row.
