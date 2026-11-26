import os
import tempfile
import unittest
from pathlib import Path

from pipeline import audio_install

PS4 = Path("Assets/StreamingAssets/Audio/GeneratedSoundBanks/PS4")


def write(p: Path, data: str = "x") -> Path:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(data)
    return p


class InstallTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.staged, self.project = r / "work/audio/PortAudio", r / "work/project"
        write(self.staged / "manifest.json", "{}")
        write(self.staged / "banks/Init.json", "{\"a\":1}")
        write(self.staged / "media/1.ogg", "OggS1")
        (self.project / "Assets").mkdir(parents=True)
        self.dst = self.project / "Assets/StreamingAssets/PortAudio"

    def tearDown(self):
        self.tmp.cleanup()

    def test_first_install_copies_everything(self):
        r = audio_install.install(self.staged, self.project)
        self.assertEqual(r["copied"], 3)
        self.assertEqual(r["unchanged"], 0)
        self.assertEqual((self.dst / "media/1.ogg").read_text(), "OggS1")
        self.assertEqual((self.dst / "banks/Init.json").read_text(), "{\"a\":1}")

    def test_second_install_is_a_noop(self):
        audio_install.install(self.staged, self.project)
        r = audio_install.install(self.staged, self.project)
        self.assertEqual((r["copied"], r["unchanged"], r["deleted"]), (0, 3, 0))

    def test_changed_size_or_mtime_is_recopied(self):
        audio_install.install(self.staged, self.project)
        write(self.staged / "media/1.ogg", "OggS-longer")
        st = (self.staged / "banks/Init.json").stat()
        os.utime(self.staged / "banks/Init.json", ns=(st.st_atime_ns, st.st_mtime_ns + 5_000_000_000))
        r = audio_install.install(self.staged, self.project)
        self.assertEqual(r["copied"], 2)
        self.assertEqual((self.dst / "media/1.ogg").read_text(), "OggS-longer")

    def test_stale_files_and_their_meta_are_deleted_but_live_meta_kept(self):
        write(self.staged / "lang/english_uk/2.ogg")
        audio_install.install(self.staged, self.project)
        write(self.dst / "media/1.ogg.meta", "guid")
        write(self.dst / "lang/english_uk/2.ogg.meta", "guid")
        write(self.dst / "lang/english_uk.meta", "guid")
        write(self.dst / "lang.meta", "guid")
        write(self.dst / "media.meta", "guid")
        (self.staged / "lang/english_uk/2.ogg").unlink()
        r = audio_install.install(self.staged, self.project)
        self.assertGreaterEqual(r["deleted"], 1)
        self.assertFalse((self.dst / "lang").exists())
        self.assertFalse((self.dst / "lang.meta").exists())
        self.assertTrue((self.dst / "media/1.ogg.meta").is_file())
        self.assertTrue((self.dst / "media.meta").is_file())

    def test_ignores_ds_store(self):
        write(self.staged / ".DS_Store")
        r = audio_install.install(self.staged, self.project)
        self.assertEqual(r["copied"], 3)
        self.assertFalse((self.dst / ".DS_Store").exists())

    def test_install_prunes_wwise_audio(self):
        write(self.project / PS4 / "LocaleIndependent/Init.bnk", "BKHD")
        write(self.project / PS4 / "LocaleIndependent/Frontend.bnk", "BKHD")
        r = audio_install.install(self.staged, self.project)
        self.assertEqual(r["pruned"], 1)
        self.assertTrue((self.project / PS4 / "LocaleIndependent/Init.bnk").is_file())

    def test_missing_staged_dir_fails(self):
        with self.assertRaises(FileNotFoundError):
            audio_install.install(self.staged.parent / "nope", self.project)


class PruneTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.project = self.root / "project"
        self.ps4 = self.project / PS4
        write(self.ps4 / "LocaleIndependent/Init.bnk", "BKHD")
        write(self.ps4 / "LocaleIndependent/Init.bnk.meta", "guid")
        write(self.ps4 / "LocaleIndependent/SoundEffect_SoundBank.bnk")
        write(self.ps4 / "LocaleIndependent/SoundEffect_SoundBank.bnk.meta")
        write(self.ps4 / "LocaleIndependent/123.wem")
        write(self.ps4 / "English(UK)/en-GB_TY1.wem")
        write(self.ps4 / "English(UK)/Commentary_Global_SoundBank.bnk")
        write(self.ps4 / "English(UK).meta")
        write(self.ps4 / "LocaleIndependent.meta")
        write(self.ps4 / "SoundbanksInfo.xml")
        write(self.ps4.parent / "PS4.meta")

    def tearDown(self):
        self.tmp.cleanup()

    def test_keeps_only_init_bank(self):
        n = audio_install.prune_wwise_audio(self.project)
        self.assertEqual(n, 7)
        left = sorted(p.relative_to(self.ps4).as_posix() for p in self.ps4.rglob("*"))
        self.assertEqual(left, ["LocaleIndependent", "LocaleIndependent.meta", "LocaleIndependent/Init.bnk",
                                "LocaleIndependent/Init.bnk.meta"])
        self.assertTrue((self.ps4.parent / "PS4.meta").is_file())
        self.assertEqual(audio_install.prune_wwise_audio(self.project), 0)

    def test_no_wwise_audio_is_fine(self):
        self.assertEqual(audio_install.prune_wwise_audio(self.root / "empty-project"), 0)

    def test_does_not_follow_symlinks_out_of_the_project(self):
        outside = self.root / "merged-audio"
        write(outside / "keep.wem", "precious")
        os.symlink(outside, self.ps4 / "Linked", target_is_directory=True)
        audio_install.prune_wwise_audio(self.project)
        self.assertEqual((outside / "keep.wem").read_text(), "precious")
        self.assertFalse((self.ps4 / "Linked").exists())

    def test_refuses_a_root_that_resolves_outside_the_project(self):
        other = self.root / "other"
        project = self.root / "p2"
        write(other / "Audio/GeneratedSoundBanks/PS4/x.wem", "precious")
        (project / "Assets").mkdir(parents=True)
        os.symlink(other, project / "Assets/StreamingAssets", target_is_directory=True)
        with self.assertRaises(ValueError):
            audio_install.prune_wwise_audio(project)
        self.assertEqual((other / "Audio/GeneratedSoundBanks/PS4/x.wem").read_text(), "precious")


if __name__ == "__main__":
    unittest.main()
