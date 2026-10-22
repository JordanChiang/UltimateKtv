using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace UltimateKtv
{
    /// <summary>載入設定檔的結果：值一定可用；有問題的項目已退回預設值。</summary>
    public sealed class SettingsLoadResult<T>
    {
        internal SettingsLoadResult(T value) => Value = value;

        public T Value { get; }

        /// <summary>整個檔案無法解析 (語法錯、空檔案)，全部使用預設值。</summary>
        public bool SyntaxError { get; internal set; }

        /// <summary>內容錯誤而退回預設值的項目 (型別不符、null、數字超出型別範圍)。</summary>
        public List<string> InvalidItems { get; } = new();

        /// <summary>檔案中沒有、使用預設值的項目。</summary>
        public List<string> MissingItems { get; } = new();

        /// <summary>項目名稱大小寫不同而對應到的項目 (值已讀入；存檔時會改成正確名稱)。</summary>
        public List<string> MisnamedItems { get; } = new();

        /// <summary>需要存檔 (補上缺少的項目、修正名稱或退回預設值)。</summary>
        public bool NeedsSave => HasProblems || MissingItems.Count > 0 || MisnamedItems.Count > 0;

        /// <summary>原檔有內容會在存檔時遺失，存檔前應先備份。</summary>
        public bool HasProblems => SyntaxError || InvalidItems.Count > 0;
    }

    /// <summary>
    /// 容錯的設定檔 (JSON) 載入：一個項目寫錯只讓該項退回預設值，不會整個檔案重設。
    /// <list type="bullet">
    /// <item>項目名稱不分大小寫。</item>
    /// <item>數字可寫成字串 ("300")；布林可寫成 "true"/"false" 或 1/0。</item>
    /// <item>null 只在預設值本來就是 null 時接受，否則退回預設值。</item>
    /// <item>整數欄位給小數、型別不符、超出型別範圍：該項退回預設值。</item>
    /// </list>
    /// </summary>
    public static class SettingsJson
    {
        private static readonly JsonDocumentOptions DocumentOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static SettingsLoadResult<T> Load<T>(string json) where T : new()
        {
            var value = new T();
            var result = new SettingsLoadResult<T>(value);
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .ToList();

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, DocumentOptions);
            }
            catch (JsonException)
            {
                result.SyntaxError = true;
                result.MissingItems.AddRange(properties.Select(p => p.Name));
                return result;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.SyntaxError = true;
                    result.MissingItems.AddRange(properties.Select(p => p.Name));
                    return result;
                }

                var elements = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var elementsIgnoreCase = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in doc.RootElement.EnumerateObject())
                {
                    elements[item.Name] = item.Value;
                    elementsIgnoreCase.TryAdd(item.Name, item.Value);
                }

                foreach (var prop in properties)
                {
                    if (!elements.TryGetValue(prop.Name, out var element))
                    {
                        if (!elementsIgnoreCase.TryGetValue(prop.Name, out element))
                        {
                            result.MissingItems.Add(prop.Name);
                            continue;
                        }
                        result.MisnamedItems.Add(prop.Name);
                    }

                    object? defaultValue = prop.GetValue(value);
                    if (TryConvert(element, prop.PropertyType, defaultValue, out object? converted))
                    {
                        prop.SetValue(value, converted);
                    }
                    else
                    {
                        result.InvalidItems.Add(prop.Name);
                    }
                }
            }

            return result;
        }

        /// <summary>把原檔複製成 <c>&lt;檔名&gt;.bak</c> (覆蓋上一份備份)。</summary>
        /// <returns>備份檔路徑；原檔不存在或複製失敗時為 null。</returns>
        public static string? Backup(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string backupPath = path + ".bak";
                File.Copy(path, backupPath, overwrite: true);
                return backupPath;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to back up {path}", ex);
                return null;
            }
        }

        private static bool TryConvert(JsonElement e, Type type, object? defaultValue, out object? value)
        {
            value = null;
            var t = Nullable.GetUnderlyingType(type) ?? type;

            if (e.ValueKind == JsonValueKind.Null)
            {
                // 預設值本來就是 null (可為 null 的欄位) 才接受 null
                return defaultValue == null && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null);
            }

            if (t == typeof(string))
            {
                if (e.ValueKind != JsonValueKind.String) return false;
                value = e.GetString();
                return value != null;
            }

            if (t == typeof(bool))
            {
                switch (e.ValueKind)
                {
                    case JsonValueKind.True: value = true; return true;
                    case JsonValueKind.False: value = false; return true;
                    case JsonValueKind.String when bool.TryParse(e.GetString()?.Trim(), out bool b): value = b; return true;
                    case JsonValueKind.Number when e.TryGetInt32(out int n) && (n == 0 || n == 1): value = n == 1; return true;
                    default: return false;
                }
            }

            if (t == typeof(int))
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i)) { value = i; return true; }
                if (e.ValueKind == JsonValueKind.String &&
                    int.TryParse(e.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) { value = i; return true; }
                return false;
            }

            if (t == typeof(long))
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long l)) { value = l; return true; }
                if (e.ValueKind == JsonValueKind.String &&
                    long.TryParse(e.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) { value = l; return true; }
                return false;
            }

            if (t == typeof(double))
            {
                double d;
                if (e.ValueKind == JsonValueKind.Number) d = e.GetDouble();
                else if (e.ValueKind != JsonValueKind.String ||
                         !double.TryParse(e.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                value = d;
                return true;
            }

            // 其他型別 (清單、列舉等) 交給序列化器，失敗就退回預設值
            try
            {
                value = e.Deserialize(type);
                return value != null || defaultValue == null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
