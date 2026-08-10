using CarDealer.Application.Abstractions;
using CarDealer.Infrastructure.Services;

namespace CarDealer.UnitTests;

public sealed class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new IdentityPasswordHasher();

    [Fact]
    public void Hash_is_not_reversible_and_not_the_password()
    {
        const string password = "Dev_Passw0rd!";

        var hash = _hasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
        Assert.True(hash.Length > 40);
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        // Per-hash salt. Identical hashes would leak which accounts share a password.
        const string password = "Dev_Passw0rd!";

        Assert.NotEqual(_hasher.Hash(password), _hasher.Hash(password));
    }

    [Fact]
    public void Correct_password_verifies()
    {
        var hash = _hasher.Hash("Dev_Passw0rd!");

        Assert.Equal(PasswordVerificationOutcome.Succeeded, _hasher.Verify(hash, "Dev_Passw0rd!"));
    }

    [Theory]
    [InlineData("dev_passw0rd!")]
    [InlineData("Dev_Passw0rd")]
    [InlineData("")]
    [InlineData(" Dev_Passw0rd!")]
    public void Wrong_password_fails(string attempt)
    {
        var hash = _hasher.Hash("Dev_Passw0rd!");

        Assert.Equal(PasswordVerificationOutcome.Failed, _hasher.Verify(hash, attempt));
    }

    [Fact]
    public void Malformed_stored_hash_fails_rather_than_throwing()
    {
        // A corrupted hash column must read as "wrong password", not as a 500 that tells an
        // attacker they found something interesting.
        Assert.Equal(PasswordVerificationOutcome.Failed, _hasher.Verify("not-a-hash", "anything"));
    }
}
