# Data collection: one participant in one go (about 50 min)

One command records everything the analysis needs for one person: calibration, 5 min rest, 5 min biceps, 5 min triceps, and 5 min of the game with each of the three filter profiles (handout: at least 5 min per condition). The game order rotates with the participant number automatically.

## Before the slot (once per laptop)

```
cd C:\Users\Admin\Keeper244
git pull
```

Bring: fresh electrodes (5 per person), alcohol wipes, tape, the laptop charger, a phone on a stand to film.

## For each person

| # | Who | What |
|---|---|---|
| 1 | operator | Close **BioCapture** and the **kit window**. Switch the **BioRadio on**. |
| 2 | operator | Start `Keeper244.exe`. Set **PARTICIPANT** to the new ID and **INPUT** to **EMG over UDP**. Leave it on the menu. |
| 3 | both | Electrodes (fresh, skin wiped dry): **ch1 on the right biceps** (middle of the muscle), **ch2 on the left triceps** (middle of the back of the upper arm), **GND on an elbow bone**. Press each down for 3 s. **Tape every wire** to the arm. Photo. |
| 4 | operator | PowerShell: `cd C:\Users\Admin\Keeper244\python` then `.\collect.bat`. Type the ID (P04, P05, …), press Enter twice. |
| 5 | operator | If it prints `switch the BioRadio OFF … ON`: do it, it retries by itself. If all 5 attempts fail: restart the laptop, go back to step 1. |
| 6 | participant | **Calibration** (5–10 min). Click the monitor window first. Follow the big prompt: **REST** (both arms loose) → **3× MAXIMUM BICEPS** (bend hard against resistance, full 5 s) → **3× MAXIMUM TRICEPS** (straighten hard) → **LIGHT** holds (gentle, ~1/4) → **STRONG** holds (firm, ~2/3, clearly harder). Operator: **Enter** skips the gel wait and shortens rests (wait ≥ 20 s between maximums). |
| 7 | operator | If the monitor turns **red** (`CALIBRATION PROBLEM`): electrode problem → fix it, press **r**. Light/strong too close → press **h** (redoes only the holds). |
| 8 | participant | **REST block, 5 min**: sit still, both arms relaxed. Do not talk or move. |
| 9 | participant | **BICEPS block, 5 min**: flex on each FLEX cue (5 s), relax on RELAX (5 s); the prompt says LIGHT or STRONG each time. |
| 10 | participant | **TRICEPS block, 5 min**: same, with the triceps arm. |
| 11 | both | **3 GAME blocks, 5 min each**: when the prompt says *Next: GAME*, the operator clicks the game and starts **L2**; restart L2 whenever it ends until the block's countdown finishes. The bridge switches the filter itself between blocks. Film the screen and the arms. |
| 12 | operator | Breaks (1 min) come between blocks; Enter skips them. At the end it saves and returns to the prompt. **Never close PowerShell mid-run**: press **x** in the monitor to stop early. |
| 13 | operator | Electrodes off. Next person: back to step 1 with a new ID. |

During any block: a red **⚠ … electrode loose?** line means a lead came off. Re-stick it; the bridge logs the span so the analysis can cut it.

## After the slot

Zip `C:\Users\Admin\Keeper244\data` (right-click → Send to → Compressed folder), upload to Drive, and send it. Do not put it in git (public repo, participant data).
