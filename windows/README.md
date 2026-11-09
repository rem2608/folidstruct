# FolidStruct for Windows

The Windows version of FolidStruct: the same templates and project folder
names as on Linux, with a **FolidStruct** submenu in the File Explorer
context menu, including the modern Windows 11 menu.

It is meant to be published in the **Microsoft Store**, which signs the
package. Builds from this repository are unsigned and can be installed for
testing only (see below).

> Status: in development, not yet tested on a real PC.

## How it is made

| Folder | What it is |
|---|---|
| `src/FolidStruct.Core` | Logic shared by everything: configuration, name format, counters, name rules, folder creation, translations. The C# counterpart of `folidstruct/core.py` and `i18n.py`, with the same `config.json` and `counters.json` format. No Windows dependency. |
| `src/FolidStruct.App` | The app (WinUI 3, .NET 10): template manager, preferences and the "New project" window. Packaged as MSIX (`Package.appxmanifest`). |
| `src/FolidStruct.ContextMenu` | The File Explorer menu: a C++ COM server implementing `IExplorerCommand`, registered in the package manifest for folders and folder backgrounds. It reads the templates from `config.json` each time the menu opens and starts the app with `--new --template-id ID --dir DIR`. |
| `tests/` | Tests of the core (xUnit) and of the menu's JSON reader and argument quoting (plain C++). Both also run on Linux. |
| `scripts/` | `make-assets.py` (icons from the SVG), `install-dev.ps1` (installs a test build). |

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

1. Turn on **Developer Mode**: Settings › System › For developers.
2. Download the `FolidStruct-windows-x64` artifact from the latest
   *Windows* run on GitHub Actions and unzip it.
3. In that folder, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File install-dev.ps1
   ```

4. Open FolidStruct from the Start menu, then right-click a folder in File
   Explorer. If the menu does not appear, restart File Explorer
   (`Stop-Process -Name explorer`).

To remove it: `install-dev.ps1 -Uninstall`.

## Publishing in the Microsoft Store

1. Create a free individual developer account in Partner Center and
   reserve the name **FolidStruct**.
2. Copy *Package/Identity/Name* and *Package/Identity/Publisher* from the
   product's *Product identity* page into the `Identity` element of
   `src/FolidStruct.App/Package.appxmanifest`.
3. Build the Store packages (`./build.ps1 -Store`) and upload them.
4. The `runFullTrust` capability needs a short justification in the
   submission: FolidStruct is a desktop app that creates folders where the
   user chooses and adds an entry to the File Explorer menu.
