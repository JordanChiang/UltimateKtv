using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace UltimateKtv
{
    /// <summary>
    /// 歌庫中的一首歌 (ktv_Song 的一列)。
    /// 播放次數、音量、伴唱位置與檔案路徑會在執行中更新，其餘欄位載入後不變。
    /// </summary>
    public sealed record Song
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Singer { get; init; } = "";
        public string Lang { get; init; } = "";
        /// <summary>歌手類別 (Song_SingerType)，未設定為 -1。</summary>
        public int SingerType { get; init; } = -1;
        public int WordCount { get; init; }
        public int SongStroke { get; init; }
        public string Spell { get; init; } = "";
        public string PenStyle { get; init; } = "";
        public DateTime? CreatDate { get; init; }
        /// <summary>歌庫中記錄的資料夾 (Song_Path)。</summary>
        public string Path { get; init; } = "";
        public string FileName { get; init; } = "";

        public int PlayCount { get; set; }
        /// <summary>歌庫記錄的音量 (Song_Volume)，未設定為 null。</summary>
        public int? Volume { get; set; }
        /// <summary>伴唱所在的音軌/聲道 (Song_Track)，未設定為 null。</summary>
        public int? Track { get; set; }
        /// <summary>實際播放的完整路徑，依歌庫磁碟設定由 Path + FileName 組成。</summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// 載入時的原始欄位值，供手機網頁依欄位名稱查詢 (RowFilter) 與輸出使用。
        /// 會變動的欄位請改讀上面的屬性，見 <see cref="GetColumnValue"/>。
        /// </summary>
        public IReadOnlyDictionary<string, object?> Columns { get; init; } = EmptyColumns;

        private static readonly IReadOnlyDictionary<string, object?> EmptyColumns = new Dictionary<string, object?>();

        /// <summary>依 ktv_Song 欄位名稱取值；會變動的欄位回傳目前值，其他欄位回傳載入時的原始值。</summary>
        public object? GetColumnValue(string column) => column switch
        {
            "Song_PlayCount" => PlayCount,
            "Song_Volume" => Volume,
            "Song_Track" => Track,
            "FilePath" => FilePath,
            _ => Columns.TryGetValue(column, out var value) ? value : null,
        };

        /// <summary>從 ktv_Song 查詢結果的一列建立；欄位缺少或為 NULL 時使用預設值。</summary>
        public static Song FromColumns(IReadOnlyDictionary<string, object?> columns)
        {
            string Text(string key) => columns.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
            int? Number(string key) => columns.TryGetValue(key, out var v) ? ToInt(v) : null;

            return new Song
            {
                Id = Text("Song_Id"),
                Name = Text("Song_SongName"),
                Singer = Text("Song_Singer"),
                Lang = Text("Song_Lang"),
                SingerType = Number("Song_SingerType") ?? -1,
                WordCount = Number("Song_WordCount") ?? 0,
                SongStroke = Number("Song_SongStroke") ?? 0,
                Spell = Text("Song_Spell"),
                PenStyle = Text("Song_PenStyle"),
                CreatDate = columns.TryGetValue("Song_CreatDate", out var date) ? ToDate(date) : null,
                Path = Text("Song_Path"),
                FileName = Text("Song_FileName"),
                PlayCount = Number("Song_PlayCount") ?? 0,
                Volume = Number("Song_Volume"),
                Track = Number("Song_Track"),
                FilePath = Text("FilePath"),
                Columns = columns,
            };
        }

        /// <summary>從 DataRow 建立；DBNull 視為 NULL。</summary>
        public static Song FromDataRow(DataRow row)
        {
            var columns = new Dictionary<string, object?>(row.Table.Columns.Count);
            foreach (DataColumn col in row.Table.Columns)
            {
                columns[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            return FromColumns(columns);
        }

        private static int? ToInt(object? value)
        {
            switch (value)
            {
                case null or DBNull: return null;
                case int i: return i;
                case long l: return (int)l;
                case short s: return s;
                case byte b: return b;
                case double d: return (int)Math.Round(d);
                case float f: return (int)Math.Round(f);
                case decimal m: return (int)Math.Round(m);
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)) return (int)Math.Round(real);
            return null;
        }

        private static DateTime? ToDate(object? value) => value switch
        {
            DateTime dt => dt,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            _ => null,
        };
    }
}
