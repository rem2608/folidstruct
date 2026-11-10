// FolidStruct – File Explorer context menu.
//
// Adds a "FolidStruct" submenu to folders and folder backgrounds, listing the
// templates defined in FolidStruct (the Windows counterpart of
// extensions/folidstruct_menu.py). Choosing one opens the "New project"
// window of the app, which creates the folders.
//
// It is a packaged COM server (IExplorerCommand) declared in
// Package.appxmanifest, so on Windows 11 it appears in the modern menu.

#include <windows.h>
#include <appmodel.h>
#include <shlobj.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <shlguid.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <cwchar>
#include <string>
#include <vector>

#include "Args.h"
#include "Json.h"

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InProc;
using Microsoft::WRL::Make;
using Microsoft::WRL::Module;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

namespace
{
    HMODULE g_module = nullptr;

    // --- Package and files -------------------------------------------------

    std::wstring ModuleDirectory()
    {
        std::wstring path(MAX_PATH, L'\0');
        while (true)
        {
            const DWORD length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()));
            if (length == 0)
                return {};
            if (length < path.size())
            {
                path.resize(length);
                break;
            }
            path.resize(path.size() * 2);
        }
        return path.substr(0, path.find_last_of(L'\\'));
    }

    // Read a whole file, sharing it fully so that FolidStruct can replace it
    // while the menu reads it.
    std::string ToUtf8(const std::wstring& text)
    {
        if (text.empty())
            return {};
        const int length = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0,
                                               nullptr, nullptr);
        std::string result(length, '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), length, nullptr,
                            nullptr);
        return result;
    }

    // Diagnostics: %TEMP%\FolidStruct-menu.log, one line per menu call, kept small.
    void Log(const std::wstring& message)
    {
        wchar_t temp[MAX_PATH + 1] = {};
        if (!GetTempPathW(MAX_PATH + 1, temp))
            return;
        const std::wstring path = std::wstring(temp) + L"FolidStruct-menu.log";
        WIN32_FILE_ATTRIBUTE_DATA info = {};
        const bool tooBig = GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info)
                            && (info.nFileSizeHigh > 0 || info.nFileSizeLow > 256 * 1024);
        HANDLE file = CreateFileW(path.c_str(), tooBig ? GENERIC_WRITE : FILE_APPEND_DATA,
                                  FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                                  tooBig ? CREATE_ALWAYS : OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return;
        SYSTEMTIME now = {};
        GetLocalTime(&now);
        wchar_t prefix[64] = {};
        swprintf_s(prefix, L"%02u:%02u:%02u.%03u pid %lu: ", now.wHour, now.wMinute, now.wSecond,
                   now.wMilliseconds, GetCurrentProcessId());
        const std::string line = ToUtf8(prefix + message) + "\r\n";
        DWORD written = 0;
        WriteFile(file, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
        CloseHandle(file);
    }

    std::string ReadWholeFile(const std::wstring& path, DWORD limit)
    {
        if (path.empty())
            return {};
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                  nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return {};
        std::string text;
        LARGE_INTEGER size = {};
        if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart <= limit)
        {
            text.resize(static_cast<size_t>(size.QuadPart));
            DWORD read = 0;
            if (!::ReadFile(file, text.data(), static_cast<DWORD>(text.size()), &read, nullptr))
                read = 0;
            text.resize(read);
        }
        CloseHandle(file);
        return text;
    }

    std::wstring FromUtf8(const std::string& text)
    {
        size_t start = text.rfind("\xEF\xBB\xBF", 0) == 0 ? 3 : 0; // BOM
        if (text.size() <= start)
            return {};
        const int length = MultiByteToWideChar(CP_UTF8, 0, text.data() + start,
                                               static_cast<int>(text.size() - start), nullptr, 0);
        std::wstring result(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data() + start, static_cast<int>(text.size() - start),
                            result.data(), length);
        return result;
    }

    std::wstring XmlAttribute(const std::wstring& element, const std::wstring& name)
    {
        const std::wstring key = L" " + name + L"=\"";
        size_t start = element.find(key);
        if (start == std::wstring::npos)
            return {};
        start += key.size();
        const size_t end = element.find(L'"', start);
        if (end == std::wstring::npos)
            return {};
        std::wstring value = element.substr(start, end - start);
        // Unescape the XML entities that can appear in a publisher name.
        const std::pair<const wchar_t*, const wchar_t*> entities[] = {
            {L"&quot;", L"\""}, {L"&apos;", L"'"}, {L"&lt;", L"<"}, {L"&gt;", L">"}, {L"&amp;", L"&"}};
        for (const auto& [entity, text] : entities)
        {
            for (size_t at = value.find(entity); at != std::wstring::npos; at = value.find(entity, at + 1))
                value.replace(at, wcslen(entity), text);
        }
        return value;
    }

    // The package family name of FolidStruct, e.g. "rem2608.FolidStruct_abcdef123".
    std::wstring PackageFamilyName()
    {
        // The surrogate process normally runs with the package identity…
        UINT32 length = 0;
        if (GetCurrentPackageFamilyName(&length, nullptr) == ERROR_INSUFFICIENT_BUFFER)
        {
            std::wstring name(length, L'\0');
            if (GetCurrentPackageFamilyName(&length, name.data()) == ERROR_SUCCESS)
            {
                name.resize(length - 1);
                return name;
            }
        }

        // …otherwise from the install folder, named after the package full name
        // (C:\Program Files\WindowsApps\<name>_<version>_<arch>__<publisher id>)…
        const std::wstring directory = ModuleDirectory();
        const std::wstring fullName = directory.substr(directory.find_last_of(L'\\') + 1);
        length = 0;
        if (PackageFamilyNameFromFullName(fullName.c_str(), &length, nullptr) == ERROR_INSUFFICIENT_BUFFER)
        {
            std::wstring family(length, L'\0');
            if (PackageFamilyNameFromFullName(fullName.c_str(), &length, family.data()) == ERROR_SUCCESS)
            {
                family.resize(length - 1);
                return family;
            }
        }

        // …or from the manifest next to this DLL (a package registered unpacked).
        const std::wstring manifestPath = ModuleDirectory() + L"\\AppxManifest.xml";
        const std::wstring manifest = FromUtf8(ReadWholeFile(manifestPath, 1 << 20));
        const size_t start = manifest.find(L"<Identity");
        const size_t end = manifest.find(L'>', start);
        if (start == std::wstring::npos || end == std::wstring::npos)
            return {};
        std::wstring identity = manifest.substr(start, end - start);
        for (auto& c : identity)
        {
            if (c == L'\n' || c == L'\r' || c == L'\t')
                c = L' ';
        }
        std::wstring name = XmlAttribute(identity, L"Name");
        std::wstring publisher = XmlAttribute(identity, L"Publisher");
        if (name.empty() || publisher.empty())
            return {};

        PACKAGE_ID id = {};
        id.name = name.data();
        id.publisher = publisher.data();
        length = 0;
        if (PackageFamilyNameFromId(&id, &length, nullptr) != ERROR_INSUFFICIENT_BUFFER)
            return {};
        std::wstring family(length, L'\0');
        if (PackageFamilyNameFromId(&id, &length, family.data()) != ERROR_SUCCESS)
            return {};
        family.resize(length - 1);
        return family;
    }

    // The app keeps its files in the package's LocalState folder.
    std::wstring ConfigPath(const std::wstring& family)
    {
        PWSTR local = nullptr;
        if (family.empty() || FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)))
            return {};
        std::wstring path = std::wstring(local) + L"\\Packages\\" + family + L"\\LocalState\\config.json";
        CoTaskMemFree(local);
        return path;
    }

    // --- Configuration -----------------------------------------------------

    struct TemplateEntry
    {
        std::wstring id;
        std::wstring name;
    };

    struct MenuConfig
    {
        std::wstring family;
        std::wstring language; // "it" or "en"
        std::vector<TemplateEntry> templates;
    };

    std::wstring SystemLanguage()
    {
        return PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_ITALIAN ? L"it" : L"en";
    }

    MenuConfig LoadConfig()
    {
        MenuConfig config;
        config.family = PackageFamilyName();
        config.language = SystemLanguage();

        const std::wstring path = ConfigPath(config.family);
        if (path.empty() || GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            // Not created yet (FolidStruct never opened since installation): show the
            // example template the app will write; it is found by name.
            Log(L"menu: family '" + config.family + L"', no config at '" + path + L"', example template");
            if (!path.empty())
                config.templates.push_back({L"", config.language == L"it" ? L"Grafica" : L"Graphics"});
            return config;
        }

        json::Value root;
        bool parsed = false;
        size_t size = 0;
        for (int attempt = 0; attempt < 3 && !parsed; ++attempt)
        {
            if (attempt > 0)
                Sleep(50); // the app may be replacing the file
            const std::string text = ReadWholeFile(path, 16 << 20);
            size = text.size();
            parsed = json::Parse(FromUtf8(text), root);
        }
        Log(L"menu: family '" + config.family + L"', config '" + path + L"', " + std::to_wstring(size)
            + L" bytes, " + (parsed ? L"read" : L"NOT read"));
        if (!parsed)
            return config;

        // The language chosen in FolidStruct, or the system language ("auto").
        if (const auto* language = root.Get(L"language"))
        {
            if (language->Str() == L"it" || language->Str() == L"en")
                config.language = language->Str();
        }
        if (const auto* templates = root.Get(L"templates"); templates && templates->type == json::Value::Type::Array)
        {
            for (const auto& item : templates->items)
            {
                const auto* name = item.Get(L"name");
                const auto* id = item.Get(L"id");
                if (name && name->type == json::Value::Type::String && !name->string.empty())
                    config.templates.push_back({id ? id->Str() : L"", name->string});
            }
        }
        return config;
    }

    const wchar_t* Label(const MenuConfig& config, const wchar_t* english)
    {
        if (config.language != L"it")
            return english;
        if (wcscmp(english, L"Manage templates…") == 0)
            return L"Gestisci modelli…";
        if (wcscmp(english, L"New project…") == 0)
            return L"Nuovo progetto…";
        return english;
    }

    // --- Actions -----------------------------------------------------------

    // The path of a real folder on disk, or an empty string (e.g. This PC, a .zip file).
    std::wstring PathOf(IShellItem* item)
    {
        SFGAOF attributes = 0;
        if (!item || FAILED(item->GetAttributes(SFGAO_FOLDER | SFGAO_FILESYSTEM | SFGAO_STREAM, &attributes))
            || (attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM)) != (SFGAO_FOLDER | SFGAO_FILESYSTEM)
            || (attributes & SFGAO_STREAM)) // e.g. a .zip file
            return {};
        PWSTR path = nullptr;
        if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            return {};
        std::wstring result(path);
        CoTaskMemFree(path);
        return result;
    }

    // ShellWindows (all File Explorer windows and tabs), used through IDispatch
    // because <exdisp.h> clashes with the other shell headers.
    constexpr CLSID ShellWindowsClsid = {0x9BA05972, 0xF6A8, 0x11CF, {0xA4, 0x42, 0x00, 0xA0, 0xC9, 0x0A, 0x8F, 0x39}};

    HRESULT CallByName(IDispatch* object, const wchar_t* name, WORD flags, VARIANT* argument, VARIANT* result)
    {
        DISPID id = 0;
        LPOLESTR names[] = {const_cast<LPOLESTR>(name)};
        HRESULT hr = object->GetIDsOfNames(IID_NULL, names, 1, LOCALE_USER_DEFAULT, &id);
        if (FAILED(hr))
            return hr;
        DISPPARAMS params = {argument, nullptr, argument ? 1u : 0u, 0};
        return object->Invoke(id, IID_NULL, LOCALE_USER_DEFAULT, flags, &params, result, nullptr, nullptr);
    }

    // The folder of one File Explorer tab, if it belongs to `window`.
    ComPtr<IShellItem> TabFolder(IDispatch* tab, HWND window, HWND* tabWindow)
    {
        ComPtr<IServiceProvider> provider;
        ComPtr<IShellBrowser> browser;
        ComPtr<IShellView> view;
        ComPtr<IFolderView> folderView;
        ComPtr<IPersistFolder2> folder;
        PIDLIST_ABSOLUTE pidl = nullptr;
        if (FAILED(tab->QueryInterface(IID_PPV_ARGS(&provider)))
            || FAILED(provider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser)))
            || FAILED(browser->GetWindow(tabWindow)) || GetAncestor(*tabWindow, GA_ROOT) != window
            || FAILED(browser->QueryActiveShellView(&view)) || FAILED(view.As(&folderView))
            || FAILED(folderView->GetFolder(IID_PPV_ARGS(&folder))) || FAILED(folder->GetCurFolder(&pidl)))
            return nullptr;
        ComPtr<IShellItem> item;
        const HRESULT hr = SHCreateItemFromIDList(pidl, IID_PPV_ARGS(&item));
        CoTaskMemFree(pidl);
        return SUCCEEDED(hr) ? item : nullptr;
    }

    // The folder shown by the File Explorer window in the foreground, i.e. the
    // one whose background was right-clicked, or the Desktop (the approach of
    // Windows Terminal).
    ComPtr<IShellItem> ForegroundExplorerFolder()
    {
        HWND window = GetForegroundWindow();
        if (window)
            window = GetAncestor(window, GA_ROOTOWNER);
        wchar_t className[64] = {};
        if (!window || !GetClassNameW(window, className, 64))
            return nullptr;
        // The desktop background: the user's Desktop folder.
        if (wcscmp(className, L"Progman") == 0 || wcscmp(className, L"WorkerW") == 0)
        {
            ComPtr<IShellItem> desktop;
            return SUCCEEDED(SHGetKnownFolderItem(FOLDERID_Desktop, KF_FLAG_DEFAULT, nullptr,
                                                  IID_PPV_ARGS(&desktop))) ? desktop : nullptr;
        }
        if (wcscmp(className, L"CabinetWClass") != 0)
            return nullptr;
        // Every tab is a separate shell window; the active tab is the first one.
        const HWND activeTab = FindWindowExW(window, nullptr, L"ShellTabWindowClass", nullptr);

        ComPtr<IDispatch> windows;
        VARIANT count;
        VariantInit(&count);
        if (FAILED(CoCreateInstance(ShellWindowsClsid, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&windows)))
            || FAILED(CallByName(windows.Get(), L"Count", DISPATCH_PROPERTYGET, nullptr, &count))
            || FAILED(VariantChangeType(&count, &count, 0, VT_I4)))
            return nullptr;

        ComPtr<IShellItem> firstMatch;
        for (LONG i = 0; i < count.lVal; ++i)
        {
            VARIANT index;
            VariantInit(&index);
            index.vt = VT_I4;
            index.lVal = i;
            VARIANT result;
            VariantInit(&result);
            if (FAILED(CallByName(windows.Get(), L"Item", DISPATCH_METHOD, &index, &result)))
                continue;
            ComPtr<IShellItem> item;
            HWND tab = nullptr;
            if (result.vt == VT_DISPATCH && result.pdispVal)
                item = TabFolder(result.pdispVal, window, &tab);
            VariantClear(&result);
            if (!item)
                continue;
            if (activeTab && tab == activeTab)
                return item;
            if (!firstMatch)
                firstMatch = item;
        }
        return firstMatch;
    }

    // The folder the menu was opened on (or in), if it is a real folder on disk.
    std::wstring FolderPath(IShellItemArray* items)
    {
        DWORD count = 0;
        if (items && SUCCEEDED(items->GetCount(&count)) && count > 0)
        {
            ComPtr<IShellItem> item;
            if (count != 1 || FAILED(items->GetItemAt(0, &item)))
                return {};
            return PathOf(item.Get());
        }
        // Background of a folder: the Windows 11 menu passes no items at all.
        const ComPtr<IShellItem> folder = ForegroundExplorerFolder();
        return PathOf(folder.Get());
    }

    // Start FolidStruct through its package, so that it runs with the package
    // identity (and finds its LocalState folder).
    HRESULT LaunchApp(const std::wstring& family, const std::wstring& arguments)
    {
        if (family.empty())
            return E_UNEXPECTED;
        ComPtr<IApplicationActivationManager> manager;
        HRESULT hr = CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_LOCAL_SERVER,
                                      IID_PPV_ARGS(&manager));
        if (FAILED(hr))
            return hr;
        CoAllowSetForegroundWindow(manager.Get(), nullptr);
        DWORD process = 0;
        return manager->ActivateApplication((family + L"!App").c_str(), arguments.c_str(), AO_NONE, &process);
    }

    // --- Commands ----------------------------------------------------------

    enum class Action { NewProject, Manage, Separator };

    // One entry of the submenu.
    class SubCommand final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
    {
    public:
        SubCommand(Action action, std::wstring title, std::wstring templateId, std::wstring templateName,
                   std::wstring family)
            : action(action), title(std::move(title)), templateId(std::move(templateId)),
              templateName(std::move(templateName)), family(std::move(family))
        {
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
        {
            *name = nullptr;
            return action == Action::Separator ? E_NOTIMPL : SHStrDupW(title.c_str(), name);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            *icon = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
        {
            *tip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* guid) override
        {
            *guid = GUID_NULL;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
        {
            *state = ECS_ENABLED;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            if (action == Action::Manage)
                return LaunchApp(family, L"");
            if (action != Action::NewProject)
                return S_OK;
            const std::wstring folder = FolderPath(items);
            if (folder.empty())
                return E_INVALIDARG;
            // By id; by name for a configuration the app has not upgraded yet.
            std::wstring arguments = L"--new";
            if (!templateId.empty())
                arguments += L" --template-id " + QuoteArg(templateId);
            else if (!templateName.empty())
                arguments += L" " + QuoteArg(templateName);
            arguments += L" --dir " + QuoteArg(folder);
            return LaunchApp(family, arguments);
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            *flags = action == Action::Separator ? ECF_ISSEPARATOR : ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        Action action;
        std::wstring title;
        std::wstring templateId;
        std::wstring templateName;
        std::wstring family;
    };

    class CommandList final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
    {
    public:
        explicit CommandList(std::vector<ComPtr<IExplorerCommand>> commands) : commands(std::move(commands)) {}

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** result, ULONG* fetched) override
        {
            ULONG done = 0;
            for (; done < count && position < commands.size(); ++done, ++position)
                commands[position].CopyTo(&result[done]);
            if (fetched)
                *fetched = done;
            return done == count ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Skip(ULONG count) override
        {
            position = (std::min)(position + count, commands.size());
            return position < commands.size() ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Reset() override
        {
            position = 0;
            return S_OK;
        }

        IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override
        {
            *result = nullptr;
            return E_NOTIMPL;
        }

    private:
        std::vector<ComPtr<IExplorerCommand>> commands;
        size_t position = 0;
    };
}

// The "FolidStruct" entry; its submenu is read from the configuration each
// time the menu opens, so it always shows the current templates.
class __declspec(uuid("8D25BF02-46D9-4D8F-987D-2E5F04A68DA2")) FolidStructCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        return SHStrDupW(L"FolidStruct", name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const std::wstring path = ModuleDirectory() + L"\\Assets\\FolidStruct.ico";
        return SHStrDupW(path.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        *guid = __uuidof(FolidStructCommand);
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        DWORD count = 0;
        if (items)
            items->GetCount(&count);
        wchar_t foreground[64] = L"-";
        if (HWND window = GetForegroundWindow())
            GetClassNameW(GetAncestor(window, GA_ROOTOWNER), foreground, 64);
        const std::wstring folder = FolderPath(items);
        Log(L"state: " + std::to_wstring(count) + L" items, foreground '" + foreground + L"', folder '" + folder
            + L"'");
        *state = folder.empty() ? ECS_HIDDEN : ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override
    {
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** result) override
    {
        *result = nullptr;
        const MenuConfig config = LoadConfig();
        std::vector<ComPtr<IExplorerCommand>> commands;
        for (const auto& entry : config.templates)
            commands.push_back(Make<SubCommand>(Action::NewProject, entry.name, entry.id, entry.name,
                                                config.family));
        if (commands.empty())
        {
            // No templates, or a damaged configuration: the app opens with the
            // first template, or explains what is wrong.
            commands.push_back(Make<SubCommand>(Action::NewProject, Label(config, L"New project…"), L"", L"",
                                                config.family));
        }
        commands.push_back(Make<SubCommand>(Action::Separator, L"", L"", L"", config.family));
        commands.push_back(Make<SubCommand>(Action::Manage, Label(config, L"Manage templates…"), L"", L"",
                                            config.family));
        auto list = Make<CommandList>(std::move(commands));
        return list ? list.CopyTo(result) : E_OUTOFMEMORY;
    }
};

CoCreatableClass(FolidStructCommand)

// --- DLL entry points ----------------------------------------------------------

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, object);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
