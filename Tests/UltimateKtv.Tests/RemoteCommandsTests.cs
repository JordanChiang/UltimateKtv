using Xunit;

namespace UltimateKtv.Tests
{
    public class RemoteCommandsTests
    {
        private sealed class FakeRemote : IRemoteControl
        {
            public readonly List<string> Calls = new();
            public bool Locked;
            public Exception? Throw;

            private void Call(string name)
            {
                Calls.Add(name);
                if (Throw != null) throw Throw;
            }

            public void Skip() => Call("Skip");
            public void SwitchToVocal() => Call("SwitchToVocal");
            public void SwitchToMusic() => Call("SwitchToMusic");
            public void MoveToFirst(Guid id) => Call($"MoveToFirst {id}");
            public void MoveToNext(Guid id) => Call($"MoveToNext {id}");
            public void Remove(Guid id) => Call($"Remove {id}");
            public void VolumeUp() => Call("VolumeUp");
            public void VolumeDown() => Call("VolumeDown");
            public bool ToggleVolumeLock()
            {
                Call("ToggleVolumeLock");
                Locked = !Locked;
                return Locked;
            }
            public void PitchUp() => Call("PitchUp");
            public void PitchDown() => Call("PitchDown");
            public void PitchReset() => Call("PitchReset");
            public void Quit(bool shutdownWindows) => Call($"Quit {shutdownWindows}");
        }

        private static string Text(string key) => $"<{key}>";

        private static string Run(string queryType, FakeRemote remote, string value = "")
        {
            Assert.True(RemoteCommands.TryRun(queryType, value, remote, Text, out var json));
            return json;
        }

        private static string Ok(string key) => $"{{\"success\":true,\"message\":\"<{key}>\"}}";

        [Theory]
        [InlineData("skipsong", "Skip", "Web.Msg.SkipSent")]
        [InlineData("vocal", "SwitchToVocal", "Web.Msg.Vocal")]
        [InlineData("music", "SwitchToMusic", "Web.Msg.Music")]
        [InlineData("volup", "VolumeUp", "Web.Msg.VolUp")]
        [InlineData("voldown", "VolumeDown", "Web.Msg.VolDown")]
        [InlineData("pitchup", "PitchUp", "Web.Msg.PitchUp")]
        [InlineData("pitchdown", "PitchDown", "Web.Msg.PitchDown")]
        [InlineData("pitchreset", "PitchReset", "Web.Msg.PitchReset")]
        [InlineData("quit", "Quit False", "Web.Msg.Quitting")]
        [InlineData("shutdownwin", "Quit True", "Web.Msg.ShuttingDown")]
        public void Command_calls_the_main_program_and_reports_success(string queryType, string call, string messageKey)
        {
            var remote = new FakeRemote();
            Assert.Equal(Ok(messageKey), Run(queryType, remote));
            Assert.Equal(new[] { call }, remote.Calls);
        }

        [Fact]
        public void Volume_lock_reports_the_new_state()
        {
            var remote = new FakeRemote();
            Assert.Equal(Ok("Web.Msg.VolLocked"), Run("vollock", remote));
            Assert.Equal(Ok("Web.Msg.VolUnlocked"), Run("vollock", remote));
        }

        [Theory]
        [InlineData("movetofirst", "MoveToFirst", "Web.Msg.MovedToFirst")]
        [InlineData("movetonext", "MoveToNext", "Web.Msg.MovedNext")]
        [InlineData("removefromlist", "Remove", "Web.Msg.Removed")]
        public void Waiting_list_commands_take_the_item_id(string queryType, string call, string messageKey)
        {
            var remote = new FakeRemote();
            var id = Guid.NewGuid();
            Assert.Equal(Ok(messageKey), Run(queryType, remote, id.ToString()));
            Assert.Equal(new[] { $"{call} {id}" }, remote.Calls);
        }

        [Theory]
        [InlineData("movetofirst", "xyz")]
        [InlineData("removefromlist", "")]
        public void Invalid_item_id_is_an_error_and_does_nothing(string queryType, string value)
        {
            var remote = new FakeRemote();
            Assert.Equal("[{\"Error\":\"Invalid ID\",\"Message\":\"Provided ID is not a valid GUID\"}]", Run(queryType, remote, value));
            Assert.Empty(remote.Calls);
        }

        [Theory]
        [InlineData("skipsong", "Skip Error")]
        [InlineData("vollock", "ToggleVolumeLock Error")]
        [InlineData("movetonext", "Error")]
        [InlineData("shutdownwin", "ShutdownWin Error")]
        public void Failure_in_the_main_program_is_reported_as_an_error(string queryType, string errorKey)
        {
            var remote = new FakeRemote { Throw = new InvalidOperationException("壞了") };
            Assert.Equal($"[{{\"Error\":\"{errorKey}\",\"Message\":\"壞了\"}}]", Run(queryType, remote, Guid.NewGuid().ToString()));
        }

        [Fact]
        public void Error_without_a_message_is_an_empty_array()
        {
            Assert.Equal("[]", RemoteJson.Error("Skip Error", ""));
        }

        [Fact]
        public void Queries_are_not_commands()
        {
            Assert.False(RemoteCommands.TryRun("searchsong", "", new FakeRemote(), Text, out _));
            Assert.False(RemoteCommands.TryRun("", "", new FakeRemote(), Text, out _));
        }

        [Fact]
        public void Lists_every_command()
        {
            Assert.Equal(
                new[] { "movetofirst", "movetonext", "music", "pitchdown", "pitchreset", "pitchup", "quit", "removefromlist", "shutdownwin", "skipsong", "vocal", "voldown", "vollock", "volup" },
                RemoteCommands.Names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }
    }
}
