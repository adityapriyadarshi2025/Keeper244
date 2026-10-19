# UDP contract (frozen in P1)

Python bridge ⇄ Unity game, over localhost UDP, one JSON object per datagram (UTF-8, no newline needed).
**Python decides the keeper's slot; Unity only renders it.**

Examples of every message: [`protocol/udp_examples.json`](../protocol/udp_examples.json). Both sides test against that file
(`python/tests/test_protocol.py`, Unity `Keeper244.Tests.EditMode/ProtocolTests`). Changing a field means changing the
Python sender, the Unity receiver, this file and the examples **in the same commit**.

| Setting | Value | Where |
|---|---|---|
| Host | `127.0.0.1` | `python/config.yaml` `network.host` |
| Python → Unity port | **5005** | `network.out_port`; Unity `Protocol.PythonToUnityPort` |
| Unity → Python port | **5006** | `network.in_port`; Unity `Protocol.UnityToPythonPort` |
| Signal timeout | **250 ms** without an `emg` packet → Unity shows NO SIGNAL | `network.signal_timeout_s`; Unity `Protocol.SignalTimeoutSeconds` |

Every message has a `type`. Receivers ignore unknown `type`s and unknown fields (forward compatible).

## Python → Unity (port 5005)

### `emg` — 100 Hz (one per envelope hop)

```json
{"type": "emg", "seq": 10234, "t": 512.34, "ab": 0.286, "at": 0.041, "slot": 1, "rest": false, "status": "ok"}
```

| Field | Type | Meaning |
|---|---|---|
| `seq` | int | Increments by 1 per packet. Unity drops any packet with `seq` ≤ the last one seen (brief §4.7) |
| `t` | float, s | Bridge monotonic clock at the envelope sample |
| `ab`, `at` | float | Normalised biceps / triceps activation (fraction of MVC), clipped to [0, 1.5] |
| `slot` | int | Decided keeper slot at 5-zone resolution, in {−2, −1, 0, +1, +2}. +1/+2 = right (biceps), −1/−2 = left (triceps). 3-zone levels use `sign(slot)` |
| `rest` | bool | Both channels below their θ_off. Drives Unity's rest gate (WAIT_REST needs `rest` true for ≥ 0.5 s) |
| `status` | string | `ok`, `no_data`, `saturated`, `flatline` (brief §4.7) |

### `calib` — sent after calibration, and again every 1 s

So the HUD can draw threshold lines on the activation bars. Arrays are `[biceps, triceps]`.

```json
{"type": "calib", "th_on": [0.10, 0.12], "th_off": [0.07, 0.084], "th_far": [0.35, 0.33]}
```

## Unity → Python (port 5006)

Python timestamps every message on arrival, so raw EMG and game events share **one clock** (brief §5.3).
`shot` is 1-based. `target` and `keeper_slot` are **zone slots** of the level (3-zone: {−1, 0, +1}; 5-zone: {−2…+2}).

| `name` | Extra fields | When |
|---|---|---|
| `level_start` | `level` (id, e.g. "L1"), `zones`, `shots` | Level begins |
| `shot_cue` | `shot`, `level`, `zones`, `target` | CUE starts (target telegraphed) |
| `kick` | `shot` | Striker kicks; FLIGHT starts |
| `arrival` | `shot`, `target`, `keeper_slot`, `saved` | Ball arrives; result decided |
| `level_end` | `level`, `shots`, `saves` | Level complete |
| `slot_applied` | `seq` | Keeper moved because of packet `seq` (latency, experiment E8) |

All of the above have `"type": "event"`. One command:

| `type` / `name` | Extra fields | When |
|---|---|---|
| `cmd` / `mark` | `label` | Operator labels a trial (e.g. artifact trials, brief §6) |

## Changes from the brief's first draft (§5.3)

- Every Python → Unity message now has `"type"` (`emg` or `calib`), so one port carries both.
- `emg` gains `rest` (Unity can't compute θ_off itself).
- New `calib` message for the HUD threshold lines.
- `level` in events is the level id string (`"L2"`), not an int; `shot_cue` gains `zones`; `arrival` gains `target`.
- New `level_start` / `level_end` events.
