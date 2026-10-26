using System;
using System.Collections.Generic;
using System.Globalization;

namespace UltimateKtv
{
    /// <summary>
    /// 歌庫中的一位歌星 (ktv_Singer 的一列)，載入後不變。
    /// </summary>
    public sealed record Singer
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        /// <summary>歌手類別 (Singer_Type) 的原始文字，未設定為空字串。數字意義見 <see cref="SingerCategory"/>。</summary>
        public string Type { get; init; } = "";
        /// <summary>注音 (Singer_Spell)。</summary>
        public string Spell { get; init; } = "";
        /// <summary>筆畫輸入碼 (Singer_PenStyle)。</summary>
        public string PenStyle { get; init; } = "";

        /// <summary>載入時的原始欄位值，供手機網頁依欄位名稱輸出使用。</summary>
        public IReadOnlyDictionary<string, object?> Columns { get; init; } = EmptyColumns;

        private static readonly IReadOnlyDictionary<string, object?> EmptyColumns = new Dictionary<string, object?>();

        /// <summary>依 ktv_Singer 欄位名稱取載入時的原始值；沒有這個欄位時為 null。</summary>
        public object? GetColumnValue(string column) => Columns.TryGetValue(column, out var value) ? value : null;

        /// <summary>從 ktv_Singer 查詢結果的一列建立；欄位缺少或為 NULL 時為空字串。</summary>
        public static Singer FromColumns(IReadOnlyDictionary<string, object?> columns)
        {
            string Text(string key) => columns.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

            return new Singer
            {
                Id = Text("Singer_Id"),
                Name = Text("Singer_Name"),
                Type = Text("Singer_Type"),
                Spell = Text("Singer_Spell"),
                PenStyle = Text("Singer_PenStyle"),
                Columns = columns,
            };
        }
    }
}
