"""Live monitor (pyqtgraph): filtered EMG, activations with threshold lines, the decided slot,
and the calibration prompt in large type. The bridge loop runs in a background thread; this
window only reads `Bridge.snapshot()` (30 Hz) and forwards key presses to `Bridge.on_key()`.
"""

from __future__ import annotations

import threading
from typing import Callable

import numpy as np

BICEPS, TRICEPS = "#f08c00", "#4dabf7"


def run_monitor(bridge, title: str, help_text: str, run_kwargs: dict | None = None,
                on_tick: Callable[[object], None] | None = None) -> None:
    import pyqtgraph as pg
    from pyqtgraph.Qt import QtCore, QtGui, QtWidgets

    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    pg.setConfigOptions(antialias=True)
    refresh_hz = float(bridge.cfg.get("monitor", {}).get("refresh_hz", 30))
    window_s = float(bridge.cfg.get("monitor", {}).get("window_s", 5))

    class Window(QtWidgets.QWidget):
        def __init__(self):
            super().__init__()
            self.setWindowTitle(title)
            self.resize(1100, 820)
            lay = QtWidgets.QVBoxLayout(self)
            self.prompt = QtWidgets.QLabel("starting...")
            f = self.prompt.font()
            f.setPointSize(20)
            f.setBold(True)
            self.prompt.setFont(f)
            self.prompt.setWordWrap(True)
            lay.addWidget(self.prompt)
            mono = QtGui.QFontDatabase.systemFont(QtGui.QFontDatabase.SystemFont.FixedFont)
            self.status = QtWidgets.QLabel("")
            self.status.setFont(mono)
            lay.addWidget(self.status)
            glw = pg.GraphicsLayoutWidget()
            lay.addWidget(glw, 1)
            self.p_sig = glw.addPlot(row=0, col=0, title="Filtered EMG (µV)")
            self.p_act = glw.addPlot(row=1, col=0, title="Activation (fraction of MVC)")
            self.p_slot = glw.addPlot(row=2, col=0, title="Keeper slot (− left / triceps, + right / biceps)")
            for pl in (self.p_sig, self.p_act, self.p_slot):
                pl.showGrid(x=True, y=True, alpha=0.2)
                pl.setXRange(-window_s, 0, padding=0)
                pl.addLegend(offset=(10, 5))
            self.p_act.setXLink(self.p_sig)
            self.p_slot.setXLink(self.p_sig)
            self.p_slot.setYRange(-2.3, 2.3)
            self.p_slot.getAxis("left").setTicks([[(-2, "L2"), (-1, "L1"), (0, "C"), (1, "R1"), (2, "R2")]])
            self.c_sig_b = self.p_sig.plot(pen=pg.mkPen(BICEPS, width=1), name="biceps")
            self.c_sig_t = self.p_sig.plot(pen=pg.mkPen(TRICEPS, width=1), name="triceps")
            self.c_act_b = self.p_act.plot(pen=pg.mkPen(BICEPS, width=2), name="biceps", connect="finite")
            self.c_act_t = self.p_act.plot(pen=pg.mkPen(TRICEPS, width=2), name="triceps", connect="finite")
            self.c_slot = self.p_slot.plot(pen=pg.mkPen("w", width=2), stepMode=None)
            self.lines: list = []
            self.cal_shown = None
            self.log = QtWidgets.QLabel("")
            self.log.setFont(mono)
            self.log.setStyleSheet("color: #aaa;")
            self.log.setWordWrap(True)
            lay.addWidget(self.log)
            hl = QtWidgets.QLabel(help_text)
            hl.setWordWrap(True)
            hl.setStyleSheet("color: #888;")
            lay.addWidget(hl)
            self.timer = QtCore.QTimer(self)
            self.timer.timeout.connect(self.refresh)
            self.timer.start(int(1000 / refresh_hz))

        def keyPressEvent(self, ev: QtGui.QKeyEvent) -> None:  # noqa: N802 (Qt name)
            key = ev.key()
            if key == QtCore.Qt.Key.Key_Escape:
                self.close()
            elif key in (QtCore.Qt.Key.Key_Return, QtCore.Qt.Key.Key_Enter):
                bridge.on_key("\n")
            elif ev.text():
                if ev.text().lower() == "x":
                    self.close()
                else:
                    bridge.on_key(ev.text())

        def _threshold_lines(self, cal) -> None:
            for ln in self.lines:
                self.p_act.removeItem(ln)
            self.lines = []
            if cal is None:
                return
            for c, colour in ((cal.biceps, BICEPS), (cal.triceps, TRICEPS)):
                for value, style in ((c.th_on, QtCore.Qt.PenStyle.SolidLine), (c.th_off, QtCore.Qt.PenStyle.DotLine),
                                     (c.th_far, QtCore.Qt.PenStyle.DashLine)):
                    ln = pg.InfiniteLine(pos=value, angle=0, pen=pg.mkPen(colour, width=1, style=style))
                    self.p_act.addItem(ln)
                    self.lines.append(ln)
            self.p_act.setYRange(0, 1.0)

        def refresh(self) -> None:
            if on_tick is not None:
                on_tick(bridge)
            if not bridge.started:
                return
            s = bridge.snapshot()
            now = bridge.now()
            sig, fr = s["signal"], s["frames"]
            if len(sig):
                t = sig[:, 0] - now
                self.c_sig_b.setData(t, sig[:, 1] * 1e6)
                self.c_sig_t.setData(t, sig[:, 2] * 1e6)
            cal = s["calibration"]
            if cal is not self.cal_shown:
                self._threshold_lines(cal)
                self.cal_shown = cal
                self.p_act.setTitle("Activation (fraction of MVC)" if cal else "Envelope (µV): not calibrated yet")
            if len(fr):
                t = fr[:, 0] - now
                if cal is not None and np.isfinite(fr[-1, 3]):
                    self.c_act_b.setData(t, fr[:, 3])
                    self.c_act_t.setData(t, fr[:, 4])
                else:
                    self.c_act_b.setData(t, fr[:, 1] * 1e6)
                    self.c_act_t.setData(t, fr[:, 2] * 1e6)
                self.c_slot.setData(t, fr[:, 5])
            prompt = s["prompt"]
            trial, open_ = s["trial"]
            if not prompt and s["mode"] == "record" and trial:
                prompt = f"RECORDING  ·  trial {open_} — Enter to stop" if open_ else f"next trial: {trial}  ·  Enter to start, [ ] to choose"
            if not prompt and s["mode"] == "play":
                prompt = "PLAYING · streaming to Unity"
            if s["silent"]:
                prompt = "NO SAMPLES from the source · Unity shows NO SIGNAL"
            self.prompt.setText(prompt or s["mode"])
            fs_m = s["fs_measured"]
            line = bridge.status_line()
            if fs_m:
                line += f"  ·  fs measured {fs_m:.1f} Hz"
            if s["player"]:
                line += f"  ·  sim {s['player']}"
            self.status.setText(line)
            self.log.setText("\n".join(s["messages"][-6:]))
            if bridge.stop_requested:
                self.close()

        def closeEvent(self, ev) -> None:  # noqa: N802
            bridge.stop()
            ev.accept()

    win = Window()
    win.show()
    worker = threading.Thread(target=bridge.run, kwargs=run_kwargs or {}, daemon=True, name="bridge")
    worker.start()
    app.exec()
    bridge.stop()
    worker.join(timeout=5)
