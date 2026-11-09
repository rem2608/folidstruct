// A small JSON reader: enough to read FolidStruct's config.json safely,
// without depending on anything outside the Windows SDK.
#pragma once

#include <string>
#include <utility>
#include <vector>

namespace json
{
    struct Value
    {
        enum class Type { Null, Bool, Number, String, Array, Object };

        Type type = Type::Null;
        bool boolean = false;
        std::wstring string;                                 // String
        std::vector<Value> items;                            // Array
        std::vector<std::pair<std::wstring, Value>> members; // Object

        // The member called `key`, or nullptr.
        const Value* Get(const std::wstring& key) const;
        // The string content if this is a string, else an empty string.
        const std::wstring& Str() const;
    };

    // Parse UTF-16 text; returns false on malformed input.
    bool Parse(const std::wstring& text, Value& result);
}
