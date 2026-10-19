# Keeper244: EMG-Controlled VR Goalkeeper

**ES244 Sensor Systems · SSRP Project · Team 20 · Project #15**

Official title: *Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled VR Football Goalkeeping Using Unity*

Status as of 2026-10-19: the project is chosen and the game concept plus technical design are drafted (this file). Phase 0 (setup) is nearly done: repo `Keeper244` on GitHub, Python env + CI, Unity project created; remaining: teammates' setup, roles, TA questions. The BioRadio hardware details are still unconfirmed (see §11).

**Changelog**

| Date | Change |
|---|---|
| 2026-10-19 | Project renamed **MyoKeeper → Keeper244** (user decision). Repo, Unity project and docs use the new name |
| 2026-10-19 | Build order changed to **game first**: phases P0–P9 in the build plan doc replace milestones M0–M7 (mapping in §9.1) |
| 2026-10-19 | Unity version fixed to **Unity 6.3 LTS, pinned at 6000.3.26f1**; team platforms: Aditya on macOS, three teammates on Windows 11 (§5.1, §10, §11) |
| 2026-10-19 | fs set to 2000 Hz, 16-bit, from the BioRadio spec sheet (250–16,000 Hz, 12/16/24-bit, 190 kbps); TA question on fs dropped |
| 2026-10-19 | P2: scoring, per-shot metrics and game logs implemented as in §3.6/§3.9; exact metric definitions and file layout in `docs/GAME_LOGS.md`. HUD and menus use Unity IMGUI (code-only, no assets); a world-space HUD is needed only if VR is confirmed |
| 2026-10-19 | P1: UDP contract frozen (`docs/PROTOCOL.md`): every Python→Unity message gets a `type`, `emg` gains `rest`, new `calib`, `level_start`, `level_end`; `level` is an id string |
| 2026-10-19 | Unity project created at `unity/Keeper244` (Universal 3D template, Force Text, Visible Meta Files, Input System only) |

---

## 0. How to use this document

This file is the single source of truth for a new Claude session that will build the project. It is self-contained. The original course files are in `docs/source_docs/` and transcribed in the appendices.

| File | What it is |
|---|---|
| `docs/PROJECT_BRIEF.md` | This file: project decision, game design, signal pipeline, architecture, protocol, analysis plan, milestones |
| `docs/source_docs/ES244_Sensor_Systems_Student_Guide.xlsx` | Course guide: 3 sensor systems × 4 sheets (overview, hardware setup, noise/analysis reference, Unity integration). Transcribed in **Appendix B** |
| `docs/source_docs/SSRP_PROJECT_COMPONENT.xlsx` | Team list, the 16 example projects, sensor repository. Transcribed in **Appendix C** |
| `docs/source_docs/grading_rubric.png` | Evaluation rubric (image). Transcribed in **§1.2** and **Appendix A** |

**Tags used throughout**

| Tag | Meaning |
|---|---|
| **[DECIDED]** | Decided by the user. Don't change it without asking |
| **[PROPOSED]** | Design default chosen by a Claude session. Sensible starting point; tune freely, but keep it in config |
| **[UNVERIFIED]** | Fact not confirmed against the actual hardware or docs. Check it before relying on it |
| **[OPEN]** | Question for the team or TA (collected in §11) |

---

## 1. Context

### 1.1 The assignment

Each team must:

1. **Acquire** a physiological signal from the body using a sensor the course provides.
2. **Identify and remove noise.**
3. **Analyse** the signal in the time domain (waveform, RMS, MAV…) and the frequency domain (FFT/PSD…).
4. **Control a Unity (VR) game in real time** with the processed signal, so that the game mimics what the body does.

The course provides three sensor systems: Wii Balance Board (centre of pressure), **BioRadio sEMG** (ours), and FSR + Arduino. The course guide's EMG row suggests: band-pass, notch if needed, rectification, sliding RMS; features RMS/MAV/onset/offset; data path *BioRadio software/SDK → processing bridge → Unity*; failure handling *neutral state on connection drop, debounce triggers*; game metrics *false activations, response time, scores*. This design follows all of that (see Appendix B).

### 1.2 Grading rubric (100 marks) and how Keeper244 earns each part

| Evaluation component | Marks | How this project scores it |
|---|---|---|
| Data Acquisition and Experimental Setup | 20 | 2-channel sEMG; electrode placement following SENIAM-style guidelines; skin prep; isometric posture; a written protocol with rest, MVC, graded holds and artifact trials; fs verified from timestamps (§6) |
| Signal Analysis and Noise Identification | 20 | PSD of rest vs contraction; 50 Hz mains and harmonics; motion artifacts deliberately recorded and characterised; possible ECG pickup; crosstalk and co-activation between biceps and triceps; SNR; fatigue via median-frequency shift (§7) |
| Application of SSRP Concepts and Techniques | 20 | Sampling/Nyquist; IIR filter design (Butterworth band-pass, notch); causal vs zero-phase filtering; rectification + envelope as demodulation; window-length latency/variance trade-off; normalisation; Schmitt-trigger hysteresis; debouncing (§4, §7) |
| VR Game Development and Real-Time Signal Integration | 20 | Unity goalkeeper game with 3-zone and 5-zone levels; Python real-time bridge over UDP; calibration; failure handling; end-to-end latency measured (§3, §5) |
| Result Presentation | 20 | Raw vs filtered plots, filter responses, envelope trade-off curves, 5×5 confusion matrix, save %, reaction times, latency histogram, fatigue curves, demo video (§7) |

### 1.3 Team 20

| Name | Roll no. |
|---|---|
| Aditya Priyadarshi | 25110013 |
| Toshika Kumawat | 25110326 |
| Pratham Gupta | 25110246 |
| Niv Mehta | 25110199 |

Aditya (AI branch, 2nd year) is comfortable with Python, C++ and ML. The whole team has used Unity before. AI-assisted coding of the game is allowed: this is a signals course, so the marks are in the signal work, and the game only has to be solid.

---

## 2. Why project #15

- **Two channels (agonist/antagonist pair).** Two independent controls give a richer game and richer analysis: crosstalk, co-contraction, and a direction decision rule.
- **Reactive gameplay.** A goalkeeper must respond to a shot. Reaction time, false activations and save % fall out naturally as metrics, and these are exactly the metrics the course guide asks for.
- **The latency trade-off is visible.** Filter and envelope choices directly decide whether the ball is saved. That makes the "SSRP concepts" marks easy to demonstrate.

---

## 3. Game design

### 3.1 Core idea

The player is a goalkeeper facing penalty kicks. Two surface-EMG channels sit on **one arm**: biceps brachii and triceps brachii (dominant arm; right arm by default **[PROPOSED]**). The muscle state sets the keeper's position along the goal line. The keeper must be in the ball's zone when the ball arrives.

### 3.2 Control mapping **[DECIDED]**

**Base game (3 positions)**

| Muscle state | Keeper position |
|---|---|
| Biceps flexed | **RIGHT** |
| Triceps flexed | **LEFT** |
| Relaxed (nothing) | **CENTRE** |

