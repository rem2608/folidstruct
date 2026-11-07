import datetime
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from folidstruct import core

TODAY = datetime.date(2026, 11, 7)


class CoreTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        patcher = mock.patch.dict(os.environ, {"XDG_CONFIG_HOME": str(self.root / "config")})
        patcher.start()
        self.addCleanup(patcher.stop)
        self.addCleanup(self.tmp.cleanup)
        self.work = self.root / "work"
        self.work.mkdir()
        self.template = core.Template("Grafica", ["Font", "Immagini", "Bozze", "Stampa"])

    def test_build_name(self):
        self.assertEqual(core.build_project_name("Logo", TODAY, None), "2026-11-07 Logo")
        self.assertEqual(core.build_project_name("Logo", TODAY, 22), "2026-11-07 22# Logo")
        self.assertEqual(core.build_project_name("Logo", None, 22), "22# Logo")
        self.assertEqual(core.build_project_name("  Logo ", None, None), "Logo")

    def test_validate(self):
        self.assertIsNone(core.validate_name("Logo Rossi"))
        for bad in ("", "  ", "a/b", ".", "..", "x" * 256):
            self.assertIsNotNone(core.validate_name(bad), bad)

    def test_create_with_date(self):
        path = core.create_project(self.work, self.template, "Logo", True, False, today=TODAY)
        self.assertEqual(path.name, "2026-11-07 Logo")
        self.assertEqual(sorted(p.name for p in path.iterdir()),
                         ["Bozze", "Font", "Immagini", "Stampa"])
        self.assertEqual(core.read_counter(), 1)  # not consumed

    def test_counter_increments(self):
        core.write_counter(22)
        a = core.create_project(self.work, self.template, "A", True, True, today=TODAY)
        b = core.create_project(self.work, self.template, "B", False, True, today=TODAY)
        self.assertEqual(a.name, "2026-11-07 22# A")
        self.assertEqual(b.name, "23# B")
        self.assertEqual(core.read_counter(), 24)

    def test_existing_folder_is_not_touched_and_counter_kept(self):
        (self.work / "Logo").mkdir()
        (self.work / "Logo" / "file.txt").write_text("x")
        core.write_counter(5)
        with self.assertRaises(core.AppError):
            core.create_project(self.work, self.template, "Logo", False, False)
        self.assertEqual(os.listdir(self.work / "Logo"), ["file.txt"])
        self.assertEqual(core.read_counter(), 5)

    def test_duplicate_and_invalid_folders_are_skipped(self):
        template = core.Template("T", ["Fonts", "Fonts ", "", "a/b", "..", "Print"])
        path = core.create_project(self.work, template, "Logo", False, False)
        self.assertEqual(sorted(p.name for p in path.iterdir()), ["Fonts", "Print"])

    def test_unwritable_parent_gives_a_message(self):
        self.work.chmod(0o500)
        self.addCleanup(self.work.chmod, 0o700)
        if os.access(self.work, os.W_OK):
            self.skipTest("running as root")
        with self.assertRaises(core.AppError):
            core.create_project(self.work, self.template, "Logo", False, True)
        self.assertEqual(core.read_counter(), 1)

    def test_unwritable_config_gives_a_message(self):
        core.config_dir().parent.mkdir(parents=True)
        core.config_dir().parent.chmod(0o500)
        self.addCleanup(core.config_dir().parent.chmod, 0o700)
        if os.access(core.config_dir().parent, os.W_OK):
            self.skipTest("running as root")
        with self.assertRaises(core.AppError):
            core.create_project(self.work, self.template, "Logo", False, True)
        with self.assertRaises(core.AppError):
            core.write_counter(5)

    def test_options_for(self):
        config = core.Config(use_date=True, use_counter=False, templates=[self.template])
        self.assertEqual(config.options_for(self.template), (True, False))
        self.template.custom_options = True
        self.template.use_date = False
        self.template.use_counter = True
        self.assertEqual(config.options_for(self.template), (False, True))

    def test_config_roundtrip(self):
        self.assertEqual(core.load_config().templates[0].folders.__len__(), 4)  # defaults
        self.template.custom_options = True
        config = core.Config(use_date=False, use_counter=True, language="en",
                             templates=[self.template])
        core.save_config(config)
        self.assertEqual(core.load_config(), config)

    def test_damaged_config(self):
        core.config_path().parent.mkdir(parents=True)
        core.config_path().write_text("{not json")
        with self.assertRaises(core.AppError):
            core.load_config()


if __name__ == "__main__":
    unittest.main()
