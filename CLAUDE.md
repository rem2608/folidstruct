# FolidStruct – contesto e storico del progetto

Questo file riassume il progetto per chi ci lavora (persone o Claude Code):
cosa fa, come è fatto, perché è fatto così e cosa è già stato deciso.
Il README, in inglese, resta la documentazione per gli utenti.

## In breve

FolidStruct crea la struttura di cartelle di un nuovo progetto con un clic
dal menù contestuale del file manager. L'utente definisce dei **modelli**
(es. "Grafica" → Font, Immagini, Bozze, Stampa); con il tasto destro su una
cartella sceglie **FolidStruct ▸ Grafica**, scrive il nome del progetto e
ottiene la cartella con tutte le sottocartelle.

**Origine:** l'autore (rem2608, grafico) creava a mano le cartelle di ogni
progetto e i nomi non erano uniformi ("stampa", "per la stampa"…).
L'obiettivo è **velocità e coerenza dei nomi** in tutto l'archivio.

- Repository: https://github.com/rem2608/folidstruct (pubblico, GPL-3.0-or-later)
- Autore: rem2608, commit firmati `Marco <m_ricchi@ikmail.com>` (impostato
  solo per questo repository; l'email globale di git sul PC è un'altra)
- Sviluppato con l'aiuto di Claude (Anthropic): commit con `Co-Authored-By`
  e sezione *Credits* nel README, per scelta esplicita dell'autore
- Versione attuale: **0.2.0** (2026-11-08)

## Identità e nomi

| Cosa | Valore |
|---|---|
| Nome visibile | FolidStruct |
| Comando, pacchetti, cartella di configurazione | `folidstruct` |
| Identificativo app | `io.github.rem2608.FolidStruct` |
| Pacchetto Python interno | `folidstruct` |
| Manutentore nei pacchetti | `rem2608 <m_ricchi@ikmail.com>` |

**Storia del nome** (non ripercorrere le stesse strade):
- *Proj*: scartato perché PROJ è una libreria geospaziale molto diffusa con
  comando `proj` e pacchetto `proj` (conflitto su Debian/Arch, ricerche
  sepolte). Per un periodo il comando è stato `proj-folders`.