**Level-up (5 positions):** two left, two right. *How hard* you flex decides near or far.

```
                        GOAL (7.32 m wide)
   ┌──────────┬──────────┬──────────┬──────────┬──────────┐
   │    L2    │    L1    │    C     │    R1    │    R2    │
   │   far    │   near   │  centre  │   near   │   far    │
   │  STRONG  │  LIGHT   │ relaxed  │  LIGHT   │  STRONG  │
   │ triceps  │ triceps  │          │  biceps  │  biceps  │
   └──────────┴──────────┴──────────┴──────────┴──────────┘
   slot:  -2        -1         0         +1         +2
   x (m): -2.928   -1.464     0.000     +1.464     +2.928
```

- Left and right are from the keeper's (player's) viewpoint, facing the striker **[PROPOSED]**.
- 5-zone slot centres sit at multiples of 7.32/5 = 1.464 m. In 3-zone mode the zone centres are at 0 and ±2.44 m (7.32/3).
- **Intensity, not duration, picks near vs far** **[DECIDED]**. Intensity means normalised EMG amplitude as a fraction of the per-session maximum voluntary contraction (%MVC).
- **Natural consequence:** while ramping up to a strong flex, the activation passes through the "light" band, so the keeper slides through R1 on the way to R2. Releasing the flex brings it back through R1 to C. This is intended and looks natural. A fast ramp that crosses the light band faster than the debounce time (§4.5) jumps straight to R2.

### 3.3 Posture: isometric control **[PROPOSED]**

The player is seated. The upper arm hangs vertical beside the trunk, the elbow is at about 90°, and the forearm rests on a table, palm up.

- **Biceps flex** = push the forearm *up* against the underside of the table (or against a fixed strap).
- **Triceps flex** = push the forearm *down* onto the table top.

**Why:** the arm barely moves. That means far fewer motion artifacts, no cable sway, and a stable muscle geometry under the electrodes. EMG amplitude then tracks effort instead of joint angle, which makes the near/far thresholds repeatable. Free (dynamic) elbow movement is an *artifact condition* to record for the noise analysis (§6), not the control method.

### 3.4 Round loop (one penalty)

```
WAIT_REST ──► CUE ──► KICK ──► FLIGHT ──► RESOLVE ──► FEEDBACK ──► WAIT_REST
```

| State | What happens | Duration (L1 default) |
|---|---|---|
| WAIT_REST | "Relax" prompt. The next shot starts only after **both channels are below θ_off for ≥ 0.5 s** (the rest gate). It also enforces muscle rest and tests a clean return to centre | ≥ 0.5 s |
| CUE | Target zone telegraphed (glowing marker on that zone; striker turns toward it). Player starts flexing | T_cue = 1.5 s |
| KICK | Striker kicks (animation event) | instant |
| FLIGHT | Ball travels to the target zone centre (± small random jitter inside the zone, for visuals only) | T_flight = 1.2 s |
| RESOLVE | At ball arrival: **save if keeper slot == target slot** (in 3-zone mode, compare 3-zone slots) | instant |
| FEEDBACK | Save/goal animation, sound, score popup | 1.5 s |

A shot targeted at centre requires the player to *stay relaxed*. That is a built-in false-activation test.

### 3.5 Levels **[PROPOSED]**

| Level | Name | Slots | Cue | T_cue | T_flight | Shots | Unlock next at |
|---|---|---|---|---|---|---|---|
| L0 | Free Play / Training | 5 | none | — | — | ∞ | — |
| L1 | Three Zones | 3 | zone marker | 1.5 s | 1.2 s | 9 (3 per zone) | ≥ 70 % saves |
| L2 | Five Zones | 5 | zone marker | 1.5 s | 1.2 s | 15 (3 per slot) | ≥ 70 % saves |
| L3 | Pro | 5 | zone marker | 0.9 s | 0.8 s | 15 | ≥ 70 % saves |
| L4 *(optional)* | Read the Kick | 5 | striker body angle + ball trajectory only | 0.6 s | 1.2 s | 15 | — |
| Endurance *(optional, analysis mode)* | — | 5 | zone marker | 1.5 s | 1.2 s | 40 | — |

- **L0** has no balls. It shows live activation bars with threshold lines and the keeper following the muscles. Use it for practice, for demos, and for checking calibration.
- **Shot targets use a balanced shuffle:** each slot appears equally often in random order, so the confusion matrix is balanced.
- **Endurance** collects fatigue data (§7, E7).

**Timing sanity check (worked):**

| Contribution | Time |
|---|---|
| Human reaction to the cue | ~200 ms (typical; not measured for this task **[UNVERIFIED]**) |
| Ramp to a held level | ~200–300 ms |
| System latency (§4.6) | ~200 ms |
| **Total minimum lead time** | **≈ 0.7 s** between cue onset and ball arrival |

L1 gives 2.7 s, which is comfortable. L3 gives 1.7 s, which is tight once you add hesitation. Tune with real data.

### 3.6 Scoring **[PROPOSED]**

| Event | Points |
|---|---|
| Save in a near or centre slot | +100 |
| Save in a far slot | +150 |

- **Streak multiplier:** ×(1 + 0.1 · streak), capped at ×2.
- False activations (leaving centre during WAIT_REST) are not penalised in score. They are shown on the HUD and logged as a metric.

### 3.7 HUD

- **Activation bars:** two vertical bars, triceps on the left and biceps on the right, matching the movement direction. Each has horizontal lines at θ_on and θ_far, so the player learns what "light" and "strong" feel like.
- **Slot strip:** a 5-cell strip showing the current slot, with the ball's target highlighted during CUE.
- **Status:** score, shot n/N, level, streak.
- **Signal indicator:** OK / NO SIGNAL (§4.7).
- **Debug overlay:** latency (ms) and packet rate (Hz), toggled with F1.

### 3.8 Presentation: desktop vs VR

