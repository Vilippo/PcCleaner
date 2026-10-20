using Cleaner.Ai;
using Xunit;

namespace Cleaner.Tests;

/// <summary>Testaa käyttäjänimen piilotusta mallille lähetettävästä tekstistä.</summary>
public sealed class PathSanitizerTests
{
    private static readonly PathSanitizer Sanitizer = new(@"C:\Users\ville", "ville");

    [Fact]
    public void Profile_in_the_middle_of_text_is_replaced()
    {
        Assert.Equal(@"Cache in %USERPROFILE%\AppData\Local\Temp (old)",
            Sanitizer.Sanitize(@"Cache in C:\Users\ville\AppData\Local\Temp (old)"));
    }

    [Fact]
    public void Profile_at_end_of_text_is_replaced()
    {
        Assert.Equal("%USERPROFILE%", Sanitizer.Sanitize(@"C:\Users\ville"));
        Assert.Equal("Home \"%USERPROFILE%\" (x)", Sanitizer.Sanitize("Home \"C:\\Users\\ville\" (x)"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal(@"%USERPROFILE%\Downloads", Sanitizer.Sanitize(@"c:\users\VILLE\Downloads"));
    }

    [Fact]
    public void User_name_on_another_drive_is_replaced()
    {
        Assert.Equal(@"D:\Users\%USERNAME%\Projects", Sanitizer.Sanitize(@"D:\Users\ville\Projects"));
        Assert.Equal(@"D:\Users\%USERNAME%\x", Sanitizer.Sanitize(@"D:\USERS\Ville\x"));
    }

    [Fact]
    public void Other_users_and_shared_prefix_are_not_changed()
    {
        Assert.Equal(@"C:\Users\anna\AppData", Sanitizer.Sanitize(@"C:\Users\anna\AppData"));
        Assert.Equal(@"C:\Users\villeneuve\AppData", Sanitizer.Sanitize(@"C:\Users\villeneuve\AppData"));
        Assert.Equal(@"C:\Users\ville.old\x", Sanitizer.Sanitize(@"C:\Users\ville.old\x"));
        Assert.Equal(@"C:\Users\ville_2\x", Sanitizer.Sanitize(@"C:\Users\ville_2\x"));
        Assert.Equal(@"C:\Users\ville-x\x", Sanitizer.Sanitize(@"C:\Users\ville-x\x"));
    }

    [Fact]
    public void Empty_user_name_skips_that_replacement()
    {
        var sanitizer = new PathSanitizer(@"C:\Users\ville", "");
        Assert.Equal(@"%USERPROFILE%\x and D:\Users\ville\y", sanitizer.Sanitize(@"C:\Users\ville\x and D:\Users\ville\y"));
    }

    [Fact]
    public void Empty_profile_skips_that_replacement()
    {
        var sanitizer = new PathSanitizer("", "ville");
        Assert.Equal(@"C:\Users\%USERNAME%\x", sanitizer.Sanitize(@"C:\Users\ville\x"));
    }

    [Fact]
    public void Text_without_paths_is_unchanged()
    {
        Assert.Equal("ville likes C:\\Temp", Sanitizer.Sanitize("ville likes C:\\Temp"));
        Assert.Equal("", Sanitizer.Sanitize(""));
    }
}
