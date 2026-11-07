import os
import unittest
from unittest import mock

from folidstruct import core, i18n


class LanguageTest(unittest.TestCase):
    def test_resolve(self):
        self.assertEqual(i18n.resolve("it"), "it")
        self.assertEqual(i18n.resolve("en"), "en")
        for system, expected in (("it_IT.UTF-8", "it"), ("en_US.UTF-8", "en"),
                                 ("fr_FR.UTF-8", "en"), ("de", "en")):
            with mock.patch.dict(os.environ, {"LANGUAGE": "", "LC_ALL": "",
                                              "LC_MESSAGES": "", "LANG": system}):
                self.assertEqual(i18n.resolve(i18n.AUTO), expected, system)

    def test_translate(self):
        self.assertEqual(i18n.translate("Print", "it"), "Stampa")
        self.assertEqual(i18n.translate("Print", "en"), "Print")
        self.assertEqual(i18n.translate("Print", "fr"), "Print")


class ExampleTemplateTest(unittest.TestCase):
    def test_example_in_each_language(self):
        self.assertEqual(core.example_template("it").name, "Grafica")
        self.assertEqual(core.example_template("en").folders,
                         ["Fonts", "Images", "Drafts", "Print"])

    def test_translates_untouched_example(self):
        config = core.Config(templates=[core.example_template("it")])
        self.assertTrue(core.translate_example(config, "en"))
        self.assertEqual(config.templates[0].name, "Graphics")
        self.assertEqual(config.templates[0].folders, ["Fonts", "Images", "Drafts", "Print"])
        self.assertTrue(core.translate_example(config, "it"))
        self.assertEqual(config.templates[0].name, "Grafica")

    def test_keeps_options_when_translating(self):
        example = core.example_template("it")
        example.custom_options = True
        example.use_counter = True
        config = core.Config(templates=[example])
        core.translate_example(config, "en")
        self.assertTrue(config.templates[0].custom_options)
        self.assertTrue(config.templates[0].use_counter)

    def test_keeps_edited_example(self):
        edited = core.example_template("it")
        edited.folders.append("Clienti")
        renamed = core.Template("Grafica 2", ["Font", "Immagini", "Bozze", "Stampa"])
        config = core.Config(templates=[edited, renamed])
        self.assertFalse(core.translate_example(config, "en"))
        self.assertEqual([t.name for t in config.templates], ["Grafica", "Grafica 2"])

    def test_no_duplicate_names(self):
        config = core.Config(templates=[core.example_template("it"),
                                        core.Template("Graphics", ["Other"])])
        self.assertFalse(core.translate_example(config, "en"))
        self.assertEqual(config.templates[0].name, "Grafica")


if __name__ == "__main__":
    unittest.main()
