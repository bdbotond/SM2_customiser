using System;
using System.Collections.Generic;
using System.Text;

namespace SM2Customizer.Core
{
    public class SsoParser
    {
        private readonly string _text;
        private int _pos;

        public SsoParser(string text)
        {
            _text = text ?? string.Empty;
            _pos = 0;
        }

        public static SsoObject Parse(string text)
        {
            var parser = new SsoParser(text);
            return parser.ParseObjectBody();
        }

        private SsoObject ParseObjectBody()
        {
            var obj = new SsoObject();

            while (_pos < _text.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _text.Length) break;

                char c = _text[_pos];
                if (c == '}')
                {
                    break;
                }

                int prevPos = _pos;

                // Parse key
                string key = ParseKey();
                if (string.IsNullOrEmpty(key))
                {
                    break;
                }

                SkipWhitespaceAndComments();

                // Expect '='
                if (_pos < _text.Length && _text[_pos] == '=')
                {
                    _pos++; // skip '='
                    SkipWhitespaceAndComments();
                }

                // Parse value
                var value = ParseNode();
                if (value != null)
                {
                    obj[key] = value;
                }

                SkipWhitespaceAndComments();
                // Optional trailing comma or semicolon
                if (_pos < _text.Length && (_text[_pos] == ',' || _text[_pos] == ';'))
                {
                    _pos++;
                }

                if (_pos <= prevPos)
                {
                    _pos++;
                }
            }

            return obj;
        }

        private SsoNode? ParseNode()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length) return null;

            char c = _text[_pos];

            if (c == '{')
            {
                _pos++; // skip '{'
                var obj = ParseObjectBody();
                SkipWhitespaceAndComments();
                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    _pos++; // skip '}'
                }
                return obj;
            }

            if (c == '[')
            {
                _pos++; // skip '['
                var arr = ParseArrayBody();
                SkipWhitespaceAndComments();
                if (_pos < _text.Length && _text[_pos] == ']')
                {
                    _pos++; // skip ']'
                }
                return arr;
            }

            if (c == '"')
            {
                return new SsoValue(ParseQuotedString(), SsoValueType.String);
            }

            // Word / Number / Boolean
            string word = ParseWord();
            if (string.Equals(word, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(word, "False", StringComparison.OrdinalIgnoreCase))
            {
                return new SsoValue(word, SsoValueType.Boolean);
            }

            if (double.TryParse(word, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return new SsoValue(word, SsoValueType.Number);
            }

            return new SsoValue(word, SsoValueType.Identifier);
        }

        private SsoArray ParseArrayBody()
        {
            var arr = new SsoArray();

            while (_pos < _text.Length)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _text.Length) break;

                if (_text[_pos] == ']')
                {
                    break;
                }

                int prevPos = _pos;
                var node = ParseNode();
                if (node != null)
                {
                    arr.Elements.Add(node);
                }

                SkipWhitespaceAndComments();
                if (_pos < _text.Length && _text[_pos] == ',')
                {
                    _pos++;
                }

                if (_pos <= prevPos)
                {
                    _pos++;
                }
            }

            return arr;
        }

        private string ParseKey()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length) return string.Empty;

            if (_text[_pos] == '"')
            {
                return ParseQuotedString();
            }

            return ParseWord();
        }

        private string ParseQuotedString()
        {
            if (_pos >= _text.Length || _text[_pos] != '"') return string.Empty;
            _pos++; // skip opening quote

            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == '\\')
                {
                    _pos++;
                    if (_pos < _text.Length)
                    {
                        sb.Append(_text[_pos]);
                        _pos++;
                    }
                    continue;
                }
                if (c == '"')
                {
                    _pos++; // skip closing quote
                    break;
                }
                sb.Append(c);
                _pos++;
            }

            return sb.ToString();
        }

        private string ParseWord()
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (char.IsWhiteSpace(c) || c == '=' || c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == ';' || c == '"')
                {
                    break;
                }
                sb.Append(c);
                _pos++;
            }
            return sb.ToString();
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                // Whitespace
                if (char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                    continue;
                }

                // Line comment //
                if (_pos + 1 < _text.Length && _text[_pos] == '/' && _text[_pos + 1] == '/')
                {
                    _pos += 2;
                    while (_pos < _text.Length && _text[_pos] != '\n' && _text[_pos] != '\r')
                    {
                        _pos++;
                    }
                    continue;
                }

                break;
            }
        }
    }
}
