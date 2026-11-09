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
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <fstream>
#include <sstream>
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

    std::string ReadFile(const std::wstring& path, size_t limit)
    {
        std::ifstream file(path, std::ios::binary);
        if (!file)
            return {};
        std::ostringstream content;
        content << file.rdbuf();
        std::string text = content.str();
        return text.size() <= limit ? text : std::string();
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

        // …otherwise work it out from the manifest next to this DLL.
        const std::wstring manifest = FromUtf8(ReadFile(ModuleDirectory() + L"\\AppxManifest.xml", 1 << 20));
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

        json::Value root;
        if (!json::Parse(FromUtf8(ReadFile(ConfigPath(config.family), 16 << 20)), root))
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

    // The folder the menu was opened on (or in), if it is a real folder on disk.
    std::wstring FolderPath(IShellItemArray* items)
    {
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count)) || count != 1)
            return {};
        ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(0, &item)))
            return {};
        SFGAOF attributes = 0;
        if (FAILED(item->GetAttributes(SFGAO_FOLDER | SFGAO_FILESYSTEM | SFGAO_STREAM, &attributes))
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
        SubCommand(Action action, std::wstring title, std::wstring templateId, std::wstring family)
            : action(action), title(std::move(title)), templateId(std::move(templateId)), family(std::move(family))
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
            std::wstring arguments = L"--new";
            if (!templateId.empty())
                arguments += L" --template-id " + QuoteArg(templateId);
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
        *state = FolderPath(items).empty() ? ECS_HIDDEN : ECS_ENABLED;
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
            commands.push_back(Make<SubCommand>(Action::NewProject, entry.name, entry.id, config.family));
        if (commands.empty())
        {
            // First use, before FolidStruct has saved its templates: the app
            // writes the default ones and opens with the first template.
            commands.push_back(Make<SubCommand>(Action::NewProject, Label(config, L"New project…"), L"",
                                                config.family));
        }
        commands.push_back(Make<SubCommand>(Action::Separator, L"", L"", config.family));
        commands.push_back(Make<SubCommand>(Action::Manage, Label(config, L"Manage templates…"), L"",
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
