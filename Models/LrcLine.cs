using System;

namespace UltimateKtv.Models
{
    public class LrcLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; }

        public LrcLine(TimeSpan timestamp, string text)
        {
            Timestamp = timestamp;
            Text = text;
        }
    }
}
