using Cleaner.Ai;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa <see cref="AnthropicBackend"/>-luokkaa ilman verkkoa: vain avaimen tunnistus ympäristömuuttujasta.
/// Kuuluu kokoelmaan <see cref="TestLanguage.Collection"/>, koska testit muuttavat prosessin ympäristöä.
/// </summary>
[Collection(TestLanguage.Collection)]
public sealed class AnthropicBackendTests
{
    private const string Variable = "ANTHROPIC_API_KEY";

    private static void WithKey(string? value, Action test)
    {
        var original = Environment.GetEnvironmentVariable(Variable);
        try
        {
            Environment.SetEnvironmentVariable(Variable, value);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
        }
    }

    [Fact]
    public void IsConfigured_is_false_without_key()
    {
        WithKey(null, () => Assert.False(AnthropicBackend.IsConfigured));
        WithKey("   ", () => Assert.False(AnthropicBackend.IsConfigured));
    }

    [Fact]
    public void IsConfigured_is_true_with_key()
    {
        WithKey("sk-ant-test-not-a-real-key", () => Assert.True(AnthropicBackend.IsConfigured));
    }

    [Fact]
    public void Constructor_without_key_throws_missing_key()
    {
        WithKey(null, () =>
        {
            var ex = Assert.Throws<AiException>(() => new AnthropicBackend());
            Assert.Equal(AiErrorKind.MissingKey, ex.Kind);
        });
    }

    [Fact]
    public void Constructor_with_key_does_not_call_the_network()
    {
        WithKey("sk-ant-test-not-a-real-key", () => Assert.NotNull(new AnthropicBackend()));
    }
}
