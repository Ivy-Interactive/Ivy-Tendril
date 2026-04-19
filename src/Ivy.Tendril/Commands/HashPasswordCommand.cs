using System.Text;
using Isopoh.Cryptography.Argon2;
using Spectre.Console;

namespace Ivy.Tendril.Commands;

public static class HashPasswordCommand
{
    public static int Handle(string[] args)
    {
        if (args.Length < 2 || args[0] != "hash-password")
            return -1;

        var password = args[1];
        var secret = args.Length > 2 ? args[2] : GenerateSecret();

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

        AnsiConsole.MarkupLine("[bold]Password hash:[/]");
        AnsiConsole.WriteLine(hash);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Hash secret (if newly generated):[/]");
        AnsiConsole.WriteLine(secret);

        return 0;
    }

    internal static string GenerateSecret()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
