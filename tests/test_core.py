import datetime
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from folidstruct import core
from folidstruct.core import CHOICE, COUNTER, DATE, FIELD, NAME, TEXT, Block, NameFormat

TODAY = datetime.date(2026, 11, 7)


class CoreTestCase(unittest.TestCase):
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
        self.fmt = NameFormat()

    def create(self, name, use_date=False, use_counter=False, fmt=None, values=None,
               template=None):
        return core.create_project(self.work, template or self.template, name, fmt or self.fmt,
                                   use_date, use_counter, values=values, today=TODAY)


class NamingTest(CoreTestCase):
    def test_default_format(self):
        self.assertEqual(core.build_project_name(self.fmt, "Logo", TODAY), "2026-11-07 Logo")
        self.assertEqual(core.build_project_name(self.fmt, "Logo", TODAY, 22),
                         "2026-11-07 22# Logo")
        self.assertEqual(core.build_project_name(self.fmt, "Logo", None, 22), "22# Logo")
        self.assertEqual(core.build_project_name(self.fmt, "  Logo ", None, None), "Logo")

    def test_custom_blocks_order_and_separator(self):
        fmt = NameFormat("_", [
            Block(FIELD, label="Cliente"), Block(DATE), Block(NAME),
            Block(TEXT, value="ITA"), Block(CHOICE, label="Tipo", options=["Logo", "Web"]),
            Block(COUNTER),
        ])
        name = core.build_project_name(fmt, "Nuovo marchio", TODAY, 5,
                                       values={0: "Rossi", 4: "Web"})
        self.assertEqual(name, "Rossi_2026-11-07_Nuovo marchio_ITA_Web_5#")

    def test_empty_blocks_are_skipped(self):
        fmt = NameFormat(" - ", [Block(DATE), Block(FIELD, label="Cliente"), Block(NAME),
                                 Block(CHOICE, label="Tipo", options=["A"])])
        self.assertEqual(core.build_project_name(fmt, "Logo", None, None,
                                                 values={1: "  ", 3: ""}), "Logo")

    def test_defaults_used_when_no_value_given(self):
        fmt = NameFormat(" ", [Block(FIELD, label="Cliente", value="Rossi"), Block(NAME)])
        self.assertEqual(core.build_project_name(fmt, "Logo"), "Rossi Logo")
        self.assertEqual(core.build_project_name(fmt, "Logo", values={0: ""}), "Logo")

    def test_whitespace_is_collapsed(self):
        fmt = NameFormat(" ", [Block(FIELD, label="C"), Block(NAME)])
        self.assertEqual(core.build_project_name(fmt, "Logo", values={0: "Ros\nsi  srl"}),
                         "Ros si srl Logo")

    def test_example_name(self):
        fmt = NameFormat(" ", [Block(DATE, enabled=False), Block(COUNTER, enabled=True),
                               Block(FIELD, label="Cliente"), Block(NAME)])
        self.assertEqual(core.example_name(fmt, counter=3), "3# ‹Cliente› " + core._("Project"))

    def test_validate(self):
        self.assertIsNone(core.validate_name("Logo Rossi"))
        for bad in ("", "  ", "a/b", ".", "..", "x" * 256):
            self.assertIsNotNone(core.validate_name(bad), bad)


class FormatTest(CoreTestCase):
    def test_roundtrip(self):
        fmt = NameFormat("-", [Block(NAME), Block(CHOICE, label="T", value="b", options=["a", "b"]),
                               Block(COUNTER, enabled=True), Block(DATE, enabled=False)])
        self.assertEqual(NameFormat.from_dict(fmt.to_dict()), fmt)

    def test_repairs_damaged_format(self):
        fmt = NameFormat.from_dict({"separator": "?", "blocks": [
            {"kind": "name"}, {"kind": "name"}, {"kind": "bogus"}, "x",
            {"kind": "text", "value": "A"}]})
        self.assertEqual(fmt.separator, " ")
        self.assertEqual([b.kind for b in fmt.blocks], [DATE, COUNTER, NAME, TEXT])
        self.assertFalse(fmt.block(COUNTER).enabled)


