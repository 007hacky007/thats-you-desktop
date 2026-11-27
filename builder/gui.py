"""The builder window: Game, Phone app, Phone page, Unity, Build.

Every check and the build itself come in through Services, so the window can be tested offscreen
with fakes. The build runs on a worker thread and reports through Qt signals.
"""
from __future__ import annotations

import os
import subprocess
import sys
import threading
import webbrowser
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PySide6.QtCore import QLocale, QObject, Qt, Signal
from PySide6.QtGui import QIcon
from PySide6.QtWidgets import (QApplication, QButtonGroup, QCheckBox, QComboBox, QFileDialog, QHBoxLayout,
                               QLabel, QLineEdit, QPlainTextEdit, QProgressBar, QPushButton, QRadioButton,
                               QStackedWidget, QVBoxLayout, QWidget)

from builder import texts

PAGES = ("game", "apk", "hosting", "unity", "build")
HUB_DOWNLOAD = "https://unity.com/download"


@dataclass
class Services:
    check_game: Callable
    check_apk: Callable
    unity_status: Callable[[], str]
    hub_installed: Callable[[], bool]
    open_url: Callable[[str], None]
    build: Callable
    cf_login: Callable[[], bool] = lambda: True
    open_hub: Callable[[], None] = lambda: None
    open_path: Callable[[Path], None] = lambda p: None


def real_services() -> Services:
    from builder import steps
    from pipeline import inputs, unity_install

    def open_path(p: Path) -> None:
        if sys.platform == "darwin":
            subprocess.Popen(["open", str(p)])
        else:
            os.startfile(str(p))  # noqa: S606

    return Services(check_game=inputs.check_game, check_apk=inputs.check_apk,
                    unity_status=lambda: unity_install.status(),
                    hub_installed=lambda: unity_install.hub_exe().is_file(),
                    open_url=webbrowser.open, build=steps.build,
                    open_hub=lambda: unity_install.open_hub_for_sign_in(), open_path=open_path)


class _Bridge(QObject):
    progress = Signal(float, str)
    log = Signal(str)
    finished = Signal(object, str)


