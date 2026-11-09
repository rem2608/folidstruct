#include "Json.h"

#include <cwchar>

namespace json
{
    namespace
    {
        constexpr int MaxDepth = 64;

        class Parser
        {
        public:
            explicit Parser(const std::wstring& text) : s(text) {}

            bool Document(Value& out)
            {
                SkipSpace();
                if (!ParseValue(out, 0))
                    return false;
                SkipSpace();
                return pos == s.size();
            }

        private:
            const std::wstring& s;
            size_t pos = 0;

            bool AtEnd() const { return pos >= s.size(); }
            wchar_t Peek() const { return AtEnd() ? L'\0' : s[pos]; }

            void SkipSpace()
            {
                while (!AtEnd() && (s[pos] == L' ' || s[pos] == L'\t' || s[pos] == L'\n' || s[pos] == L'\r'))
                    ++pos;
            }

            bool Literal(const wchar_t* word)
            {
                for (size_t i = 0; word[i]; ++i)
                {
                    if (pos + i >= s.size() || s[pos + i] != word[i])
                        return false;
                }
                pos += wcslen(word);
                return true;
            }

            bool ParseValue(Value& out, int depth)
            {
                if (depth > MaxDepth)
                    return false;
                switch (Peek())
                {
                case L'{': return ParseObject(out, depth);
                case L'[': return ParseArray(out, depth);
                case L'"':
                    out.type = Value::Type::String;
                    return ParseString(out.string);
                case L't':
                    out.type = Value::Type::Bool;
                    out.boolean = true;
                    return Literal(L"true");
                case L'f':
                    out.type = Value::Type::Bool;
                    out.boolean = false;
                    return Literal(L"false");
                case L'n':
                    out.type = Value::Type::Null;
                    return Literal(L"null");
                default:
                    return ParseNumber(out);
                }
            }

            bool ParseNumber(Value& out)
            {
                // Numbers are not needed by the menu: validate and skip them.
                const size_t start = pos;
                if (Peek() == L'-')
                    ++pos;
                bool digits = false;
                while (!AtEnd())
                {
                    const wchar_t c = s[pos];
                    if (c >= L'0' && c <= L'9')
                        digits = true;
                    else if (c != L'.' && c != L'e' && c != L'E' && c != L'+' && c != L'-')
                        break;
                    ++pos;
                }
                out.type = Value::Type::Number;
                return digits && pos > start;
            }

            static int Hex(wchar_t c)
            {
                if (c >= L'0' && c <= L'9') return c - L'0';
                if (c >= L'a' && c <= L'f') return c - L'a' + 10;
                if (c >= L'A' && c <= L'F') return c - L'A' + 10;
                return -1;
            }

            bool ParseString(std::wstring& out)
            {
                ++pos; // opening quote
                out.clear();
                while (!AtEnd())
                {
                    const wchar_t c = s[pos++];
                    if (c == L'"')
                        return true;
                    if (c < 0x20)
                        return false;
                    if (c != L'\\')
                    {
                        out.push_back(c);
                        continue;
                    }
                    if (AtEnd())
                        return false;
                    switch (s[pos++])
                    {
                    case L'"': out.push_back(L'"'); break;
                    case L'\\': out.push_back(L'\\'); break;
                    case L'/': out.push_back(L'/'); break;
                    case L'b': out.push_back(L'\b'); break;
                    case L'f': out.push_back(L'\f'); break;
                    case L'n': out.push_back(L'\n'); break;
                    case L'r': out.push_back(L'\r'); break;
                    case L't': out.push_back(L'\t'); break;
                    case L'u':
                    {
                        // UTF-16 code units map directly onto std::wstring,
                        // surrogate pairs included.
                        if (pos + 4 > s.size())
                            return false;
                        int code = 0;
                        for (int i = 0; i < 4; ++i)
                        {
                            const int h = Hex(s[pos++]);
                            if (h < 0)
                                return false;
                            code = code * 16 + h;
                        }
                        out.push_back(static_cast<wchar_t>(code));
                        break;
                    }
                    default:
                        return false;
                    }
                }
                return false;
            }

            bool ParseArray(Value& out, int depth)
            {
                ++pos; // [
                out.type = Value::Type::Array;
                SkipSpace();
                if (Peek() == L']')
                {
                    ++pos;
                    return true;
                }
                while (true)
                {
                    SkipSpace();
                    out.items.emplace_back();
                    if (!ParseValue(out.items.back(), depth + 1))
                        return false;
                    SkipSpace();
                    if (Peek() == L',')
                    {
                        ++pos;
                        continue;
                    }
                    if (Peek() == L']')
                    {
                        ++pos;
                        return true;
                    }
                    return false;
                }
            }

            bool ParseObject(Value& out, int depth)
            {
                ++pos; // {
                out.type = Value::Type::Object;
                SkipSpace();
                if (Peek() == L'}')
                {
                    ++pos;
                    return true;
                }
                while (true)
                {
                    SkipSpace();
                    if (Peek() != L'"')
                        return false;
                    std::wstring key;
                    if (!ParseString(key))
                        return false;
                    SkipSpace();
                    if (Peek() != L':')
                        return false;
                    ++pos;
                    SkipSpace();
                    out.members.emplace_back(std::move(key), Value{});
                    if (!ParseValue(out.members.back().second, depth + 1))
                        return false;
                    SkipSpace();
                    if (Peek() == L',')
                    {
                        ++pos;
                        continue;
                    }
                    if (Peek() == L'}')
                    {
                        ++pos;
                        return true;
                    }
                    return false;
                }
            }
        };
    }

    const Value* Value::Get(const std::wstring& key) const
    {
        if (type != Type::Object)
            return nullptr;
        for (const auto& [name, value] : members)
        {
            if (name == key)
                return &value;
        }
        return nullptr;
    }

    const std::wstring& Value::Str() const
    {
        static const std::wstring empty;
        return type == Type::String ? string : empty;
    }

    bool Parse(const std::wstring& text, Value& result)
    {
        result = Value{};
        return Parser(text).Document(result);
    }
}
