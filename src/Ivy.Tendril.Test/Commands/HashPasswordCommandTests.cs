using System.Text;
using Isopoh.Cryptography.Argon2;
using Ivy.Tendril.Commands;

namespace Ivy.Tendril.Test.Commands;

public class HashPasswordCommandTests
{
    [Fact]
    public void Handle_NonMatchingCommand_ReturnsNegativeOne()
    {
        var result = HashPasswordCommand.Handle(["other-command", "password"]);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_TooFewArgs_ReturnsNegativeOne()
    {
        var result = HashPasswordCommand.Handle(["hash-password"]);
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_ValidArgs_ReturnsZero()
    {
        var result = HashPasswordCommand.Handle(["hash-password", "test123"]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Handle_WithExistingSecret_ReturnsZero()
    {
        var secret = Convert.ToBase64String(new byte[32]);
        var result = HashPasswordCommand.Handle(["hash-password", "test123", secret]);
        Assert.Equal(0, result);
    }

    [Fact]
    public void GenerateSecret_ProducesValidBase64()
    {
        var secret = HashPasswordCommand.GenerateSecret();
        var bytes = Convert.FromBase64String(secret);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void Handle_ProducesVerifiableHash()
    {
        var password = "my-test-password";
        var secret = HashPasswordCommand.GenerateSecret();
        var secretBytes = Convert.FromBase64String(secret);

        var salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        var hash = Argon2.Hash(new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 65536,
            Lanes = 1,
            Threads = 1,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = salt,
            Secret = secretBytes,
            HashLength = 32
        });

        var verified = Argon2.Verify(hash, new Argon2Config
        {
            Password = Encoding.UTF8.GetBytes(password),
            Secret = secretBytes
        });

        Assert.True(verified);
    }
}