using Thrum.Core.Actions;
using Thrum.Core.Models;
using Xunit;

namespace Thrum.Tests;

public sealed class ActionExecutorTests
{
    private sealed class MockActionRunner : IActionRunner
    {
        public int ExecutedCount { get; private set; }

        public void SendSingleKey(string key) => ExecutedCount++;
        public void SendKeyShortcut(string key, bool ctrl, bool alt, bool shift, bool win) => ExecutedCount++;
        public void SendMediaPlayPause() => ExecutedCount++;
        public void SendVolumeUp() => ExecutedCount++;
        public void SendVolumeDown() => ExecutedCount++;
        public void SendVolumeMute() => ExecutedCount++;
        public void LaunchApp(string appPath) => ExecutedCount++;
        public void OpenUrl(string url) => ExecutedCount++;
    }

    [Fact]
    public void ActionCooldown_SuppressesRapidDuplicateActions()
    {
        var runner = new MockActionRunner();
        var executor = new ActionExecutor(runner) { GlobalDebounceMs = 50 };

        var zone = new Zone
        {
            Id = "zone-vol",
            Name = "Volume Zone",
            Action = new ZoneAction
            {
                Type = ActionType.VolumeUp,
                CooldownMs = 400
            }
        };

        // First execution succeeds
        bool first = executor.TryExecute(zone);
        Assert.True(first);
        Assert.Equal(1, runner.ExecutedCount);

        // Immediate second execution fails due to cooldown
        bool second = executor.TryExecute(zone);
        Assert.False(second);
        Assert.Equal(1, runner.ExecutedCount);

        // Wait past cooldown (450ms)
        Thread.Sleep(450);

        bool third = executor.TryExecute(zone);
        Assert.True(third);
        Assert.Equal(2, runner.ExecutedCount);
    }

    [Fact]
    public void GlobalDebounce_PreventsRapidCrossZoneFiring()
    {
        var runner = new MockActionRunner();
        var executor = new ActionExecutor(runner) { GlobalDebounceMs = 200 };

        var zone1 = new Zone
        {
            Id = "zone-1",
            Action = new ZoneAction { Type = ActionType.MediaPlayPause, CooldownMs = 50 }
        };

        var zone2 = new Zone
        {
            Id = "zone-2",
            Action = new ZoneAction { Type = ActionType.VolumeDown, CooldownMs = 50 }
        };

        // First zone executes
        Assert.True(executor.TryExecute(zone1));
        Assert.Equal(1, runner.ExecutedCount);

        // Immediately attempt second zone (violates 200ms global debounce)
        Assert.False(executor.TryExecute(zone2));
        Assert.Equal(1, runner.ExecutedCount);
    }

    [Fact]
    public void IgnoreZone_NeverExecutesAction()
    {
        var runner = new MockActionRunner();
        var executor = new ActionExecutor(runner);

        var ignoreZone = new Zone
        {
            Id = "zone-ignore",
            IsIgnore = true,
            Action = new ZoneAction { Type = ActionType.MediaPlayPause }
        };

        Assert.False(executor.TryExecute(ignoreZone));
        Assert.Equal(0, runner.ExecutedCount);
    }
}