- **Desktop mode is required** for development and as a fallback. It uses a third-person camera behind and above the keeper.
- **VR mode** applies only if the lab provides a headset **[OPEN]**. It uses OpenXR + the XR Interaction Toolkit.
- **Never move the VR camera with the keeper.** Sudden sideways camera motion the user didn't make causes motion sickness. Keep the VR camera fixed: either a third-person "coach view" behind the keeper, or first-person where only the keeper's gloves and avatar move across the goal.
- Meta Quest Link runs on Windows PCs only (as far as I know; confirm for the lab's headset). Three teammates are on Windows 11, so VR work happens on their machines.
- **Art:** start with primitives (a capsule keeper, a sphere ball, a box goal). Swap in free Asset Store football assets and Mixamo animations later. Gameplay first.

### 3.9 Per-shot metrics (logged; used in §7)

`shot_id, level, target_slot, keeper_slot_at_arrival, saved, cue_time, kick_time, arrival_time, reaction_time` (cue onset → first non-centre state in the correct direction), `wrong_direction` (first move opposite), `overshoot` (reached |slot| > |target|), `undershoot`, `time_in_target_before_arrival`, `false_activations_in_wait_rest`, `mean_a_b`, `mean_a_t` (during hold).

---

## 4. Signal processing pipeline

### 4.1 Overview

```
 BioRadio, 2 ch sEMG (biceps, triceps), fs = 2 kHz  (device range 250–16,000 Hz)
        │  wireless (Bluetooth)
        ▼
 EMGSource  ──  BioRadioSource | SimulatedSource | ReplaySource
        │  chunks: t[N], x[N, 2]
        ▼
 [1] Band-pass 20–450 Hz ─► [2] Notch 50 Hz (+100/150 opt.) ─► [3] Square (for RMS)
        ─► [4] Envelope: moving RMS, 100 ms window, 10 ms hop ─► [5] Normalise to %MVC
        ─► [6] Decision state machine (direction, near/far, hysteresis, debounce)
        │
        ├──► UDP, 100 Hz ──► Unity (render keeper, run game, send events back)
        └──► Logger (raw + envelope + state + game events, one clock)
```

### 4.2 Stage-by-stage defaults **[PROPOSED]** (all in `config.yaml`)

| # | Stage | Default | Why | SSRP concept shown |
|---|---|---|---|---|
| 0 | Sampling | **2000 Hz, 16-bit** | sEMG power is mostly below ~450–500 Hz, so Nyquist needs > 900 Hz; 2000 Hz gives a 2.2× margin over the 450 Hz band edge. BioRadio spec: 250–16,000 Hz, 12/16/24-bit, 190 kbps max streaming. Bandwidth check: 2 ch × 16 bit × 2000 Hz = 64 kbps (34 % of 190 kbps); 24-bit would be 96 kbps | Sampling theorem, aliasing |
| 1 | Band-pass | Butterworth, `butter(N=4, [20, 450], btype='bandpass', output='sos')` | < 20 Hz: motion artifact, baseline drift, much of any ECG energy. > 450 Hz: little EMG, mostly noise. If the device fs is < 1000 Hz, lower the top edge to about 0.45·fs | IIR design, pass/stop bands |
| 2 | Notch | `iirnotch(50, Q=30)`, −3 dB width ≈ 50/30 ≈ 1.7 Hz; optional 100/150 Hz | Indian mains is 50 Hz. Keep it only if the PSD shows a peak. Trade-off: it also removes real EMG at 50 Hz | Notch filter, Q factor |
| 3 | Rectify / square | x² for RMS (or \|x\| for MAV) | The EMG is a zero-mean carrier modulated by muscle drive. Rectifying moves the drive information to low frequency | Demodulation |
| 4 | Envelope | Moving RMS, W = 100 ms, hop 10 ms (output 100 Hz). Alternative: 2nd-order Butterworth low-pass on the rectified signal, f_c = 4–6 Hz | Smooth estimate of muscle drive. Longer window = less ripple but more delay | Moving average as a low-pass filter; latency vs variance |
| 5 | Normalise | Per-channel %MVC (§4.4) | Raw µV depends on skin, electrode and person. %MVC makes thresholds portable | Normalisation |
| 6 | Decide | Hysteresis + dominance ratio + 80 ms dwell (§4.5) | Stops chatter at thresholds, handles co-contraction, rejects spikes | Schmitt trigger, debouncing |

### 4.3 Online processing must be causal

- **Real time:** use `scipy.signal.sosfilt` with **persistent per-channel state `zi`**, carried across chunks. Initialise it with `sosfilt_zi(sos) * x0`.
- **Never use `filtfilt` online.** It needs future samples.
- **Offline analysis** may use `sosfiltfilt` (zero-phase). Comparing causal vs zero-phase output is itself an analysis item (E2): it shows the phase delay the real-time system pays.

### 4.4 Calibration (every session, every participant)

1. **Settle:** after placing the electrodes, wait ~5 min before calibrating. Electrode–skin impedance usually drops as the gel hydrates; this is common practice.
2. **Rest:** 10 s fully relaxed. For each channel k, record the envelope mean μ_rest,k and SD σ_rest,k.
3. **MVC:** 3 × 5 s maximal isometric contractions per muscle, with ≥ 60 s rest between them and verbal encouragement. e_MVC,k = the highest 500 ms moving average of the envelope across the trials.
4. **Comfort levels (5-zone only):** 3 × 3 s "light" holds and 3 × 3 s "strong" holds per muscle. Record their mean normalised levels a_light,k and a_strong,k. Coach the player to keep "strong" at about 40–60 % MVC to limit fatigue.
5. **Validate** the thresholds (formulas below). If a check fails, redo step 4 with more distinct effort levels.
6. **Save** everything to `calibration.json`.

**Normalised activation** for channel k (biceps or triceps), clipped to [0, 1.5]:

$$
a_k(t) = \frac{e_k(t) - \mu_{\text{rest},k}}{e_{\text{MVC},k} - \mu_{\text{rest},k}}
$$

**Thresholds** (per muscle). σ_n,k is the rest noise SD in normalised units:

$$
\sigma_{n,k} = \frac{\sigma_{\text{rest},k}}{e_{\text{MVC},k} - \mu_{\text{rest},k}}
$$

$$
\theta_{\text{on},k} = \max\left(0.10,\ 4\,\sigma_{n,k}\right), \qquad \theta_{\text{off},k} = 0.7\,\theta_{\text{on},k}
$$

$$
\theta_{\text{far},k} = \frac{a_{\text{light},k} + a_{\text{strong},k}}{2}, \qquad \text{hysteresis band } \pm h,\ h = 0.05
$$

**Validation checks:**

- a_light,k ≥ θ_on,k + 0.05
- a_strong,k − a_light,k ≥ 0.15

**Worked example** (illustrative numbers only, not measured):

| Quantity | Value |
|---|---|
| μ_rest | 8 µV |
| σ_rest | 2 µV |
| e_MVC | 400 µV |
| σ_n | 2/392 = 0.0051, so θ_on = max(0.10, 0.020) = 0.10 and θ_off = 0.07 |
| a_light | 0.20 |
| a_strong | 0.50 |
| θ_far | 0.35 (switch to far at 0.40, back to near at 0.30) |

A live envelope of 120 µV gives a = (120 − 8)/392 = 0.286. That is above θ_on and below 0.40, so the keeper goes to **near (slot ±1)**.

### 4.5 Decision logic (runs every 10 ms on the envelope stream)

```python
# Inputs each tick: a_b, a_t (normalised biceps/triceps), per-muscle thresholds from calibration.
# Persistent state (init: False/0): act_b, act_t, far, prev_direction, slot (output), pending, pending_since.
K       = 1.5     # dominance ratio for direction under co-contraction   [PROPOSED]
T_DWELL = 0.080   # s, candidate must persist this long before output changes  [PROPOSED]

def schmitt(x, on, off, prev):          # hysteresis comparator
    return x >= on if not prev else x > off

act_b = schmitt(a_b, th_on_b, th_off_b, act_b)
act_t = schmitt(a_t, th_on_t, th_off_t, act_t)

if act_b and (not act_t or a_b >= K * a_t):
    direction, a, th_far = +1, a_b, th_far_b        # biceps dominant  -> RIGHT
elif act_t and (not act_b or a_t >= K * a_b):
    direction, a, th_far = -1, a_t, th_far_t        # triceps dominant -> LEFT
else:
    direction = 0                                    # rest OR ambiguous co-contraction -> CENTRE

if direction == 0:
    far, cand = False, 0
else:
    if direction != prev_direction:
        far = False
    far  = schmitt(a, th_far + h, th_far - h, far)
    cand = direction * (2 if far else 1)
prev_direction = direction

if cand != pending:
    pending, pending_since = cand, now
if pending != slot and now - pending_since >= T_DWELL:
    slot = pending                                   # 5-zone output in {-2,-1,0,+1,+2}

slot3 = (slot > 0) - (slot < 0)                      # 3-zone output in {-1,0,+1}
```

| State | Condition | Slot |
|---|---|---|
| C | Neither muscle active, **or** both active with neither ≥ K× the other | 0 |
| R1 | Biceps dominant, θ_on ≤ a_b < θ_far (with hysteresis) | +1 |
| R2 | Biceps dominant, a_b ≥ θ_far | +2 |
| L1 | Triceps dominant, θ_on ≤ a_t < θ_far | −1 |
| L2 | Triceps dominant, a_t ≥ θ_far | −2 |

Python is the **single source of truth** for the slot. Unity only renders it, which keeps one logged decision per tick.

**Why both hysteresis and debounce?** A previous session simulated this exact logic at a 100 Hz tick, with θ_on = 0.10, θ_off = 0.07 and θ_far = 0.35. The test signal hovered *exactly* at θ_on with Gaussian noise of SD 0.015, for 30 s, over 50 runs:

| Variant | Mean slot changes per 30 s |
|---|---|
| Neither | ≈ 1500 |
| Hysteresis only | ≈ 130 |
| Debounce only | ≈ 2.6 |
| Both | ≈ 1.5 |

The same simulation confirmed the other behaviours:

- A step to 0.25 changes the slot exactly 80 ms later.
- A step to 0.6 goes straight to ±2.
- Co-contraction at 0.3/0.3 stays at 0.
- A 50 ms spike is ignored.
- A 0.6 s ramp from 0 to 0.6 passes 0 → 1 → 2.

### 4.6 Latency budget (worked; computed with scipy)

**Group delays of the filters:**

| Filter | Group delay |
|---|---|
| Band-pass (fs = 1000 Hz) | 12.4 ms at 30 Hz, 2.7 ms at 60 Hz, 1.1 ms at 100 Hz, 0.6 ms at 150–250 Hz. Small in the main EMG band |
| Notch | ~5 ms at 45/55 Hz, ≈ 0 elsewhere |

**Moving-RMS envelope** with window W:

$$
\tau_{\text{RMS}} \approx \frac{W}{2} = \frac{100\ \text{ms}}{2} = 50\ \text{ms}
$$

**2nd-order Butterworth low-pass envelope** (DC group delay; the digital design matches to 0.1 ms):

$$
\tau_{\text{LP}}(0) = \frac{\sqrt{2}}{2\pi f_c}
$$

| f_c | 2 Hz | 4 Hz | 5 Hz | 6 Hz | 8 Hz |
|---|---|---|---|---|---|
| τ_LP(0) | 112.5 ms | 56.3 ms | 45.0 ms | 37.5 ms | 28.1 ms |

**End-to-end budget:**

| Contribution | Estimate |
|---|---|
| Device buffering + Bluetooth | 10–50 ms **[UNVERIFIED]** |
| Band-pass + notch | ~1–5 ms |
| Envelope (RMS 100 ms) | ~50 ms |
| Hop quantisation (10 ms) | ≤ 10 ms |
| Debounce dwell | 80 ms |
| UDP on localhost | < 1 ms |
| Unity frame (60 fps) + display | ~17 + ~10–20 ms |
| **Total** | **≈ 170–235 ms → target < 250 ms**, measured in E8 |

The biggest levers are the envelope window and the dwell time. E3 measures the trade-off.

### 4.7 Failure handling

- **Signal loss:** if Unity receives no packet for 250 ms, the keeper returns to centre and a NO SIGNAL banner shows. The round pauses; the shot is voided, not counted as a goal.
- **Sensor status:** Python sets `status` (ok / no_data / saturated / flatline) and sends it in every packet. Saturation = ADC at its limits; flatline = rest SD near 0 (lead off).
- **Clamping:** activations are clipped to [0, 1.5]. Unity moves the keeper at a capped speed (adjacent slot in ~0.15 s), so there are no teleports.
- **Packet order:** Unity drops packets whose `seq` is older than the last one it saw.

---

## 5. Software architecture

### 5.1 Tech stack

| Part | Choice | Notes |
|---|---|---|
| Processing bridge | Python 3.11+, numpy, scipy, pyyaml; pyqtgraph for the live monitor **[PROPOSED]** | All DSP, calibration, decisions and logging |
| Analysis | Jupyter, numpy, scipy, pandas, matplotlib **[PROPOSED]** | Offline notebooks (§7) |
| Game | **Unity 6.3 LTS, pinned at 6000.3.26f1, Universal 3D (URP) template, C#, Input System package [DECIDED]** | 6.0 LTS support ends Oct 2026; 6.3 LTS is supported to Dec 2027. The patch is pinned in `ProjectSettings/ProjectVersion.txt`; everyone installs exactly that patch. Lab PCs only need the built `.exe`, not Unity. `UdpClient` on a background thread; `JsonUtility` for parsing |
| VR (if a headset exists) | OpenXR plugin + XR Interaction Toolkit | Fixed camera (§3.8) |
| Device access | **[UNVERIFIED]** Vendor pages mention LabVIEW and MATLAB drivers and an "open architecture". The course guide says "BioRadio software/SDK". The real route (vendor API? BioCapture streaming? LSL? MATLAB driver → UDP?) must be confirmed with the TA (§11) | Hidden behind `EMGSource`, so the rest of the system never depends on it |

### 5.2 `EMGSource` abstraction (build this first)

```python
from abc import ABC, abstractmethod
import numpy as np

class EMGSource(ABC):
    fs: float                 # nominal sample rate (verify from timestamps)
    channels: list[str]       # ["biceps", "triceps"]

    @abstractmethod
    def start(self) -> None: ...
    @abstractmethod
    def read(self) -> tuple[np.ndarray, np.ndarray]:
        """Non-blocking. Returns (t[N], x[N, C]) of all samples since the last call (N may be 0).
        t in seconds on the bridge's monotonic clock; x in volts (or device units, documented)."""
    @abstractmethod
    def stop(self) -> None: ...
```

Implementations:

1. **`SimulatedSource`.** Hardware-free development. Per channel: band-limited noise (white noise through a 20–450 Hz band-pass), multiplied by a drive envelope set from the keyboard or sliders (e.g. keys 1/2 = biceps light/strong, 3/4 = triceps light/strong; the drive ramps over ~150 ms). Then add a 50 Hz sinusoid (mains), a white noise floor, a small crosstalk fraction of the other channel (~5–10 %, tunable), and optional motion-artifact bumps (low-frequency, large amplitude). This lets the whole pipeline, the game, *and* the noise-analysis notebooks be developed before any lab time.
2. **`ReplaySource`.** Streams a recorded `raw.csv` at real-time pace, for regression tests and offline tuning on real data.
3. **`BioRadioSource`.** Real device. Implementation TBD once the access route is known.

### 5.3 Network protocol (localhost UDP, JSON) **[DECIDED — frozen in P1]**

The full contract is in **`docs/PROTOCOL.md`**; one example of every message is in **`protocol/udp_examples.json`**, which both the Python tests and the Unity EditMode tests check. Summary:

**Python → Unity:** port 5005. `emg` at 100 Hz, plus `calib` after calibration and every 1 s.

```json
{"type": "emg", "seq": 10234, "t": 512.34, "ab": 0.286, "at": 0.041, "slot": 1, "rest": false, "status": "ok"}
{"type": "calib", "th_on": [0.10, 0.12], "th_off": [0.07, 0.084], "th_far": [0.35, 0.33]}
```

Unity derives the 3-zone slot as `sign(slot)`. `rest` = both channels below θ_off (drives the rest gate). `calib` arrays are `[biceps, triceps]` (HUD threshold lines).

**Unity → Python:** port 5006, event-driven. Python timestamps every event on arrival, so the raw signal and the game events share **one clock**.

```json
{"type": "event", "name": "level_start",  "level": "L1", "zones": 3, "shots": 9}
{"type": "event", "name": "shot_cue",     "shot": 3, "level": "L2", "zones": 5, "target": -2}
{"type": "event", "name": "kick",         "shot": 3}
{"type": "event", "name": "arrival",      "shot": 3, "target": -2, "keeper_slot": -2, "saved": true}
{"type": "event", "name": "level_end",    "level": "L1", "shots": 9, "saves": 7}
{"type": "event", "name": "slot_applied", "seq": 10234}
{"type": "cmd",   "name": "mark",         "label": "artifact_trial_cable_tug"}
```

`slot_applied` echoes the `seq` of the packet that moved the keeper. It is used for latency measurement (E8).

### 5.4 Logging (every session)

```
data/sessions/<YYYYMMDD_HHMM>_<participant>/
  meta.json          # participant id, arm, electrode sites, device, fs nominal + measured, config snapshot, git hash
  calibration.json   # mu_rest, sd_rest, e_mvc, a_light, a_strong, thresholds per channel
  raw.csv            # t, biceps, triceps      (ALWAYS keep raw; never only processed)
  envelope.csv       # t, e_b, e_t, a_b, a_t, slot, status
  events.csv         # t, type, name, json_payload
  shots.csv          # one row per shot, fields from §3.9
```

### 5.5 Repo layout

```
Keeper244/
  README.md
  docs/                       PROJECT_BRIEF.md, SETUP.md, PROTOCOL.md, source_docs/
  protocol/                   udp_examples.json (shared by Python and Unity tests)
  python/
    emg_bridge/
      sources/                base.py, simulated.py, replay.py, bioradio.py
      dsp/                    filters.py (streaming SOS w/ state), envelope.py, features.py
      control/                calibration.py, decision.py (state machine §4.5)
      net/                    udp_out.py, udp_in.py
      io/                     recorder.py (session logging)
      monitor.py              live pyqtgraph view: raw, envelope, thresholds, slot
      main.py                 CLI: run | calibrate | record | replay
    config.yaml
    tests/                    test_filters.py, test_decision.py, test_udp.py
    tools/                    fake_sender.py (Phase 3)
    analysis/                 01_raw.ipynb ... 08_latency.ipynb (§7)
  unity/Keeper244/
    Assets/Scripts/           EMGReceiver.cs, IInputProvider.cs, EMGInput.cs, KeyboardInput.cs,
                              GameManager.cs (round state machine), KeeperController.cs,
                              StrikerController.cs, BallController.cs, LevelConfig.cs (ScriptableObject),
                              HUD.cs, EventSender.cs
  data/                       (git-ignored) sessions/
```

- `KeyboardInput.cs` (e.g. A/D = left/right near, Shift+A/D = far) lets the game be built and tested with no Python running.
- `IInputProvider` makes keyboard and EMG input swappable.

### 5.6 Tests (definition of "works")

- **Filters:** attenuation at 50 Hz ≥ 30 dB with the notch on; band-pass passband ripple ≈ 0; streaming (chunked) output equals single-shot `sosfilt` output on the same signal.
- **Decision:**
  - A step of a_b to 0.25 gives slot +1 after 80 ms ± one tick.
  - A step to 0.6 gives +2.
  - a_b = a_t = 0.3 (co-contraction) gives 0.
  - A signal hovering exactly at θ_on with noise SD 0.015 gives ≤ ~5 slot changes in 30 s (≈ 1.5 on average). Without hysteresis and debounce it gives ≈ 1500.
  - A 50 ms spike produces no slot change (debounce works).
- **End to end:** SimulatedSource → bridge → Unity. Pressing key "2" moves the keeper to R2. Killing the bridge triggers NO SIGNAL within 250 ms.

---

## 6. Data acquisition protocol (per participant, ~45 min)

1. **Device check.** Battery charged; fs set (≥ 1 kHz) and later verified from timestamps; both channels configured as EMG (bipolar).
2. **Skin prep.** Shave if needed; light abrasion with prep gel; alcohol wipe; let it dry.
3. **Electrode placement** (Ag/AgCl, ~20 mm inter-electrode distance, aligned with the muscle fibres). Sites follow SENIAM-style guidelines **[verify against SENIAM / BioRadio manual]**:
   - **Biceps brachii:** on the line from the medial acromion to the cubital fossa, at 1/3 of the distance from the cubital fossa.
   - **Triceps brachii (long or lateral head):** at 50 % of the line from the posterior crest of the acromion to the olecranon, 2 finger-widths medial (long head) or lateral (lateral head).
   - **Reference/ground:** a bony site such as the olecranon or the wrist (ulnar styloid), per the BioRadio manual.
   - Mark the sites with a skin marker and photograph them (for the report and for repeat sessions). Tape the leads for strain relief.
4. **Signal check.** Rest trace is flat with no 50 Hz dominance; a brief flex of each muscle gives a clear burst on its own channel; check how much appears on the other channel (crosstalk).
5. **Posture:** as in §3.3.
6. **Rest:** 30 s (longer than calibration rest, for the noise PSD).
7. **MVC:** 3 × 5 s per muscle, ≥ 60 s rest between.
8. **Graded holds:** 10, 20, 30, 50 % MVC with on-screen feedback, 5 s each, ×2. Used for amplitude-vs-effort and SNR-vs-level.
9. **Comfort holds:** light ×3 and strong ×3 per muscle (calibration step 4).
10. **Artifact trials** (labelled with the `mark` command):
    - (a) cable tug
    - (b) free elbow flexion/extension *without* resisting
    - (c) tapping near an electrode
    - (d) standing near a mains-powered device or laptop charger
11. **Game:** L0 for 2 min, then L1, then L2, then L3, with ≥ 2 min rest between levels. Recalibrate if electrodes are touched.
12. **Fatigue trial (optional, last):** sustained biceps hold at 50 % MVC for 60 s (or until the player can't hold it), or the Endurance mode.

**Data hygiene:**

- Always store raw data.
- Label every trial.
- Note fs measured from timestamps, not the nominal value (the course guide says the same).
- Record arm, side, posture, condition, and electrode sites in `meta.json`.

Participants: all 4 team members (more if allowed **[OPEN]**). Follow the lab's safety instructions for the device.

---

## 7. Analysis plan and experiments

### 7.1 Features and formulas

**RMS** over an N-sample window:

$$
\text{RMS} = \sqrt{\frac{1}{N}\sum_{n=1}^{N} x[n]^2}
$$

**MAV** (mean absolute value):

$$
\text{MAV} = \frac{1}{N}\sum_{n=1}^{N} \lvert x[n] \rvert
$$

**Median frequency (MDF):** the frequency that splits the PSD P(f) into two equal-power halves:

$$
\int_{0}^{\text{MDF}} P(f)\,df = \int_{\text{MDF}}^{f_s/2} P(f)\,df
$$

**Mean frequency (MNF):**

$$
\text{MNF} = \frac{\sum_i f_i\,P(f_i)}{\sum_i P(f_i)}
$$

**SNR** of a channel:

$$
\text{SNR}_{\text{dB}} = 20\log_{10}\frac{\text{RMS}_{\text{contraction}}}{\text{RMS}_{\text{rest}}}
$$

- **PSD:** Welch, Hann window, 1 s segments (nperseg = fs), 50 % overlap, giving 1 Hz resolution.
- **Onset detection (offline reference):** envelope > μ_rest + 3σ_rest sustained for ≥ 25 ms. This is a common threshold method; the parameters are tunable.

### 7.2 Experiments (each maps to rubric rows)

| ID | Question | Method | Output | Rubric |
|---|---|---|---|---|
| E1 | What noise is in the signal? | PSD of rest vs contraction; peaks at 50/100/150 Hz; artifact trials' spectra (< 20 Hz energy); check for periodic ~1 Hz ECG spikes; SNR per channel and per graded level | PSD plots, annotated noise table, SNR table | Signal analysis |
| E2 | What do our filters do? | Magnitude/phase/group-delay plots of the band-pass and notch; before/after PSD; causal `sosfilt` vs zero-phase `sosfiltfilt` overlay; the notch's dent in true EMG | Filter-response figure, before/after figure | SSRP concepts |
| E3 | Envelope latency vs smoothness | RMS W ∈ {50, 100, 200, 300} ms and LP f_c ∈ {2, 4, 6, 8} Hz. For each: onset delay vs the offline zero-phase reference, ripple (coefficient of variation during steady holds), false slot changes in the decision replay | Trade-off (Pareto) plot; justified final choice | SSRP + real-time |
| E4 | Does filtering matter for control? | Replay the same recordings through the decision logic using (a) raw rectified, (b) band-pass only, (c) band-pass + notch, (d) full pipeline | False activations/min, chatter count, wrong-direction rate | Signal analysis, results |
| E5 | Crosstalk and co-activation | During single-muscle holds: a_t/a_b ratio and cross-correlation peak between channels | Justifies K = 1.5 (or a better value) | Signal analysis |
| E6 | How well can people play? | L1 vs L2 vs L3 for 4 participants: save %, 5×5 confusion matrix (target vs achieved slot), reaction-time distribution, overshoot/undershoot rate, learning across attempts | Confusion matrices, bar charts, box plots | Game + results |
| E7 | Fatigue | MDF/MNF vs time during the sustained hold (linear fit slope, Hz/s); MDF across the Endurance session; does save % drop? | MDF-vs-time plot, slope table | Signal analysis + SSRP |
| E8 | End-to-end latency | Offline onset time (raw) → `slot` change time (bridge) → `slot_applied` echo (Unity); per-stage breakdown vs the §4.6 budget | Latency histogram + stacked budget bar | Real-time |
| E9 *(optional stretch)* | ML vs rules | 5-class classifier (LDA/SVM) on classic time-domain features (RMS, MAV, waveform length, zero crossings, slope sign changes) from both channels, compared with the threshold rule | Accuracy, confusion matrix, latency | SSRP (extension) |

**Fatigue pitfall worth stating in the report:** during sustained submaximal effort, the EMG amplitude needed for the *same force* tends to *rise* as the muscle fatigues, while the MDF falls. Over a long session a "light" flex can drift toward the far threshold. Mitigations: the rest gate, rest between levels, a cap on strong effort, and recalibration between levels.

---

## 8. Components (as submitted for #15 in the project form)

BioRadio wireless sEMG acquisition unit (min. 2 EMG channels); BioRadio EMG lead cables (2 bipolar channels + reference/ground); disposable pre-gelled Ag/AgCl surface electrodes; alcohol swabs and skin-prep gel; medical tape / elastic arm strap for lead strain relief; BioCapture software + BioRadio SDK/API for real-time streaming; Windows PC with Bluetooth; Unity game engine; Python (NumPy, SciPy) processing bridge; measuring tape and skin marker for electrode placement; VR headset (if provided by lab).

Useful extras (not on the form; get them if possible): a fixed strap or a table edge for isometric pushing; a razor for hair removal; spare electrodes; a phone for photos and the demo video.

---

## 9. Build plan

### 9.1 Phases (game first) **[DECIDED]**

The detailed plan lives in the "Keeper244 — Build Plan & Phases" doc. The game is built first (P1–P3) on keyboard input and a fake UDP feed; the Python bridge (P4) can run in parallel once P1 freezes the UDP contract.

| Phase | Deliverable | Was milestone |
|---|---|---|
| P0 | Setup: repo, Unity 6.3 LTS project, Python env, TA questions | M0 |
| P1 | Game core: scene, round loop, keyboard input, 3-zone L1 | part of M2 |
| P2 | Game complete: L0–L3, HUD, scoring, menus, per-shot logging | part of M2 |
| P3 | Game networking: UDP receiver, event sender, NO SIGNAL, `fake_sender.py`, VR readiness, Windows build | part of M2 |
| P4 | Python bridge on the simulator; §5.6 tests pass | M1 |
| P5 | Hardware bring-up; first full protocol dataset | M3 |
| P6 | Offline analysis v1 (E1, E2, E3, E5); lock DSP parameters | M4 |
| P7 | Real-time integration; E8 latency | M5 |
| P8 | 5-zone levels with real EMG + experiments (E4, E6, E7) | M6 |
| P9 | Results + presentation | M7 |

### 9.2 Suggested roles (team decides)

| Role | Owns |
|---|---|
| Acquisition lead | Hardware, electrode placement, protocol execution, data labelling |
| DSP/analysis lead | Filters, envelope, decision logic tuning, notebooks E1–E7 |
| Game lead | Unity scenes, gameplay, HUD, VR setup |
| Integration + presentation lead | Bridge, UDP, logging, latency (E8), final figures and slides |

---

## 10. Risks and mitigations

| Risk | Mitigation |
|---|---|
| BioRadio real-time access route unknown or awkward | Behind the `EMGSource` interface. Fallbacks in order: vendor API/SDK, then vendor software streaming (LSL if supported), then MATLAB driver → UDP to Python, then record-and-replay for development |
| Limited lab time with the BioRadio (shared across many teams) | Simulator + replay for all development; batch the data collection; arrive with a printed protocol checklist |
| Mixed platforms (Aditya on macOS, three teammates on Windows 11); vendor software / Quest Link likely Windows-only | Unity and Python are cross-platform and develop fine on the Mac; acquisition and VR run on a teammate's Windows laptop or a lab PC; `.gitattributes` normalises line endings |
| Co-contraction makes direction ambiguous | Dominance ratio K, per-muscle normalisation, practice in L0; quantify it in E5 |
| Fatigue shifts amplitudes | Rest gate, cap strong effort at ~40–60 % MVC, rest between levels, recalibrate |
| Electrode shift / sweat during the session | Tape and strap the leads; skin-marker sites; recalibrate after any disturbance |
| Latency too high for L3 | Shorter RMS window or higher LP f_c, shorter dwell; E3 picks the operating point |
| VR motion sickness | Fixed camera (§3.8) |

---

## 11. Open questions

1. **VR headset [OPEN]:** does the lab provide one (which model)? Is a desktop 3D game acceptable if not? The rubric row says "VR Game Development".
2. **BioRadio [OPEN]:** exact model, number of channels, max sample rate, resolution, battery. **How do we get live data into our own code** (vendor SDK and its language, BioCapture streaming/LSL, MATLAB/LabVIEW driver)? Which OS? How many units, what lab hours, and can it leave the lab?
3. **Windows machine — answered:** three teammates are on Windows 11.
4. **Unity / C# experience — answered:** the whole team has used Unity; AI-assisted game coding is allowed.
5. **Deadlines and deliverables [OPEN]:** final demo date, report/slides format, whether a video is required, and any intermediate check-ins.
6. **Participants [OPEN]:** only team members, or others too? Is any consent/ethics form required by the course?

---

## 12. Instructions for the next Claude session (builder)

1. Read this whole file first. **Respect [DECIDED] items.** Treat [PROPOSED] items as defaults (keep them in `config.yaml` or Unity ScriptableObjects, never hard-coded). **Never state [UNVERIFIED] items as fact.**
2. Ask the user the §11 questions that affect the *current* phase, but **don't block P1–P4 on them**.
3. Build order: P1–P3 (Unity game on keyboard + fake UDP feed), P4 (Python bridge + simulator + tests), then the hardware-dependent phases.
4. Online DSP must be causal (persistent filter state across chunks). Always log raw data. Python decides the slot; Unity renders it.
5. **About the user (Aditya):**
   - Types short and casual; read the intent.
   - Wants direct, dense answers, tables for comparisons, worked numbers, and the *why* behind design choices.
   - Wants uncertainty flagged explicitly and no invented data.
   - Short commands like "next" mean proceed immediately.
6. When something here turns out wrong on real hardware (fs, latency, thresholds), update the design and say what changed and why.

---

## Appendix A: Grading rubric (transcribed from `grading_rubric.png`)

| Evaluation Component | Marks |
|---|---|
| Data Acquisition and Experimental Setup | 20 |
| Signal Analysis and Noise Identification | 20 |
| Application of SSRP Concepts and Techniques | 20 |
| VR Game Development and Real-Time Signal Integration | 20 |
| Result Presentation | 20 |
| **Total Marks** | **100** |

## Appendix B: ES244 Sensor Systems Student Guide (verbatim transcription of `ES244_Sensor_Systems_Student_Guide.xlsx`)

All three sensor systems are kept for context; **our project uses the BioRadio sEMG rows.**

### B.1 Sheet: System Overview

| System | Signal measured | Sensing hardware | Acquisition interface | Computer connection | Typical outputs | Unity control | Important note | Link |
|---|---|---|---|---|---|---|---|---|
| Wii Balance Board | Vertical loads; calculated CoP AP and ML | Four corner load sensors | WBB internal electronics | Bluetooth via compatible software | 4 corner loads, CoP X/Y, timestamps | Body lean moves car / player | CoP is computed from corner loads; log timestamps | https://github.com/tomvredeveld/wii-balance-board-recorder |
| BioRadio sEMG | Muscle electrical activity (voltage) | Surface electrodes on target muscle | BioRadio EMG-compatible inputs | BioRadio wireless link with bluetooth / supported acquisition software | Raw EMG, filtered EMG, RMS/MAV, activation | Muscle activation controls speed, direction, actions | Confirm model, channels, sample rate, SDK/software availability | https://www.glneurotech.com/products/bioradio/ |
| FSR + Arduino | Pinch force proxy (ADC / calibrated N) | FSR e.g. FlexiForce; interface resistor/circuit | Arduino ADC | USB serial | ADC count, voltage, filtered force proxy | Pinch raises/lowers ball |  | https://www.tekscan.com/blog/flexiforce/differences-between-force-sensitive-resistor-technologies-assessing-linearity-drift |

### B.2 Sheet: Hardware Setup

| System | Hardware required | Placement / wiring | Setup steps | Safety / checks | Sampling guidance | What to record |
|---|---|---|---|---|---|---|
| Wii Balance Board | WBB, batteries, Bluetooth-capable PC | Both feet on board; stable level floor | Pair Bluetooth; verify four corner loads; zero with no load; capture quiet standing | Clear surroundings; spotter for standing tasks | Measure actual rate from timestamps; avoid assuming fixed rate | Raw corner loads, calculated CoP, timestamps, trial labels |
| BioRadio sEMG | BioRadio, electrodes, leads, PC, acquisition software | Electrodes over muscle belly according to placement guidance; reference as specified by device | Check device/software; clean skin; connect channels; verify rest/contraction trace | Use approved isolated physiological acquisition equipment; follow device instructions | Choose EMG-appropriate sampling rate based on device capability; record actual value | Raw voltage, timestamps, muscle, side, posture, condition |
| FSR | Arduino interface with FSR, Arduino, suitable interface circuit, USB cable, PC | Sensor between finger/thumb pads with mechanical protection; appropriate divider/conditioning | Connect analog input; inspect ADC readings; record baseline and multiple force levels | Do not overload sensing area; confirm supply voltage and wiring | Choose and document a consistent acquisition rate | ADC count, voltage, calibration condition, timestamps |

### B.3 Sheet: For reference

| System | Noise/artifacts to inspect | Time-domain analysis | Frequency-domain analysis | Online processing | Extracted features | Compare / evaluate |
|---|---|---|---|---|---|---|
| Wii Balance Board | Load jitter, spikes, drift, timing irregularities; natural sway is not noise | CoP trace, mean, SD, path, response | FFT/PSD for motion vs high-frequency components | Causal moving average and IIR low-pass; optional dead zone | CoP AP/ML, variability, filtered position | Raw vs filtered jitter, latency, steering accuracy |
| BioRadio sEMG | Motion artifacts, power-line interference, electrode contact noise | Waveform, rectified envelope, RMS, MAV | EMG spectrum / PSD; check 50 Hz interference | Appropriate band-pass, notch if needed, rectification and sliding RMS | RMS, MAV, onset/offset, activation duration | Rest vs contraction, false triggers, delay, task performance |
| FSR + Arduino | ADC quantization, electrical noise, mechanical hysteresis/drift | ADC/voltage traces, mean, SD, step response | FFT/PSD if useful for noise characterization | Median/outlier rejection and causal low-pass smoothing | Baseline, normalized pressure, calibrated force if available | Unfiltered vs filtered ball jitter, overshoot, delay |

### B.4 Sheet: Unity Integration

| System | Suggested game | Input feature | Mapping | Calibration | Real-time data path | Failure handling | Game metrics |
|---|---|---|---|---|---|---|---|
| Wii Balance Board | Car racing / balloon dodge | Filtered CoP X,Y | X controls steering; Y optional second axis | Quiet stance center; reachable lean limits | Bluetooth acquisition -> Python bridge -> UDP/TCP -> Unity | Hold safe neutral on signal loss; clamp range | Collisions, target hits, time, movement delay |
| BioRadio sEMG | Car, balloon, football | Filtered EMG RMS / activation events | One muscle controls one action; two channels for two controls | Resting baseline and comfortable contraction reference | BioRadio software/SDK -> processing bridge -> Unity | Neutral state when connection drops; debounce triggers | False activations, response time, scores |
| FSR + Arduino | Pinch-controlled rising ball | Filtered normalized pinch reading | Increasing pinch moves ball up; releasing moves down | No-force baseline; comfortable pinch reference | Arduino USB serial -> PC processing / Unity | Clamp limits and avoid sudden jumps | Target error, stability, completion time |

## Appendix C: SSRP Project Component sheet (transcription of `SSRP_PROJECT_COMPONENT.xlsx`)

### C.1 Sheet: EXAMPLE_PROJECT (the 16 given projects; **we chose #15**)

| Project No. | Title of the Project | Sensor to be Used |
|---|---|---|
| 1 | Real-Time Center of Pressure Signal Processing for Body-Controlled Car Racing Using Wii Balance Board and Unity | Wii Balance Board (CoP) |
| 2 | Real-Time CoP Signal Processing for Body-Controlled Balloon Dodging Using Wii Balance Board and Unity | Wii Balance Board (CoP) |
| 3 | Real-Time CoP Signal Processing for Body-Controlled Balloon-Bursting Using Wii Balance Board and Unity | Wii Balance Board (CoP) |
| 4 | Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled Watering of Garden Using Unity | EMG Sensors (Biceps and Triceps) |
| 5 | Real-Time EMG Signal Processing for Hand-Grasp-Controlled VR Object Manipulation Using Unity | EMG Sensors (Flexor Carpi Radialis, Both Hands) |
| 6 | Real-Time EMG Signal Processing for Shoulder-Controlled VR Object Movement Using Unity | EMG Sensor (Trapezius) |
| 7 | Real-Time EMG Signal Processing for Biceps-Controlled VR Weightlifting Using Unity | EMG Sensor (Biceps) |
| 8 | Real-Time EMG Signal Processing with Calf Muscle for Speed-Controlled VR Walking Using Unity | EMG Sensor (Calf Muscle) |
| 9 | Real-Time EMG Signal Processing for VR Walking Control During Overground Walking Using Unity | EMG Sensor (Calf Muscle) |
| 10 | Real-Time EMG Signal Processing for Muscle-Controlled VR Walking and Heel Raising While Manipulating VR Objects Using Unity | EMG Sensor (Calf Muscle) |
| 11 | Real-Time Triceps EMG Signal Processing for VR Basketball Throwing Using Unity | EMG Sensor (Triceps) |
| 12 | Real-Time EMG Signal Processing for Forearm Muscle-Controlled VR Target Shooting Using Unity | EMG Sensor (Forearm Muscles) |
| 13 | Real-Time EMG Signal Processing for Calf Muscle-Controlled Balloon Flying Using Unity | EMG Sensor (Calf Muscle) |
| 14 | Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled VR Football Game Using Unity | EMG Sensors (Biceps and Triceps) |
| 15 | Real-Time EMG Signal Processing for Biceps and Triceps Muscle-Controlled VR Football Goalkeeping Using Unity | EMG Sensors (Biceps and Triceps) |
| 16 | Real-Time FSR Signal Processing for Finger Pinch-Controlled VR Ball Movement Using Unity | Force Sensitive Resistor (FSR) Sensor |

### C.2 Sheet: SENSOR_REPOSITORY

| Available Sensor/ Experiment Setup | Links |
|---|---|
| Wii Balance Board for COP | Wii Balance Board - Wikipedia |
| FSR Sensor | https://robu.in/product/flexiforce-a301-100-force-sensor/ |
| EMG Sensor | https://www.glneurotech.com/products/bioradio/ |

Link given for the Wii Balance Board in the guide: https://github.com/tomvredeveld/wii-balance-board-recorder

### C.3 Sheet: TEAMS (our row only; the full 32-team roster is in the original xlsx)

| Groups | Team Member 1 Name | Team Member 1 Roll Number | Team Member 2 Name | Team Member 2 Roll Number | Team Member 3 Name | Team Member 3 Roll Number | Team Member 4 Name | Team Member 4 Roll Number |
|---|---|---|---|---|---|---|---|---|
| 20 | Aditya Priyadarshi | 25110013 | Toshika Kumawat | 25110326 | Pratham Gupta | 25110246 | Niv Mehta | 25110199 |

## Appendix D: Reference pointers

| What | Where | Note |
|---|---|---|
| BioRadio product page | https://www.glneurotech.com/products/bioradio/ | Check the model, channels, fs and SDK for the lab's unit |
| SENIAM electrode placement | http://seniam.org (sensor locations) | Verify the biceps/triceps sites in §6 |
| Lab Streaming Layer (LSL) | https://labstreaminglayer.org | Only relevant if the BioRadio software supports LSL **[UNVERIFIED]** |
| scipy.signal | `butter`, `iirnotch`, `sosfilt`, `sosfilt_zi`, `sosfiltfilt`, `welch`, `group_delay` | DSP functions used in this design |
| Unity 6 releases | https://unity.com/unity-6-release | 6.3 LTS supported to Dec 2027; 6.0 LTS to Oct 2026 |

*End of brief.*
