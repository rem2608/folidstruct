# FolidStruct for Windows

The Windows version of FolidStruct: the same templates and project folder
names as on Linux, with a **FolidStruct** submenu in the File Explorer
context menu, including the modern Windows 11 menu.

It is meant to be published in the **Microsoft Store**, which signs the
package. Builds from this repository are unsigned and can be installed for
testing only (see below).

> Status: tested on Windows 11 (x64); not yet published in the Store.

## How it is made

| Folder | What it is |
|---|---|
| `src/FolidStruct.Core` | Logic shared by everything: configuration, name format, counters, name rules, folder creation, translations. The C# counterpart of `folidstruct/core.py` and `i18n.py`, with the same `config.json` and `counters.json` format. No Windows dependency. |
| `src/FolidStruct.App` | The app (WinUI 3, .NET 10): template manager, preferences and the "New project" window. Packaged as MSIX (`Package.appxmanifest`). |
| `src/FolidStruct.ContextMenu` | The File Explorer menu: a C++ COM server implementing `IExplorerCommand`, registered in the package manifest for folders and folder backgrounds. It reads the templates from `config.json` each time the menu opens and starts the app with `--new --template-id ID --dir DIR`. |
| `tests/` | Tests of the core (xUnit) and of the menu's JSON reader and argument quoting (plain C++). Both also run on Linux. |
| `scripts/` | `make-assets.py` (icons from the SVG), `install-dev.ps1` (installs a test build). |

Where the menu appears: on folders and on the background of a folder, in
the Windows 11 menu. **Not on the desktop background**: Windows does not
offer packaged menu handlers there. To create a project on the desktop,
open *Desktop* in File Explorer and right-click its background.

Differences from Linux:

- The files live in the package's `LocalState` folder
  (`%LOCALAPPDATA%\Packages\<package family name>\LocalState`), where both
  the app and the menu find them.
- Names follow the Windows rules: no `\ / : * ? " < > |`, no reserved names
  (`CON`, `NUL`, `COM1`…), no trailing dot or space, case-insensitive
  duplicates.

## Building

Needs Windows 10/11 with Visual Studio 2022 or later ("Desktop development
with C++" and ".NET desktop development" workloads) and the .NET 10 SDK.

```powershell
cd windows
./build.ps1                  # x64 test package in windows/out
./build.ps1 -Platform ARM64
```

GitHub Actions (`.github/workflows/windows.yml`) builds both platforms on
every push that touches `windows/` and keeps the packages as artifacts
named `FolidStruct-windows-x64` and `FolidStruct-windows-ARM64`.

Tests:

```bash
cd windows
dotnet test --project tests/FolidStruct.Core.Tests
```

## Installing a test build

Builds from GitHub Actions are signed with a throwaway test certificate
(created for each build; its private key is discarded) whose subject matches
the package publisher. The artifact contains the package, the Windows App SDK
runtime, the certificate (`FolidStruct-test.cer`) and `install-dev.ps1`.

1. Download the `FolidStruct-windows-x64` artifact (or `-ARM64`) from the
   latest *Windows* run on GitHub Actions and unzip it.
2. In that folder, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File install-dev.ps1
   ```

   It asks for administrator rights to add the test certificate to the
   *Trusted People* of the PC (replacing the one of a previous test build),
   then installs the package like the Microsoft Store would.
3. Open FolidStruct from the Start menu, then right-click a folder in File
   Explorer.

Unsigned local builds (`build.ps1`) have no certificate: the script then
registers the package unpacked, which needs **Developer Mode** (Settings ›
System › For developers).

To remove FolidStruct and the test certificate: `install-dev.ps1 -Uninstall`
from an administrator terminal.

## Publishing in the Microsoft Store

The package identity in `src/FolidStruct.App/Package.appxmanifest` is the one
Partner Center assigned (`Rem2608.FolidStruct`).

1. Download the `FolidStruct-Store` artifact of the latest *Windows* run:
   it holds the unsigned x64 and ARM64 packages (the Store signs them).
2. In Partner Center, create a submission and upload both `.msix` files in
   *Packages*.
3. The `runFullTrust` capability needs a short justification: FolidStruct is
   a desktop app that creates folders where the user chooses and adds an
   entry to the File Explorer context menu.
4. Each new version needs a higher version number
   (`scripts/set-version.sh`).
