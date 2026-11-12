// Command line quoting, the inverse of CommandLineToArgvW: the app receives
// each argument exactly as given, whatever spaces, quotes or trailing
// backslashes it contains (e.g. "C:\").
#pragma once

#include <string>

inline std::wstring QuoteArg(const std::wstring& arg)
{
    if (!arg.empty() && arg.find_first_of(L" \t\n\v\"") == std::wstring::npos)
        return arg;

    std::wstring result = L"\"";
    size_t backslashes = 0;
    for (const wchar_t c : arg)
    {
        if (c == L'\\')
        {
            ++backslashes;
            continue;
        }
        if (c == L'"')
            // Backslashes before a quote are doubled, and the quote escaped.
            result.append(backslashes * 2 + 1, L'\\');
        else
            result.append(backslashes, L'\\');
        backslashes = 0;
        result.push_back(c);
    }
    // Backslashes before the closing quote are doubled.
    result.append(backslashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}
