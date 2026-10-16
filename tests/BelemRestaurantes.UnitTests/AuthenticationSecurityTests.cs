using BelemRestaurantes.Infrastructure;
using BelemRestaurantes.Infrastructure.MySql;

namespace BelemRestaurantes.UnitTests;

public sealed class AuthenticationSecurityTests
{
    [Fact]
    public void Password_hashes_are_salted_and_verify_only_correct_password()
    {
        var verifier = new Pbkdf2PasswordVerifier();
        var hash = verifier.Hash("Example!password-123");
        Assert.NotEqual(hash, verifier.Hash("Example!password-123"));
        Assert.True(verifier.Verify("Example!password-123", hash));
        Assert.False(verifier.Verify("other", hash));
    }

    [Theory]
    [InlineData("1.AA==.AA==")]
    [InlineData("2147483647.AA==.AA==")]
    [InlineData("600000.invalid.invalid")]
    public void Malformed_or_unbounded_password_hashes_are_rejected(string hash) =>
        Assert.False(new Pbkdf2PasswordVerifier().Verify("password", hash));

    [Fact]
    public void Totp_matches_rfc6238_vector_and_expires()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var totp = new TotpProvider();
        var instant = DateTimeOffset.FromUnixTimeSeconds(59);
        Assert.Equal("287082", totp.GenerateFor(instant, secret));
        Assert.True(totp.Validate("287082", secret, instant));
        Assert.False(totp.Validate("287082", secret, instant.AddMinutes(3)));
    }

    [Theory]
    [InlineData("Server=localhost;Database=other;SslMode=Required")]
    [InlineData("Server=remote.example;Database=belrestaurante;SslMode=Required")]
    [InlineData("Server=localhost;Database=belrestaurante;SslMode=None")]
    public void Unsafe_database_configuration_is_rejected(string connection) =>
        Assert.Throws<InvalidOperationException>(() => new MySqlConnectionFactory(connection));
}