- Valutati e scartati: *FolderCraft* (app Mac esistente, e somiglia alla
  famiglia LightCraft/PhotoCraft/VectorCraft), *FolderSnap* (confusione con
  Snap di Ubuntu), *FolderForge* (progetto GitHub simile), *Structo* (app
  commerciale per cantieri), *FoldStruct* (azienda tedesca di software per
  l'edilizia, Paderborn), *OneClickFolders*, *Structly*, *Folid*, *Stampo*.
- Scelto **FolidStruct**: nessun uso trovato in rete.

## Funzionalità

- Modelli con nome e lista di sottocartelle (un solo livello, per scelta).
- **Nome della cartella del progetto** composto da blocchi riordinabili:
  - integrati (sempre presenti, spostabili, non eliminabili): **data** ISO
    (`2026-11-08`), **numero progressivo** con `#` (`22#`), **nome del progetto**;
  - personalizzati: **testo fisso**, **campo da compilare** (etichetta +
    valore predefinito facoltativo), **scelta da elenco** (etichetta, valori,
    predefinito facoltativo, più la voce "—" per saltarla);
  - i blocchi vuoti vengono saltati;
  - separatore: spazio (predefinito), `_`, `-`, ` - `.
- Impostazioni su tre livelli: **globali** (Preferenze), **per modello**
  (interruttore "Opzioni personalizzate") e **per progetto** (nella
  finestrella: interruttori data/numero, campi e tendine).
- Default richiesti dall'autore: data attiva, numero disattivo.
- **Contatore per modello** (dalla 0.2.0): ogni modello ha la sua
  numerazione; "Prossimo numero" sta nella scheda del modello, non nelle
  Preferenze. Il numero si consuma solo se la creazione riesce.
- Sicurezza: non sovrascrive né unisce mai cartelle esistenti; i nomi sono
  validati (niente `/`, `.`, `..`, oltre 255 byte); in caso di errore a metà
  rimuove solo le cartelle vuote appena create.
- Lingue: inglese (testi sorgente) e italiano. Scelta nelle Preferenze:
  Automatica / Italiano / English. Il modello di esempio viene tradotto solo
  se non è stato modificato; con lingue diverse dall'italiano resta in inglese.
- Comando da terminale: `folidstruct`, `folidstruct --new [MODELLO] --dir DIR`,
  `--name NOME` (crea senza finestra), `--init`.

## Struttura del codice

```
folidstruct/            pacchetto Python (installato in /usr/share/folidstruct)
  core.py               logica senza GTK: configurazione, formato del nome,
                        contatori, validazione, creazione, migrazione
  paths.py              percorsi di configurazione (condivisi con i18n)
  i18n.py               traduzioni (dizionario TRANSLATIONS, scelta lingua)
  manager.py            finestra principale (modelli) e Preferenze
  formateditor.py       editor del formato del nome, usato sia nelle
                        Preferenze sia nelle opzioni del modello
  newproject.py         finestrella "Nuovo progetto"
  ui.py                 finestra d'errore senza finestra madre
  __main__.py           riga di comando
extensions/folidstruct_menu.py
                        menù contestuale per Nautilus, Nemo e Caja
                        (stessa API Python; FILE_MANAGER e COMMAND_PATH
                        vengono sostituiti all'installazione)
data/                   .desktop, icone, metainfo AppStream, service menu
                        Dolphin, screenshot (in inglese, tema chiaro)
bin/folidstruct.in      lanciatore
meson.build, meson_options.txt
                        installazione standard (opzioni -Dnautilus/-Dnemo/
                        -Dcaja/-Ddolphin)
install.sh, uninstall.sh
                        installazione manuale in ~/.local senza root
debian/                 pacchetto .deb (source format 3.0 native)
packaging/aur/PKGBUILD  base NON pubblicata per Arch (l'autore non vuole
                        pubblicare su AUR)
scripts/                set-version.sh, check-version.sh
.github/workflows/      ci.yml (test a ogni push), release.yml (tag v* → .deb)
tests/                  unittest (31 test)
windows/                versione Windows (vedi sotto e windows/README.md)
```

### Configurazione (`~/.config/folidstruct/`)

- `config.json` – **versione 2**: `language`, `format` globale
  (`separator`, `blocks`), `templates` (ognuno con `id` stabile, `name`,
  `folders`, `custom_format`, `format`).
- `counters.json` – `{id_modello: prossimo_numero}`. Separato dalla
  configurazione così una finestra rimasta aperta con dati vecchi non può
  far tornare indietro la numerazione.
- `.lock` – lock con `fcntl` per gli accessi contemporanei (non rientrante).
- **Migrazione v1 → v2** in `load_config()`: converte `use_date`/`use_counter`
  e `custom_options` nel formato a blocchi, assegna gli `id`, e il vecchio
  contatore unico (`counter`) diventa il punto di partenza di ogni modello;
  salva una volta sola, sotto lock.

## Integrazioni con i file manager

- **Nautilus 43+**: estensione nautilus-python (`python3-nautilus`). Non
  bloccare la versione con `gi.require_version("Nautilus", …)`: Ubuntu
  26.04 usa l'API 4.1 e un blocco su 4.0 faceva scartare l'estensione.
- **Nemo e Caja**: stesso file (le funzioni ricevono anche la finestra come
  primo argomento, per questo si usa `*args`). Non provati su sistemi reali.
- **Dolphin**: service menu **fisso** "FolidStruct ▸ New project…" che apre
  la finestrella con la tendina dei modelli. Inizialmente era rigenerato a
  ogni salvataggio, ma con i pacchetti di sistema non funzionava (menù
  assente fino al primo avvio, orfano dopo la disinstallazione). Le sue
  voci seguono la lingua di sistema, non quella scelta nell'app.
- **Scrivania di Ubuntu**: gestita dall'estensione GNOME Shell *Desktop
  Icons NG* (ding), che non carica le estensioni di Nautilus. Offre solo un
  sottomenù "Script" sulle icone, non sullo sfondo. **Decisione
  dell'autore: nessuna soluzione alternativa**, per non avere due
  comportamenti diversi nel sistema; è documentata come eccezione nel
  README ("Show Desktop in Files").
- Thunar: non supportato.

## Versione Windows (in sviluppo, cartella `windows/`)

Decisioni dell'autore (2026-11-09): **menù moderno di Windows 11** (non
solo "Mostra altre opzioni") e distribuzione tramite **Microsoft Store**,
che è anche una vetrina. L'autore ha un PC con Windows 11 per le prove.

