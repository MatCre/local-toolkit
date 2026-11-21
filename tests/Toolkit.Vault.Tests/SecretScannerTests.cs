namespace Toolkit.Vault.Tests;

public class SecretScannerTests
{
    // Fake credentials are assembled at runtime from pieces, so this file never contains a string
    // that looks like a real key. GitHub's secret scanning would otherwise flag or block the push.
    private static readonly string FakeOpenRouterKey = "sk-" + "or-v1-" + Hex(64, seed: 1);
    private static readonly string FakeAnthropicKey = "sk-" + "ant-api03-" + Base64(60, seed: 2).Replace('+', 'x').Replace('/', 'y');
    private static readonly string FakeGitHubToken = "gh" + "p_" + Alnum(36, seed: 3);
    private static readonly string FakeAwsKey = "AK" + "IA" + "QWERTYUIOP234567";
    private static readonly string FakeRandomToken = Base64(48, seed: 4);

    public static TheoryData<string, string> Secrets => new()
    {
        { FakeOpenRouterKey, "API key (sk-…)" },
        { $"key: {FakeAnthropicKey}", "API key (sk-…)" },
        { $"token={FakeGitHubToken}", "GitHub token" },
        { $"aws_access_key_id = {FakeAwsKey}", "AWS access key" },
        { "-----BEGIN " + "RSA PRIVATE KEY-----\nMIIEow...", "private key" },
        { "Server=db;User Id=sa;Password=Hunter2!;", "password in a connection string" },
        { $"Authorization: Bearer {FakeRandomToken}", "random-looking token" },
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void Finds_secrets(string text, string expectedKind)
    {
        var finding = Assert.Single(SecretScanner.Scan(text));

        Assert.Equal(expectedKind, finding.Kind);
    }

    [Fact]
    public void Never_reveals_the_whole_secret()
    {
        var finding = Assert.Single(SecretScanner.Scan(FakeOpenRouterKey));

        Assert.DoesNotContain(FakeOpenRouterKey, finding.Masked);
        Assert.StartsWith("sk-", finding.Masked);
        Assert.EndsWith(FakeOpenRouterKey[^3..], finding.Masked);
    }

    [Theory]
    [InlineData("Use AddResilienceHandler, then AddRetry with exponential back-off.")]
    [InlineData("services.AddToolkitAI(builder.Configuration).AddToolkitAISqlite();")]
    [InlineData("nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free")] // model IDs are long but readable
    [InlineData("commit 9a2ba6ae4f1c7d3b5e8f0a2c4d6e8f0a1b3c5d7e")]    // git hashes are hex: low entropy
    [InlineData("id 3f2a9c1e-5b7d-4e8a-9c21-7d4e5f6a8b90")]             // GUIDs split at their hyphens
    [InlineData("ToolkitRoutingChatClientTestsWithAVeryLongName")]     // letters only
    [InlineData("Data Source=data/vault.db;Mode=ReadOnly")]           // connection string, no password
    [InlineData("dotnet user-secrets set ToolkitAI:Providers:openrouter:ApiKey \"YOUR-KEY\"")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions")]
    public void Leaves_ordinary_notes_alone(string text) =>
        Assert.Empty(SecretScanner.Scan(text));

    private static string Hex(int length, int seed) =>
        string.Concat(Bytes(length, seed).Select(b => (b % 16).ToString("x")));

    private static string Alnum(int length, int seed)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return string.Concat(Bytes(length, seed).Select(b => chars[b % chars.Length]));
    }

    private static string Base64(int length, int seed) => Convert.ToBase64String(Bytes(length, seed))[..length];

    private static byte[] Bytes(int count, int seed)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}