class CreationTest(CoreTestCase):
    def test_create_with_date(self):
        path = self.create("Logo", use_date=True)
        self.assertEqual(path.name, "2026-11-07 Logo")
        self.assertEqual(sorted(p.name for p in path.iterdir()),
                         ["Bozze", "Font", "Immagini", "Stampa"])
        self.assertEqual(core.read_counter(self.template), 1)  # not consumed

    def test_create_with_custom_blocks(self):
        fmt = NameFormat("_", [Block(DATE), Block(FIELD, label="Cliente"), Block(NAME)])
        path = self.create("Logo", use_date=True, fmt=fmt, values={1: "Rossi"})
        self.assertEqual(path.name, "2026-11-07_Rossi_Logo")

    def test_value_with_slash_is_rejected(self):
        fmt = NameFormat(" ", [Block(FIELD, label="Cliente"), Block(NAME)])
        with self.assertRaises(core.AppError):
            self.create("Logo", fmt=fmt, values={0: "a/b"})
        self.assertEqual(list(self.work.iterdir()), [])

    def test_counters_are_per_template(self):
        other = core.Template("Video", [])
        core.write_counter(self.template, 22)
        a = self.create("A", use_counter=True)
        b = self.create("B", use_counter=True)
        c = self.create("C", use_counter=True, template=other)
        self.assertEqual([a.name, b.name, c.name], ["22# A", "23# B", "1# C"])
        self.assertEqual(core.read_counter(self.template), 24)
        self.assertEqual(core.read_counter(other), 2)

    def test_forget_counter(self):
        core.write_counter(self.template, 9)
        core.forget_counter(self.template)
        self.assertEqual(core.read_counter(self.template), 1)

    def test_existing_folder_is_not_touched_and_counter_kept(self):
        (self.work / "Logo").mkdir()
        (self.work / "Logo" / "file.txt").write_text("x")
        core.write_counter(self.template, 5)
        with self.assertRaises(core.AppError):
            self.create("Logo")
        self.assertEqual(os.listdir(self.work / "Logo"), ["file.txt"])
        self.assertEqual(core.read_counter(self.template), 5)

    def test_duplicate_and_invalid_folders_are_skipped(self):
        template = core.Template("T", ["Fonts", "Fonts ", "", "a/b", "..", "Print"])
        path = self.create("Logo", template=template)
        self.assertEqual(sorted(p.name for p in path.iterdir()), ["Fonts", "Print"])

    def test_unwritable_parent_gives_a_message(self):
        self.work.chmod(0o500)
        self.addCleanup(self.work.chmod, 0o700)
        if os.access(self.work, os.W_OK):
            self.skipTest("running as root")
        with self.assertRaises(core.AppError):
            self.create("Logo", use_counter=True)
        self.assertEqual(core.read_counter(self.template), 1)

    def test_unwritable_config_gives_a_message(self):
        core.config_dir().parent.mkdir(parents=True)
        core.config_dir().parent.chmod(0o500)
        self.addCleanup(core.config_dir().parent.chmod, 0o700)
        if os.access(core.config_dir().parent, os.W_OK):
            self.skipTest("running as root")
        with self.assertRaises(core.AppError):
            self.create("Logo", use_counter=True)
        with self.assertRaises(core.AppError):
            core.write_counter(self.template, 5)


class ConfigTest(CoreTestCase):
    def test_default_config(self):
        config = core.load_config()
        self.assertEqual(len(config.templates[0].folders), 4)
        self.assertEqual([b.kind for b in config.format.blocks], [DATE, COUNTER, NAME])

    def test_roundtrip(self):
        self.template.custom_format = True
        self.template.format = NameFormat("_", [Block(NAME), Block(TEXT, value="X"),
                                                Block(DATE), Block(COUNTER)])
        config = core.Config(language="en", templates=[self.template])
        core.save_config(config)
        self.assertEqual(core.load_config(), config)

    def test_format_for(self):
        config = core.Config(templates=[self.template])
        self.assertIs(config.format_for(self.template), config.format)
        self.template.custom_format = True
        self.assertIs(config.format_for(self.template), self.template.format)

    def test_new_template_copies_global_format(self):
        config = core.Config(format=NameFormat("_"))
        template = core.new_template(config, "New")
        self.assertEqual(template.format, config.format)
        self.assertIsNot(template.format, config.format)

    def test_upgrade_from_version_1(self):
        core.config_dir().mkdir(parents=True)
        core.config_path().write_text(json.dumps({
            "version": 1, "use_date": True, "use_counter": False, "language": "it",
            "templates": [
                {"name": "Grafica", "folders": ["Font"], "custom_options": False,
                 "use_date": True, "use_counter": False},
                {"name": "Video", "folders": [], "custom_options": True,
                 "use_date": False, "use_counter": True},
            ]}))
        (core.config_dir() / "counter").write_text("23\n")

        config = core.load_config()
        graphics, video = config.templates
        self.assertEqual(config.language, "it")
        self.assertFalse(graphics.custom_format)
        self.assertTrue(config.format.block(DATE).enabled)
        self.assertFalse(config.format.block(COUNTER).enabled)
        self.assertTrue(video.custom_format)
        self.assertFalse(video.format.block(DATE).enabled)
        self.assertTrue(video.format.block(COUNTER).enabled)
        # Each template continues from the old shared counter.
        self.assertEqual(core.read_counter(graphics), 23)
        self.assertEqual(core.read_counter(video), 23)
        self.assertFalse((core.config_dir() / "counter").exists())
        # Saved once: ids are stable from now on.
        saved = json.loads(core.config_path().read_text())
        self.assertEqual(saved["version"], core.CONFIG_VERSION)
        self.assertEqual([t.id for t in core.load_config().templates], [graphics.id, video.id])

    def test_damaged_config(self):
        core.config_path().parent.mkdir(parents=True)
        core.config_path().write_text("{not json")
        with self.assertRaises(core.AppError):
            core.load_config()


if __name__ == "__main__":
    unittest.main()