- **Perché lo Store**: il menù moderno richiede un pacchetto MSIX firmato.
  Lo Store firma gratis (account sviluppatore gratuito per i privati dal
  2025). Azure Artifact Signing non è disponibile per i privati in Italia
  (solo USA/Canada); un certificato OV è a pagamento. Su GitHub restano
  codice e link allo Store; si potrà aggiungere un MSIX firmato in futuro.
- **Struttura**: `FolidStruct.Core` (C#, .NET 10, porting di core.py e
  i18n.py, stesso formato di config.json/counters.json, gira anche su
  Linux), `FolidStruct.App` (WinUI 3, Windows App SDK 1.8, MSIX con .NET
  incluso), `FolidStruct.ContextMenu` (C++ con WRL, `IExplorerCommand`
  registrato nel manifest per `Directory` e `Directory\Background`,
  CLSID `8D25BF02-46D9-4D8F-987D-2E5F04A68DA2`, CRT statico).
- **Configurazione**: nella cartella `LocalState` del pacchetto
  (`%LOCALAPPDATA%\Packages\<famiglia>\LocalState`). Il menù la trova
  dal nome della famiglia del pacchetto e apre l'app con
  `IApplicationActivationManager` (`--new --template-id ID --dir DIR`),
  così l'app ha l'identità del pacchetto. Il menù legge config.json con un
  piccolo lettore JSON proprio (nessuna dipendenza).
- **Menù moderno, lezioni imparate** (provato sul PC dell'autore il
  2026-11-10): sullo sfondo di una cartella e sul desktop il menù di
  Windows 11 chiama `GetState`/`Invoke` **senza elementi**; se in quel caso
  la voce si nasconde, compare solo in "Mostra altre opzioni". La cartella
  si ricava dalla finestra in primo piano (Esplora file, scheda attiva
  inclusa, tramite ShellWindows usato con `IDispatch` perché `exdisp.h`
  va in conflitto con le altre intestazioni; sul desktop `Progman`/`WorkerW`
  → cartella Desktop), come fa Windows Terminal. Clic destro *sopra* una
  cartella: per ora solo in "Mostra altre opzioni"; all'autore non
  interessa. Le prove si fanno con pacchetti firmati con un certificato di
  test usa e getta (generato da GitHub Actions, `install-dev.ps1` lo rende
  attendibile), non con la registrazione da sviluppatore.
- **Nomi**: regole di Windows (`\ / : * ? " < > |`, CON/NUL/COM1…, niente
  punto o spazio finale, doppioni senza distinguere maiuscole).
- **Compilazione**: qui (Linux) si compilano e provano solo Core e le parti
  portabili del menù; app e DLL si compilano con GitHub Actions
  (`.github/workflows/windows.yml`, x64 e ARM64), che controlla anche il
  contenuto del pacchetto e salva gli artifact. I pacchetti non firmati si
  provano con `windows/scripts/install-dev.ps1` (Modalità sviluppatore).
- **Identità del pacchetto** (`Package.appxmanifest`): `Name` e
  `Publisher` sono provvisori; vanno sostituiti con quelli di Partner
  Center dopo aver riservato il nome "FolidStruct".
- `scripts/set-version.sh` e `check-version.sh` aggiornano/controllano
  anche `windows/src/FolidStruct.Core/Config.cs` (AppInfo.Version) e il
  manifest MSIX (`X.Y.Z.0`).
- Icone MSIX e .ico generate dallo SVG con `windows/scripts/make-assets.py`.

## Icona

Concetto dell'autore: una cartella su una scheda a circuito (piste con
nodi), con le viti di uno "stampo" industriale. Versione scelta: "F",
piatta, colori della palette GNOME (piastra `#3584e4`, piste `#1a5fb4`,
cartella bianca con linguetta `#99c1f1`). La forma della cartella è
ridisegnata da zero: l'idea iniziale partiva da un'icona Icons8, la cui
licenza non ne permette l'uso come icona di un'app. Esiste anche l'icona
simbolica a 16 px (cartella con tre piste, una terminante in un nodo).