class Window(QWidget):
    def __init__(self, services: Services, lang: str = "en"):
        super().__init__()
        self.s = services
        self.lang = lang
        self.t = texts.TEXTS[lang]
        self.games: dict[str, object] = {"base": None, "update": None}
        self.game_paths: dict[str, Path | None] = {"base": None, "update": None}
        self.apk: Path | None = None
        self.apk_ok = False
        self.unity_state = "unknown"
        self.output: Path | None = None
        self.bridge = _Bridge()
        self.bridge.progress.connect(self._on_progress)
        self.bridge.log.connect(self._on_log)
        self.bridge.finished.connect(self._on_finished)
        self._build_ui()
        self.go("game")

    # ------------------------------------------------------------ layout
    def _label(self, text: str, heading: bool = False) -> QLabel:
        lab = QLabel(text)
        lab.setWordWrap(True)
        if heading:
            f = lab.font()
            f.setPointSize(f.pointSize() + 5)
            f.setBold(True)
            lab.setFont(f)
        return lab

    def _build_ui(self) -> None:
        t = self.t
        self.setWindowTitle(t["title"])
        self.resize(760, 520)
        root = QVBoxLayout(self)
        top = QHBoxLayout()
        top.addWidget(self._label(t["intro"]), 1)
        self.lang_box = QComboBox()
        self.lang_box.addItems(["English", "Čeština"])
        self.lang_box.setCurrentIndex(0 if self.lang == "en" else 1)
        self.lang_box.setToolTip(t["lang"])
        top.addWidget(self.lang_box)
        root.addLayout(top)
        self.stack = QStackedWidget()
        root.addWidget(self.stack, 1)
        self.pages = {}
        for name in PAGES:
            page = QWidget()
            lay = QVBoxLayout(page)
            getattr(self, "_page_" + name)(lay)
            lay.addStretch(1)
            self.stack.addWidget(page)
            self.pages[name] = page
        nav = QHBoxLayout()
        self.back_button = QPushButton(t["back"])
        self.next_button = QPushButton(t["next"])
        self.back_button.clicked.connect(lambda: self.go(PAGES[max(0, self.page_index - 1)]))
        self.next_button.clicked.connect(lambda: self.go(PAGES[min(len(PAGES) - 1, self.page_index + 1)]))
        nav.addWidget(self.back_button)
        nav.addStretch(1)
        nav.addWidget(self.next_button)
        root.addLayout(nav)

    def _page_game(self, lay) -> None:
        t = self.t
        lay.addWidget(self._label(t["game.heading"], True))
        lay.addWidget(self._label(t["game.note"]))
        self.game_status = {}
        for role in ("base", "update"):
            row = QHBoxLayout()
            row.addWidget(QLabel(t["game." + role]))
            folder = QPushButton(t["game.folder"])
            pkg = QPushButton(t["game.pkg"])
            folder.clicked.connect(lambda _=False, r=role: self._pick_game(r, folder=True))
            pkg.clicked.connect(lambda _=False, r=role: self._pick_game(r, folder=False))
            row.addWidget(folder)
            row.addWidget(pkg)
            row.addStretch(1)
            lay.addLayout(row)
            st = self._label(t["game.none"])
            self.game_status[role] = st
            lay.addWidget(st)
        self.try_untested = QCheckBox(t["game.try"])
        self.try_untested.setVisible(False)
        self.try_untested.toggled.connect(lambda _: self._update_nav())
        lay.addWidget(self.try_untested)

    def _page_apk(self, lay) -> None:
        t = self.t
        lay.addWidget(self._label(t["apk.heading"], True))
        lay.addWidget(self._label(t["apk.note"]))
        row = QHBoxLayout()
        pick = QPushButton(t["apk.pick"])
        pick.clicked.connect(self._pick_apk)
        self.find_apk = QPushButton(t["apk.find"])
        from pipeline import inputs
        self.find_apk.clicked.connect(lambda: self.s.open_url(inputs.APK_SEARCH_URL))
        row.addWidget(pick)
        row.addWidget(self.find_apk)
        row.addStretch(1)
        lay.addLayout(row)
        self.apk_status = self._label(t["apk.none"])
        lay.addWidget(self.apk_status)
        lay.addSpacing(16)
        self.no_apk = QCheckBox(t["apk.skip"])
        self.disclaimer = self._label(t["apk.disclaimer"])
        self.disclaimer.setVisible(False)
        self.no_apk.toggled.connect(lambda on: (self.disclaimer.setVisible(on), self._update_nav()))
        lay.addWidget(self.no_apk)
        lay.addWidget(self.disclaimer)

    def _page_hosting(self, lay) -> None:
        t = self.t
        lay.addWidget(self._label(t["host.heading"], True))
        lay.addWidget(self._label(t["host.note"]))
        self.use_tunnel = QRadioButton(t["host.tunnel"])
        self.use_account = QRadioButton(t["host.account"])
        group = QButtonGroup(self)
        group.addButton(self.use_tunnel)
        group.addButton(self.use_account)
        self.use_tunnel.setChecked(True)
        lay.addWidget(self.use_tunnel)
        lay.addWidget(self._label("    " + t["host.tunnel.note"]))
        lay.addWidget(self.use_account)
        lay.addWidget(self._label("    " + t["host.account.note"]))

    def _page_unity(self, lay) -> None:
        t = self.t
        lay.addWidget(self._label(t["unity.heading"], True))
        lay.addWidget(self._label(t["unity.note"]))
        self.unity_status_label = self._label("")
        lay.addWidget(self.unity_status_label)
        row = QHBoxLayout()
        dl = QPushButton(t["unity.download"])
        dl.clicked.connect(lambda: self.s.open_url(HUB_DOWNLOAD))
        hub = QPushButton(t["unity.open"])
        hub.clicked.connect(self.s.open_hub)
        again = QPushButton(t["unity.check"])
        again.clicked.connect(self.refresh_unity)
        for b in (dl, hub, again):
            row.addWidget(b)
        row.addStretch(1)
        lay.addLayout(row)

    def _page_build(self, lay) -> None:
        t = self.t
        lay.addWidget(self._label(t["build.heading"], True))
        lay.addWidget(self._label(t["build.note"]))
        row = QHBoxLayout()
        row.addWidget(QLabel(t["build.out"]))
        self.out_edit = QLineEdit(str(Path.home() / "That's You"))
        row.addWidget(self.out_edit, 1)
        choose = QPushButton(t["build.choose"])
        choose.clicked.connect(self._pick_out)
        row.addWidget(choose)
        lay.addLayout(row)
        self.start_button = QPushButton(t["build.start"])
        self.start_button.clicked.connect(self.start_build)
        lay.addWidget(self.start_button)
        self.progress = QProgressBar()
        self.progress.setRange(0, 100)
        lay.addWidget(self.progress)
        self.build_status = self._label("")
        lay.addWidget(self.build_status)
        row = QHBoxLayout()
        self.toggle_details = QPushButton(t["build.details"])
        self.toggle_details.setCheckable(True)
        self.open_game = QPushButton(t["build.open"])
        self.open_game.setEnabled(False)
        self.open_game.clicked.connect(lambda: self.output and self.s.open_path(self.output))
        row.addWidget(self.toggle_details)
        row.addStretch(1)
        row.addWidget(self.open_game)
        lay.addLayout(row)
        self.details = QPlainTextEdit()
        self.details.setReadOnly(True)
        self.details.setVisible(False)
        self.toggle_details.toggled.connect(self.details.setVisible)
        lay.addWidget(self.details, 1)

    # ------------------------------------------------------------ navigation
    @property
    def page_index(self) -> int:
        return self.stack.currentIndex()

    def go(self, name: str) -> None:
        self.stack.setCurrentWidget(self.pages[name])
        if name == "unity" and self.unity_state == "unknown":
            self.refresh_unity()
        self._update_nav()

    def _page_ok(self, name: str) -> bool:
        if name == "game":
            infos = [self.games[r] for r in ("base", "update")]
            if not all(infos):
                return False
            return all(i.supported for i in infos) or self.try_untested.isChecked()
        if name == "apk":
            return self.apk_ok or self.no_apk.isChecked()
        if name == "unity":
            return self.unity_state in ("ready", "missing")
        return True

    def _update_nav(self) -> None:
        name = PAGES[self.page_index]
        self.back_button.setEnabled(self.page_index > 0)
        self.next_button.setVisible(name != "build")
        self.next_button.setEnabled(self._page_ok(name))
        untested = any(i is not None and not i.supported for i in self.games.values())
        self.try_untested.setVisible(untested)

    # ------------------------------------------------------------ inputs
    def _pick_game(self, role: str, folder: bool) -> None:
        if folder:
            path = QFileDialog.getExistingDirectory(self, self.t["game." + role])
        else:
            path, _ = QFileDialog.getOpenFileName(self, self.t["game." + role], "", "PS4 package (*.pkg)")
        if path:
            self.set_game(role, Path(path))

    def set_game(self, role: str, path: Path) -> None:
        self.game_paths[role] = path
        try:
            info = self.s.check_game(path, role)
            self.games[role] = info
            key = "game.ok" if info.supported else "game.untested"
            self.game_status[role].setText(f"{path.name}: " + self.t[key].format(
                title=info.title, version=info.version, cid=info.content_id))
        except Exception as e:  # noqa: BLE001 - shown to the player
            self.games[role] = None
            self.game_status[role].setText(f"{path.name}: {e}")
        self._update_nav()

    def _pick_apk(self) -> None:
        path, _ = QFileDialog.getOpenFileName(self, self.t["apk.pick"], "", "Android app (*.apk)")
        if path:
            self.set_apk(Path(path))

    def set_apk(self, path: Path) -> None:
        self.apk = path
        try:
            self.s.check_apk(path)
            self.apk_ok = True
            self.apk_status.setText(self.t["apk.ok"])
        except Exception as e:  # noqa: BLE001
            self.apk_ok = False
            self.apk_status.setText(f"{path.name}: {e}")
        self._update_nav()

    def refresh_unity(self) -> None:
        state = self.s.unity_status()
        if state == "missing" and not self.s.hub_installed():
            state = "nohub"
        self.unity_state = state
        self.unity_status_label.setText(self.t["unity." + state])
        self._update_nav()

    def _pick_out(self) -> None:
        path = QFileDialog.getExistingDirectory(self, self.t["build.out"])
        if path:
            self.out_edit.setText(path)

    # ------------------------------------------------------------ build
    def choices(self):
        from builder import steps
        out = Path(self.out_edit.text()).expanduser()
        return steps.Choices(base=self.game_paths["base"], update=self.game_paths["update"],
                             apk=None if (self.no_apk.isChecked() and not self.apk_ok) else self.apk,
                             host="account" if self.use_account.isChecked() else "tunnel",
                             out=out, work=out / ".build", untested_ok=self.try_untested.isChecked())

    def start_build(self) -> None:
        c = self.choices()
        self.start_button.setEnabled(False)
        self.open_game.setEnabled(False)
        self.build_status.setText("")

        def work():
            try:
                out = self.s.build(c, lambda f, m: self.bridge.progress.emit(f, m), self.bridge.log.emit)
                self.bridge.finished.emit(out, "")
            except Exception as e:  # noqa: BLE001
                self.bridge.finished.emit(None, str(e))

        threading.Thread(target=work, daemon=True).start()

    def _on_progress(self, frac: float, msg: str) -> None:
        self.progress.setValue(int(round(frac * 100)))
        self.build_status.setText(msg)
        self.details.appendPlainText(msg)

    def _on_log(self, msg: str) -> None:
        self.details.appendPlainText(msg)

    def _on_finished(self, out, error: str) -> None:
        self.start_button.setEnabled(True)
        if error:
            self.build_status.setText(self.t["build.failed"].format(error=error))
            self.details.appendPlainText(error)
            return
        self.output = Path(out) if out else None
        self.progress.setValue(100)
        self.build_status.setText(self.t["build.done"])
        self.open_game.setEnabled(True)


def icon_path() -> Path | None:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).parent))
    for cand in (base / "assets" / "icon.png", base / "builder" / "assets" / "icon.png"):
        if cand.is_file():
            return cand
    return None


def main() -> int:
    app = QApplication.instance() or QApplication(sys.argv)
    ip = icon_path()
    if ip:
        app.setWindowIcon(QIcon(str(ip)))
    lang = texts.pick_language(QLocale.system().name())
    holder = {}

    def show(lang: str, state: Window | None = None) -> None:
        w = Window(real_services(), lang)
        w.lang_box.currentIndexChanged.connect(lambda i: show("en" if i == 0 else "cs", w))
        if state is not None:
            w.move(state.pos())
            state.close()
        holder["w"] = w
        w.show()

    show(lang)
    return app.exec()
