"""Minimal translation support.

Source strings are in English. To add a language, add a dictionary to
``TRANSLATIONS`` keyed by the two-letter language code, and its name to
``LANGUAGE_NAMES``.

The language is the one chosen in Preferences, or the system language
("auto"). Languages without a translation fall back to English.
"""

import json
import os

from .paths import config_path

AUTO = "auto"

# Shown in the language selector, each in its own language.
LANGUAGE_NAMES = {
    "it": "Italiano",
    "en": "English",
}

TRANSLATIONS = {
    "it": {
        # Defaults
        "Graphics": "Grafica",
        "Fonts": "Font",
        "Images": "Immagini",
        "Drafts": "Bozze",
        "Print": "Stampa",
        # Validation
        "The name cannot be empty": "Il nome non può essere vuoto",
        "The name cannot contain “/”": "Il nome non può contenere “/”",
        "“.” and “..” are not valid names": "“.” e “..” non sono nomi validi",
        "The name is too long": "Il nome è troppo lungo",
        "A template with this name already exists": "Esiste già un modello con questo nome",
        "This folder is already in the list": "Questa cartella è già nell'elenco",
        # Errors
        "A folder named “{name}” already exists here": "Qui esiste già una cartella chiamata “{name}”",
        "Permission denied": "Permesso negato",
        "The configuration file is damaged: {error}": "Il file di configurazione è danneggiato: {error}",
        "Open FolidStruct from the applications menu to create one.":
            "Apri FolidStruct dal menù delle applicazioni per crearne uno.",
        "Cannot create the project": "Impossibile creare il progetto",
        # New project window
        "New project": "Nuovo progetto",
        "Cancel": "Annulla",
        "Create": "Crea",
        "Project name": "Nome del progetto",
        "Options": "Opzioni",
        "Date prefix": "Data come prefisso",
        "Progressive number": "Numero progressivo",
        "Result": "Risultato",
        "Folder": "Cartella",
        "Subfolders": "Sottocartelle",
        "No subfolders": "Nessuna sottocartella",
        "Location": "Posizione",
        "Project": "Progetto",
        # Manager window
        "New template": "Nuovo modello",
        "New folder": "Nuova cartella",
        "No templates": "Nessun modello",
        "Create a template to define a folder structure": "Crea un modello per definire una struttura di cartelle",
        "Create template": "Crea modello",
        "Template": "Modello",
        "Name": "Nome",
        "Folders": "Cartelle",
        "Folder {n}": "Cartella {n}",
        "Add folder": "Aggiungi cartella",
        "Move up": "Sposta su",
        "Move down": "Sposta giù",
        "Remove": "Rimuovi",
        "Delete template": "Elimina modello",
        "Delete “{name}”?": "Eliminare “{name}”?",
        "The template will be removed. Folders already created are not affected.":
            "Il modello verrà rimosso. Le cartelle già create non vengono toccate.",
        "Delete": "Elimina",
        "Preferences": "Preferenze",
        "About FolidStruct": "Informazioni su FolidStruct",
        "Project folder name": "Nome della cartella del progetto",
        "Custom options": "Opzioni personalizzate",
        "Use these options instead of the global ones": "Usa queste opzioni invece di quelle globali",
        "Default options, can be changed each time a project is created":
            "Opzioni predefinite, modificabili ogni volta che si crea un progetto",
        "Next number": "Prossimo numero",
        "Language": "Lingua",
        "Automatic": "Automatica",
        "“Automatic” uses the system language": "“Automatica” usa la lingua del sistema",
        "Example": "Esempio",
        "Right-click a folder in Files and choose FolidStruct":
            "Fai clic destro su una cartella in File e scegli FolidStruct",
        "Create project folder structures from templates":
            "Crea strutture di cartelle per i progetti a partire da modelli",
    },
}


def system_language():
    for var in ("LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG"):
        value = os.environ.get(var)
        if value:
            code = value.split(":")[0].split(".")[0].split("_")[0]
            return code if code in LANGUAGE_NAMES else "en"
    return "en"


def resolve(choice):
    """Turn a saved choice ("auto", "it", "en"…) into a language code."""
    if choice in LANGUAGE_NAMES:
        return choice
    return system_language()


def _saved_choice():
    try:
        with open(config_path(), encoding="utf-8") as f:
            return json.load(f).get("language", AUTO)
    except (OSError, ValueError, AttributeError):
        return AUTO


_language = resolve(_saved_choice())


def set_language(choice):
    global _language
    _language = resolve(choice)


def current_language():
    return _language


def translate(text, language):
    return TRANSLATIONS.get(language, {}).get(text, text)


def _(text):
    return translate(text, _language)
