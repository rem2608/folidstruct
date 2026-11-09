using System.Globalization;

namespace FolidStruct.Core;

/// <summary>
/// Minimal translation support, the same as folidstruct/i18n.py on Linux.
///
/// Source strings are in English. To add a language, add a dictionary to
/// <see cref="Translations"/> keyed by the two-letter language code, and its
/// name to <see cref="LanguageNames"/>. Languages without a translation fall
/// back to English.
/// </summary>
public static class I18n
{
    public const string Auto = "auto";

    /// <summary>Shown in the language selector, each in its own language.</summary>
    public static readonly IReadOnlyDictionary<string, string> LanguageNames =
        new Dictionary<string, string> { ["it"] = "Italiano", ["en"] = "English" };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["it"] = new Dictionary<string, string>
            {
                // Defaults
                ["Graphics"] = "Grafica",
                ["Fonts"] = "Font",
                ["Images"] = "Immagini",
                ["Drafts"] = "Bozze",
                ["Print"] = "Stampa",
                // Validation
                ["The name cannot be empty"] = "Il nome non può essere vuoto",
                ["The name cannot contain these characters: {chars}"] =
                    "Il nome non può contenere questi caratteri: {chars}",
                ["“.” and “..” are not valid names"] = "“.” e “..” non sono nomi validi",
                ["The name cannot end with a dot or a space"] =
                    "Il nome non può finire con un punto o uno spazio",
                ["“{name}” is reserved by Windows"] = "“{name}” è un nome riservato di Windows",
                ["The name is too long"] = "Il nome è troppo lungo",
                ["A template with this name already exists"] = "Esiste già un modello con questo nome",
                ["This folder is already in the list"] = "Questa cartella è già nell'elenco",
                // Errors
                ["A folder named “{name}” already exists here"] =
                    "Qui esiste già una cartella chiamata “{name}”",
                ["Permission denied"] = "Permesso negato",
                ["The configuration file is damaged: {error}"] =
                    "Il file di configurazione è danneggiato: {error}",
                ["Open FolidStruct from the Start menu to create one."] =
                    "Apri FolidStruct dal menù Start per crearne uno.",
                ["Cannot create the project"] = "Impossibile creare il progetto",
                ["The folder could not be found"] = "Impossibile trovare la cartella",
                // New project window
                ["New project"] = "Nuovo progetto",
                ["Cancel"] = "Annulla",
                ["Create"] = "Crea",
                ["Project name"] = "Nome del progetto",
                ["Options"] = "Opzioni",
                ["Progressive number"] = "Numero progressivo",
                ["Result"] = "Risultato",
                ["Folder"] = "Cartella",
                ["Subfolders"] = "Sottocartelle",
                ["No subfolders"] = "Nessuna sottocartella",
                ["Location"] = "Posizione",
                ["Project"] = "Progetto",
                // Manager window
                ["Templates"] = "Modelli",
                ["New template"] = "Nuovo modello",
                ["New folder"] = "Nuova cartella",
                ["No templates"] = "Nessun modello",
                ["Create a template to define a folder structure"] =
                    "Crea un modello per definire una struttura di cartelle",
                ["Create template"] = "Crea modello",
                ["Template"] = "Modello",
                ["Name"] = "Nome",
                ["Folders"] = "Cartelle",
                ["Folder {n}"] = "Cartella {n}",
                ["Add folder"] = "Aggiungi cartella",
                ["Move up"] = "Sposta su",
                ["Move down"] = "Sposta giù",
                ["Remove"] = "Rimuovi",
                ["Delete template"] = "Elimina modello",
                ["Delete “{name}”?"] = "Eliminare “{name}”?",
                ["The template will be removed. Folders already created are not affected."] =
                    "Il modello verrà rimosso. Le cartelle già create non vengono toccate.",
                ["Delete"] = "Elimina",
                ["Preferences"] = "Preferenze",
                ["About FolidStruct"] = "Informazioni su FolidStruct",
                ["Project folder name"] = "Nome della cartella del progetto",
                ["Custom options"] = "Opzioni personalizzate",
                ["Next number"] = "Prossimo numero",
                ["Close"] = "Chiudi",
                ["Version {version}"] = "Versione {version}",
                ["License: GNU GPL version 3 or later"] = "Licenza: GNU GPL versione 3 o successiva",
                ["Website"] = "Sito web",
                // Name format
                ["Use this name format instead of the global one"] =
                    "Usa questo formato del nome invece di quello globale",
                ["Used by every template without custom options. Date and number can also be " +
                 "switched on or off when creating a project."] =
                    "Usato da tutti i modelli senza opzioni personalizzate. Data e numero si " +
                    "possono anche attivare o disattivare quando si crea un progetto.",
                ["Each template counts on its own"] = "Ogni modello ha la sua numerazione",
                ["Separator"] = "Separatore",
                ["Space"] = "Spazio",
                ["Underscore (_)"] = "Trattino basso (_)",
                ["Hyphen (-)"] = "Trattino (-)",
                ["Spaced hyphen ( - )"] = "Trattino con spazi ( - )",
                ["Date"] = "Data",
                ["Fixed text"] = "Testo fisso",
                ["Field to fill in"] = "Campo da compilare",
                ["Choice from a list"] = "Scelta da elenco",
                ["Add block"] = "Aggiungi blocco",
                ["Fixed text, field to fill in or choice"] = "Testo fisso, campo da compilare o scelta",
                ["Included by default"] = "Incluso in modo predefinito",
                ["Typed when creating the project"] = "Scritto quando si crea il progetto",
                ["default: {value}"] = "predefinito: {value}",
                ["Edit"] = "Modifica",
                ["Save"] = "Salva",
                ["Text"] = "Testo",
                ["Label, e.g. Client"] = "Etichetta, es. Cliente",
                ["Values, separated by commas"] = "Valori, separati da virgole",
                ["Default value (optional)"] = "Valore predefinito (facoltativo)",
                ["Always added to the name, e.g. a studio or language code."] =
                    "Aggiunto sempre al nome, ad esempio la sigla dello studio o della lingua.",
                ["Asked when creating the project; left empty, it is skipped."] =
                    "Chiesto quando si crea il progetto; se resta vuoto viene saltato.",
                ["Picked from a menu when creating the project, so names stay consistent; " +
                 "it can also be left empty."] =
                    "Scelto da un menù quando si crea il progetto, così i nomi restano uniformi; " +
                    "si può anche lasciare vuoto.",
                ["Details"] = "Dettagli",
                ["Language"] = "Lingua",
                ["Automatic"] = "Automatica",
                ["“Automatic” uses the system language"] = "“Automatica” usa la lingua del sistema",
                ["Example"] = "Esempio",
                ["Right-click a folder in File Explorer and choose FolidStruct"] =
                    "Fai clic destro su una cartella in Esplora file e scegli FolidStruct",
                ["Create project folder structures from templates"] =
                    "Crea strutture di cartelle per i progetti a partire da modelli",
            },
        };

    private static string _language = SystemLanguage();

    /// <summary>The language of the user interface, or English if not translated.</summary>
    public static string SystemLanguage()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return LanguageNames.ContainsKey(code) ? code : "en";
    }

    /// <summary>Turn a saved choice ("auto", "it", "en"…) into a language code.</summary>
    public static string Resolve(string? choice) =>
        choice is not null && LanguageNames.ContainsKey(choice) ? choice : SystemLanguage();

    public static void SetLanguage(string? choice) => _language = Resolve(choice);

    public static string CurrentLanguage => _language;

    public static string Translate(string text, string language) =>
        Translations.TryGetValue(language, out var table) && table.TryGetValue(text, out var t)
            ? t : text;

    /// <summary>Translate to the current language.</summary>
    public static string T(string text) => Translate(text, _language);

    /// <summary>Translate and fill in "{key}" placeholders.</summary>
    public static string T(string text, params (string Key, object Value)[] args)
    {
        var result = T(text);
        foreach (var (key, value) in args)
            result = result.Replace("{" + key + "}", value.ToString());
        return result;
    }
}
