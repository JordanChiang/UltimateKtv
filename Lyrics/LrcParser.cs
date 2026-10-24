using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UltimateKtv.Models;

namespace UltimateKtv.Services
{
    public static class LrcParser
    {
        // Match timestamps like [01:23.45], [01:23.456] or [01:23]
        private static readonly Regex LrcTimeRegex = new Regex(@"\[(\d{2,}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

        public static List<LrcLine> Parse(string filePath)
        {
            var lrcLines = new List<LrcLine>();

            if (!File.Exists(filePath))
                return lrcLines;

            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var matches = LrcTimeRegex.Matches(line);
                    if (matches.Count > 0)
                    {
                        // Clean line text from timestamps
                        string text = LrcTimeRegex.Replace(line, "").Trim();

                        foreach (Match match in matches)
                        {
                            if (int.TryParse(match.Groups[1].Value, out int minutes) &&
                                int.TryParse(match.Groups[2].Value, out int seconds))
                            {
                                int milliseconds = 0;
                                if (match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out int millisecondsRaw))
                                {
                                    if (match.Groups[3].Value.Length == 1) milliseconds = millisecondsRaw * 100;
                                    else if (match.Groups[3].Value.Length == 2) milliseconds = millisecondsRaw * 10;
                                    else milliseconds = millisecondsRaw;
                                }
                                var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                                lrcLines.Add(new LrcLine(timestamp, text));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Error parsing LRC file {filePath}", ex);
            }

            lrcLines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return lrcLines;
        }
    }
}
