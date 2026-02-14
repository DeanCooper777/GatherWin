using GatherWin.Services;

namespace GatherWin.Tests.Services;

public class CredentialProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var original = "sk-ant-api03-test-key-12345";

        var encrypted = CredentialProtector.Protect(original);
        Assert.NotEqual(original, encrypted);

        var decrypted = CredentialProtector.Unprotect(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Protect_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CredentialProtector.Protect(string.Empty));
        Assert.Equal(string.Empty, CredentialProtector.Protect(null!));
    }

    [Fact]
    public void Unprotect_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CredentialProtector.Unprotect(string.Empty));
        Assert.Equal(string.Empty, CredentialProtector.Unprotect(null!));
    }

    [Fact]
    public void Unprotect_InvalidData_Throws()
    {
        // Random Base64 that isn't valid DPAPI data should throw
        var invalidData = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        Assert.ThrowsAny<Exception>(() => CredentialProtector.Unprotect(invalidData));
    }

    [Fact]
    public void IsPlaintext_DetectsApiKeys()
    {
        Assert.True(CredentialProtector.IsPlaintext("sk-ant-api03-something"));
        Assert.False(CredentialProtector.IsPlaintext("AQAAANCMnd8BF..."));
        Assert.False(CredentialProtector.IsPlaintext(""));
    }
}
