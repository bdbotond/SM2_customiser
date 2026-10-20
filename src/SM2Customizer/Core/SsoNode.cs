using System;
using System.Collections.Generic;
using System.Text;

namespace SM2Customizer.Core
{
    public abstract class SsoNode
    {
        public abstract string Serialize(int indent = 0);

        protected static string GetIndent(int indent) => new string(' ', indent * 3);
    }

    public class SsoValue : SsoNode
    {
        public string RawValue { get; set; } = string.Empty;
        public SsoValueType ValueType { get; set; } = SsoValueType.String;

        public SsoValue() { }

        public SsoValue(string raw, SsoValueType type)
        {
            RawValue = raw;
            ValueType = type;
        }

        public static SsoValue FromString(string str) => new SsoValue(str, SsoValueType.String);
        public static SsoValue FromBool(bool b) => new SsoValue(b ? "True" : "False", SsoValueType.Boolean);
        public static SsoValue FromNumber(int num) => new SsoValue(num.ToString(), SsoValueType.Number);
        public static SsoValue FromNumber(double num) => new SsoValue(num.ToString(System.Globalization.CultureInfo.InvariantCulture), SsoValueType.Number);

        public override string Serialize(int indent = 0)
        {
            if (ValueType == SsoValueType.String)
            {
                return $"\"{RawValue}\"";
            }
            return RawValue;
        }

        public override string ToString() => RawValue;
    }

    public enum SsoValueType
    {
        String,
        Number,
        Boolean,
        Identifier
    }

    public class SsoProperty
    {
        public string Key { get; set; } = string.Empty;
        public SsoNode Value { get; set; } = new SsoValue();

        public SsoProperty() { }

        public SsoProperty(string key, SsoNode value)
        {
            Key = key;
            Value = value;
        }

        public string Serialize(int indent)
        {
            var prefix = new string(' ', indent * 3);
            var keyStr = NeedsQuotes(Key) ? $"\"{Key}\"" : Key;

            if (Value is SsoObject obj)
            {
                return $"{prefix}{keyStr}   =   {{\n{obj.Serialize(indent + 1)}\n{prefix}}}";
            }
            if (Value is SsoArray arr)
            {
                return $"{prefix}{keyStr}   =   [\n{arr.Serialize(indent + 1)}\n{prefix}]";
            }
            return $"{prefix}{keyStr}   =   {Value.Serialize(indent)}";
        }

        private static bool NeedsQuotes(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            if (key.Contains(" ") || key.Contains("#") || key.Contains("-") || key.Contains(".")) return true;
            return false;
        }
    }

    public class SsoObject : SsoNode
    {
        public List<SsoProperty> Properties { get; } = new List<SsoProperty>();

        public SsoNode? this[string key]
        {
            get => Properties.Find(p => p.Key == key)?.Value;
            set
            {
                var existing = Properties.Find(p => p.Key == key);
                if (existing != null)
                {
                    if (value == null)
                        Properties.Remove(existing);
                    else
                        existing.Value = value;
                }
                else if (value != null)
                {
                    Properties.Add(new SsoProperty(key, value));
                }
            }
        }

        public SsoObject GetOrCreateObject(string key)
        {
            var node = this[key];
            if (node is SsoObject obj) return obj;
            var newObj = new SsoObject();
            this[key] = newObj;
            return newObj;
        }

        public SsoArray GetOrCreateArray(string key)
        {
            var node = this[key];
            if (node is SsoArray arr) return arr;
            var newArr = new SsoArray();
            this[key] = newArr;
            return newArr;
        }

        public string? GetString(string key)
        {
            if (this[key] is SsoValue val) return val.RawValue;
            return null;
        }

        public bool? GetBool(string key)
        {
            if (this[key] is SsoValue val)
            {
                if (string.Equals(val.RawValue, "True", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(val.RawValue, "False", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return null;
        }

        public int? GetInt(string key)
        {
            if (this[key] is SsoValue val && int.TryParse(val.RawValue, out int result))
            {
                return result;
            }
            return null;
        }

        public void SetString(string key, string val) => this[key] = SsoValue.FromString(val);
        public void SetBool(string key, bool val) => this[key] = SsoValue.FromBool(val);
        public void SetInt(string key, int val) => this[key] = SsoValue.FromNumber(val);

        public override string Serialize(int indent = 0)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Properties.Count; i++)
            {
                sb.Append(Properties[i].Serialize(indent));
                if (i < Properties.Count - 1)
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
    }

    public class SsoArray : SsoNode
    {
        public List<SsoNode> Elements { get; } = new List<SsoNode>();

        public void AddString(string str) => Elements.Add(SsoValue.FromString(str));

        public bool ContainsString(string str)
        {
            return Elements.Exists(e => e is SsoValue val && string.Equals(val.RawValue, str, StringComparison.OrdinalIgnoreCase));
        }

        public override string Serialize(int indent = 0)
        {
            var sb = new StringBuilder();
            var prefix = GetIndent(indent);

            for (int i = 0; i < Elements.Count; i++)
            {
                var el = Elements[i];
                if (el is SsoObject obj)
                {
                    sb.Append($"{prefix}{{\n{obj.Serialize(indent + 1)}\n{prefix}}}");
                }
                else if (el is SsoArray nestedArr)
                {
                    sb.Append($"{prefix}[\n{nestedArr.Serialize(indent + 1)}\n{prefix}]");
                }
                else
                {
                    sb.Append($"{prefix}{el.Serialize(indent)}");
                }

                if (i < Elements.Count - 1)
                {
                    sb.Append(",");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
    }
}
