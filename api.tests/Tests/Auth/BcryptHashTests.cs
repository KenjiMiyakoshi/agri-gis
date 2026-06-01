using AgriGis.Api.Auth;
using Xunit;

namespace AgriGis.Api.Tests.Tests.Auth;

// A508: BCrypt 往復確認。Hash → Verify(正解) true / Verify(誤) false / SaltParseException 安全
public sealed class BcryptHashTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashThenVerify_CorrectPassword_True()
    {
        var hash = _hasher.Hash("Pa$$word1234");
        Assert.True(_hasher.Verify("Pa$$word1234", hash));
    }

    [Fact]
    public void HashThenVerify_WrongPassword_False()
    {
        var hash = _hasher.Hash("Pa$$word1234");
        Assert.False(_hasher.Verify("OtherPa$$word", hash));
    }

    [Fact]
    public void Verify_MalformedHash_False()
    {
        Assert.False(_hasher.Verify("anything", "not-a-bcrypt-hash"));
    }

    [Fact]
    public void Verify_EmptyInputs_False()
    {
        Assert.False(_hasher.Verify("", ""));
    }

    [Fact]
    public void Hash_TwoCalls_ProduceDifferentSalts()
    {
        var h1 = _hasher.Hash("same");
        var h2 = _hasher.Hash("same");
        Assert.NotEqual(h1, h2);
        Assert.True(_hasher.Verify("same", h1));
        Assert.True(_hasher.Verify("same", h2));
    }
}
