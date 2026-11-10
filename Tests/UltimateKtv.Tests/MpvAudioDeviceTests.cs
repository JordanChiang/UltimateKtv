using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace UltimateKtv.Tests
{
    public class MpvAudioDeviceTests
    {
        private readonly ITestOutputHelper _output;

        public MpvAudioDeviceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string? MatchMpvAudioDevice(IntPtr ctx, string? targetDevice)
        {
            if (string.IsNullOrWhiteSpace(targetDevice) ||
                targetDevice.Equals("Default DirectSound Device", StringComparison.OrdinalIgnoreCase) ||
                targetDevice.Equals("Default WaveOut Device", StringComparison.OrdinalIgnoreCase))
            {
                return "auto";
            }

            string cleanTarget = targetDevice;
            if (cleanTarget.StartsWith("DirectSound: ", StringComparison.OrdinalIgnoreCase))
            {
                cleanTarget = cleanTarget.Substring("DirectSound: ".Length).Trim();
            }

            string? countStr = MpvNative.GetPropertyUtf8(ctx, "audio-device-list/count");
            if (!int.TryParse(countStr, out int count) || count <= 0) return null;

            // 1. Exact match by description or name
            for (int i = 0; i < count; i++)
            {
                string? name = MpvNative.GetPropertyUtf8(ctx, $"audio-device-list/{i}/name");
                string? desc = MpvNative.GetPropertyUtf8(ctx, $"audio-device-list/{i}/description");
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (string.Equals(desc, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, cleanTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            // 2. Contains match (either way)
            for (int i = 0; i < count; i++)
            {
                string? name = MpvNative.GetPropertyUtf8(ctx, $"audio-device-list/{i}/name");
                string? desc = MpvNative.GetPropertyUtf8(ctx, $"audio-device-list/{i}/description");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc)) continue;

                if (desc.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                    cleanTarget.Contains(desc, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }

        [Fact]
        public void TestMatchMpvAudioDevice()
        {
            Assert.True(MpvNative.EnsureLoaded());
            IntPtr ctx = MpvNative.Create!();
            try
            {
                MpvNative.Initialize!(ctx);

                var rDefault = MatchMpvAudioDevice(ctx, "Default DirectSound Device");
                _output.WriteLine($"Match 'Default DirectSound Device' -> '{rDefault}'");
                Assert.Equal("auto", rDefault);

                var rDsSpeaker = MatchMpvAudioDevice(ctx, "DirectSound: 喇叭 (High Definition Audio Device)");
                _output.WriteLine($"Match 'DirectSound: 喇叭...' -> '{rDsSpeaker}'");
                Assert.NotNull(rDsSpeaker);
                Assert.StartsWith("wasapi/", rDsSpeaker);

                var rSpeaker = MatchMpvAudioDevice(ctx, "喇叭 (High Definition Audio Device)");
                _output.WriteLine($"Match '喇叭...' -> '{rSpeaker}'");
                Assert.Equal(rDsSpeaker, rSpeaker);

                // Try setting the property
                int err = MpvNative.SetPropertyUtf8(ctx, "audio-device", rSpeaker!);
                _output.WriteLine($"SetProperty audio-device -> {err}");
                Assert.True(err >= 0);

                string? cur = MpvNative.GetPropertyUtf8(ctx, "audio-device");
                _output.WriteLine($"Read back audio-device: '{cur}'");
                Assert.Equal(rSpeaker, cur);
            }
            finally
            {
                MpvNative.TerminateDestroy!(ctx);
            }
        }
    }
}