## Pacchetti e rilasci

- Rilascio: `scripts/set-version.sh X.Y.Z`, scrivere le note in
  `debian/changelog` e nel metainfo, commit, `git tag vX.Y.Z`,
  `git push && git push --tags`. GitHub Actions costruisce il .deb, lo
  installa per prova e pubblica la release.
- Lezioni imparate sulla release (entrambe risolte):
  - `gnome.post_install()` di Meson richiedeva `gtk-update-icon-cache` già
    in configurazione: ora i passaggi sono attivi solo se i programmi esistono.
  - Sul runner GitHub manca `build-essential` (dpkg-dev è preinstallato,
    quindi i suoi pacchetti consigliati non arrivano): `dpkg-buildpackage`
    usciva con codice 3. Ora viene installato esplicitamente.
  - Per riprodurre il runner: `docker run ubuntu:24.04` con
    `apt-get install --no-install-recommends`.
- I log delle Actions richiedono autenticazione; lo stato si legge dalla
  API pubblica (`/repos/rem2608/folidstruct/actions/runs`).
- Il push lo fa l'autore (token GitHub classico con permessi `repo` e
  `workflow`).
- Chi passa da `install.sh` al pacchetto deve prima lanciare
  `./uninstall.sh` (menù doppio); `install.sh` si rifiuta se trova
  `/usr/bin/folidstruct`. `uninstall.sh` ricostruisce la cache delle icone
  utente (una cache vecchia nascondeva l'icona del pacchetto).

### Storico versioni

- **0.1.0** (2026-11-07): modelli, data e numero (contatore unico), tre
  livelli di opzioni, scelta lingua, Nautilus/Nemo/Caja/Dolphin, .deb.
- **0.2.0** (2026-11-08): formato del nome a blocchi (testo fisso, campi,
  scelte), blocchi riordinabili, separatore, contatore per modello,
  migrazione automatica della configurazione.

## Sviluppo

```bash
python3 -m folidstruct          # avvia dai sorgenti
python3 -m unittest -v          # test (anche con LANG=C.UTF-8 per l'inglese)
meson setup build && meson test -C build
```

- Ambiente dell'autore: Ubuntu 26.04, GNOME 50, GTK 4.22, libadwaita 1.9,
  Python 3.14. Requisiti minimi dichiarati: Python 3.10, libadwaita 1.5
  (Ubuntu 24.04, Debian 13, Fedora 40, Mint 22).
- Screenshot e prove grafiche senza interagire: si crea la finestra con
  `XDG_CONFIG_HOME` temporanea e si salva un PNG con
  `Gtk.WidgetPaintable` + `GskRenderer.render_texture`. Per gli screenshot
  pubblici: `LANGUAGE=en` e `Adw.ColorScheme.FORCE_LIGHT`.
- Nuovi testi dell'interfaccia: scriverli in inglese dentro `_()` e
  aggiungere la traduzione in `i18n.py`.
- Stile: righe entro ~100 caratteri, commenti brevi in inglese.

## Idee e decisioni per il futuro

- **Mac**: l'autore ha un account sviluppatore Apple e sta per comprare un
  MacBook (consigliato Air 15" M5 24 GB/1 TB, oppure Pro 14" M5 se fa
  spesso lavori lunghi e pesanti). Piano: app nativa in Swift con
  estensione **Finder Sync** (sottomenù dinamico; l'estensione è isolata,
  quindi le cartelle le crea l'app principale), distribuita come `.dmg`
  firmato e notarizzato; codice in una cartella `macos/` con lo stesso
  formato dei modelli. Va sviluppata su un Mac con Xcode.
- **Windows**: in sviluppo, vedi la sezione "Versione Windows".
- Possibili migliorie: ripristino della configurazione se danneggiata,
  supporto a Thunar.
- Già valutato: l'idea è diffusa (es. PowerToys New+ di Microsoft,
  DirTemplateExtension, FolderCrafter, 47 Folders); FolidStruct non ne
  copia codice, nome, icone o testi.
