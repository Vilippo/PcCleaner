using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Tests;

public class PathGuardTests
{
    [Fact]
    public void Rejects_drive_root()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.False(PathGuard.IsSafeToDelete(root, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Rejects_user_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return; // CI-ympäristössä voi puuttua
        Assert.False(PathGuard.IsSafeToDelete(profile, out _));
    }

    [Fact]
    public void Rejects_parent_of_protected_folder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return;
        var parent = Path.GetDirectoryName(profile);
        if (string.IsNullOrEmpty(parent)) return;
        Assert.False(PathGuard.IsSafeToDelete(parent, out _));
    }

    [Fact]
    public void Accepts_file_inside_temp()
    {
        var file = Path.Combine(Path.GetTempPath(), "pccleaner-test", "x.tmp");
        Assert.True(PathGuard.IsSafeToDelete(file, out _));
    }

    [Fact]
    public void Rejects_empty_path()
    {
        Assert.False(PathGuard.IsSafeToDelete("", out _));
    }
}
