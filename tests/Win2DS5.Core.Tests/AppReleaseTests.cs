using System.Text.Json;
using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class AppReleaseTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0", "1.0.1", false)]
    [InlineData("nope", "1.0.0", false)]
    [InlineData("1.0.0+local", "1.0.0", false)]
    public void IsNewer_ComparesPublishedBuilds(string remote, string local, bool newer)
    {
        Assert.Equal(newer, AppRelease.IsNewer(remote, local));
    }

    [Fact]
    public void InstallMarker_RoundTripsTheShortcutPaths()
    {
        var marker = new InstallMarker
        {
            DesktopShortcut = @"C:\Users\A\Desktop\Win2DS5.lnk",
            StartMenuShortcut = @"C:\Users\A\Start Menu\Programs\Win2DS5.lnk",
        };
        var json = JsonSerializer.Serialize(marker, ProfileStore.Options);
        var back = JsonSerializer.Deserialize<InstallMarker>(json, ProfileStore.Options);
        Assert.NotNull(back);
        Assert.True(back.Installed);
        Assert.Equal(marker.DesktopShortcut, back.DesktopShortcut);
        Assert.Equal(marker.StartMenuShortcut, back.StartMenuShortcut);
    }
}
