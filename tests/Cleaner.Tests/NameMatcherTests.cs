using Cleaner.Modules.Uninstall;
using Xunit;

namespace Cleaner.Tests;

public sealed class NameMatcherTests
{
    [Fact]
    public void Tokens_drop_parentheses_and_architecture() =>
        Assert.Equal(new[] { "notepad" }, NameMatcher.Tokens("Notepad++ (64-bit x64)"));

    [Fact]
    public void Tokens_drop_version_strings() =>
        Assert.Equal(new[] { "7", "zip" }, NameMatcher.Tokens("7-Zip 23.01 (x64 edition)"));

    [Fact]
    public void Tokens_drop_v_versions_and_symbols() =>
        Assert.Equal(new[] { "foo", "tool" }, NameMatcher.Tokens("Foo™ Tool v2 32-bit"));

    [Fact]
    public void Significant_tokens_skip_short_and_stop_words() =>
        Assert.Equal(new[] { "mozilla" }, NameMatcher.SignificantTokens("Mozilla Corporation"));

    [Fact]
    public void Normalize_joins_with_single_space() =>
        Assert.Equal("mozilla firefox", NameMatcher.Normalize("Mozilla  Firefox (x64 en-US)"));

    [Fact]
    public void Short_single_token_does_not_match_substring() =>
        Assert.Equal(MatchKind.None, NameMatcher.Match(folderName: "GitHub Desktop", programName: "Git", publisher: null));

    [Fact]
    public void Same_name_is_exact() =>
        Assert.Equal(MatchKind.Exact, NameMatcher.Match(folderName: "Git", programName: "Git", publisher: null));

    [Fact]
    public void Version_and_parentheses_are_ignored_for_exact() =>
        Assert.Equal(MatchKind.Exact, NameMatcher.Match(folderName: "7-Zip", programName: "7-Zip 23.01 (x64 edition)", publisher: null));

    [Fact]
    public void Joined_words_are_not_equal_to_separate_words() =>
        Assert.Equal(MatchKind.None, NameMatcher.Match(folderName: "Foobar", programName: "Foo Bar", publisher: null));

    [Fact]
    public void Folder_missing_significant_program_token_does_not_match() =>
        Assert.Equal(MatchKind.None, NameMatcher.Match(folderName: "Firefox", programName: "Mozilla Firefox", publisher: null));

    [Fact]
    public void Long_single_token_matches_inside_folder_words() =>
        Assert.Equal(MatchKind.Tokens, NameMatcher.Match(folderName: "Mozilla Firefox", programName: "Firefox", publisher: null));

    [Fact]
    public void All_program_tokens_in_folder_is_tokens_match() =>
        Assert.Equal(MatchKind.Tokens, NameMatcher.Match(folderName: "Foo Bar Tools", programName: "Foo Bar", publisher: null));

    [Fact]
    public void Publisher_without_company_suffix_matches() =>
        Assert.Equal(MatchKind.Publisher, NameMatcher.Match(folderName: "Mozilla", programName: "Mozilla Firefox", publisher: "Mozilla Corporation"));

    [Fact]
    public void Publisher_matches_folder() =>
        Assert.Equal(MatchKind.Publisher, NameMatcher.Match(folderName: "Mozilla", programName: "Firefox", publisher: "Mozilla"));

    [Fact]
    public void Empty_publisher_never_matches() =>
        Assert.Equal(MatchKind.None, NameMatcher.Match(folderName: "Mozilla", programName: "Firefox", publisher: ""));

    [Fact]
    public void Denied_folder_never_matches() =>
        Assert.Equal(MatchKind.None, NameMatcher.Match(folderName: "Windows", programName: "Windows", publisher: null));

    [Fact]
    public void Common_files_is_denied()
    {
        Assert.True(NameMatcher.IsDeniedFolderName("Common Files"));
        Assert.True(NameMatcher.IsDeniedFolderName("nvidia"));
        Assert.False(NameMatcher.IsDeniedFolderName("Mozilla"));
    }
}
