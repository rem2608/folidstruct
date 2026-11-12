// Tests for the portable parts of the File Explorer menu (JSON reader and
// argument quoting). Builds anywhere, e.g. from this folder:
//   g++ -std=c++20 -I../../src/FolidStruct.ContextMenu MenuTests.cpp
//       ../../src/FolidStruct.ContextMenu/Json.cpp -o menu-tests
//   cl /std:c++20 /EHsc /utf-8 /I..\..\src\FolidStruct.ContextMenu MenuTests.cpp
//       ..\..\src\FolidStruct.ContextMenu\Json.cpp

#include <cstdio>
#include <string>
#include <vector>

#include "Args.h"
#include "Json.h"

static int failures = 0;

#define CHECK(condition)                                                  \
    do                                                                    \
    {                                                                     \
        if (!(condition))                                                 \
        {                                                                 \
            std::printf("FAIL line %d: %s\n", __LINE__, #condition);     \
            ++failures;                                                   \
        }                                                                 \
    } while (0)

// The reverse of QuoteArg, following the CommandLineToArgvW rules.
static std::vector<std::wstring> Split(const std::wstring& line)
{
    std::vector<std::wstring> args;
    size_t i = 0;
    while (i < line.size())
    {
        while (i < line.size() && line[i] == L' ')
            ++i;
        if (i >= line.size())
            break;
        std::wstring arg;
        bool quoted = false;
        while (i < line.size() && (quoted || line[i] != L' '))
        {
            size_t backslashes = 0;
            while (i < line.size() && line[i] == L'\\')
            {
                ++backslashes;
                ++i;
            }
            if (i < line.size() && line[i] == L'"')
            {
                arg.append(backslashes / 2, L'\\');
                if (backslashes % 2)
                    arg.push_back(L'"');
                else
                    quoted = !quoted;
                ++i;
            }
            else
            {
                arg.append(backslashes, L'\\');
                if (i < line.size() && (quoted || line[i] != L' '))
                    arg.push_back(line[i++]);
            }
        }
        args.push_back(arg);
    }
    return args;
}

static void TestQuoting()
{
    const std::wstring cases[] = {
        L"simple", L"C:\\", L"C:\\Users\\Marco\\Progetti 2026\\", L"with \"quotes\"", L"a\\\\\"b",
        L"\\\\server\\share\\dir with space", L"", L"tab\there", L"àèìòù ✓"};
    for (const auto& value : cases)
    {
        const auto args = Split(L"--dir " + QuoteArg(value) + L" --end");
        CHECK(args.size() == 3);
        if (args.size() == 3)
        {
            CHECK(args[1] == value);
            CHECK(args[2] == L"--end");
        }
    }
    CHECK(QuoteArg(L"abc") == L"abc");
    CHECK(QuoteArg(L"C:\\a b\\") == L"\"C:\\a b\\\\\"");
}

static void TestJson()
{
    json::Value root;
    const std::wstring config = LR"({
  "version": 2,
  "language": "it",
  "format": {"separator": " - ", "blocks": [{"kind": "date", "enabled": true}, {"kind": "name"}]},
  "templates": [
    {"id": "a1", "name": "Grafica", "folders": ["Font", "Immagini"], "custom_format": false,
     "format": {"separator": " ", "blocks": []}},
    {"id": "b2", "name": "Vid\u00e9o \"HD\" \ud83c\udfac", "folders": [], "custom_format": true},
    {"name": 42},
    "junk"
  ],
  "extra": [1.5e3, -2, null, true, false, {}, []]
})";
    CHECK(json::Parse(config, root));
    CHECK(root.Get(L"language")->Str() == L"it");
    const auto* templates = root.Get(L"templates");
    CHECK(templates && templates->items.size() == 4);
    if (templates && templates->items.size() == 4)
    {
        CHECK(templates->items[0].Get(L"name")->Str() == L"Grafica");
        CHECK(templates->items[0].Get(L"id")->Str() == L"a1");
        const std::wstring video = templates->items[1].Get(L"name")->Str();
        CHECK(video.substr(0, 11) == L"Vid\u00e9o \"HD\" ");
        CHECK(templates->items[2].Get(L"name")->Str().empty()); // not a string
        CHECK(templates->items[3].Get(L"name") == nullptr);
    }
    CHECK(root.Get(L"missing") == nullptr);

    const wchar_t* bad[] = {L"", L"{", L"{\"a\":}", L"[1,]", L"{\"a\" 1}", L"\"unterminated",
                            L"{} extra", L"tru", L"\"bad \\x escape\"", L"[\"\\u12\"]"};
    for (const auto* text : bad)
        CHECK(!json::Parse(text, root));

    std::wstring deep(1000, L'[');
    deep += std::wstring(1000, L']');
    CHECK(!json::Parse(deep, root)); // too deep, but no crash
}

int main()
{
    TestQuoting();
    TestJson();
    if (failures == 0)
        std::printf("All menu tests passed\n");
    return failures == 0 ? 0 : 1;
}
