using System.Text;
using Isopoh.Cryptography.Argon2;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Sync <see cref="TendrilSettings.Auth"/> from env (<c>TENDRIL_AUTH_*</c>, <c>BasicAuth__*</c>): plaintext is Argon2-hashed;
/// values starting with <c>$argon2</c> are stored as-is; caller writes <c>config.yaml</c> after sync.
/// </summary>
public static class AuthEnvironmentBootstrapper
{
    public const string EnvPassword = "TENDRIL_AUTH_PASSWORD";
    public const string EnvHashSecret = "TENDRIL_AUTH_HASH_SECRET";
    public const string EnvUsername = "TENDRIL_AUTH_USERNAME";
    public const string EnvBasicAuthUsers = "BasicAuth__Users";
    public const string EnvBasicAuthHashSecret = "BasicAuth__HashSecret";
    public const string EnvBasicAuthJwtSecret = "BasicAuth__JwtSecret";

    public static bool TrySyncFromEnvironment(TendrilSettings settings, ILogger logger)
    {
        var username = Environment.GetEnvironmentVariable(EnvUsername)?.Trim();
        var passwordMaterial = Environment.GetEnvironmentVariable(EnvPassword)?.Trim();
        if (TryParseBasicAuthUsers(out var basicUser, out var basicPassword))
        {
            if (string.IsNullOrEmpty(passwordMaterial))
                passwordMaterial = basicPassword;
            if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(basicUser))
                username = basicUser;
        }

        if (string.IsNullOrEmpty(passwordMaterial))
            return false;

        var secret = ResolveHashSecret(settings, logger);

        if (LooksLikePhcArgon2Hash(passwordMaterial))
        {
            if (PrehashedAuthAlreadyMatches(settings, username, passwordMaterial, secret))
                return false;

            logger.LogInformation(
                "Applying pre-hashed session password from {Env} (will be persisted when config is saved).",
                PasswordSourceEnvForLog());
            settings.Auth = new AuthConfig
            {
                Password = passwordMaterial,
                HashSecret = secret,
                Username = string.IsNullOrEmpty(username) ? null : username
            };
            return true;
        }

        if (settings.Auth != null && AuthPasswordHelper.StoredHashMatchesPlaintext(settings.Auth, passwordMaterial))
            return false;

        logger.LogInformation(
            "Applying session password from {Env} (hash will be persisted when config is saved).",
            PasswordSourceEnvForLog());

        settings.Auth = new AuthConfig
        {
            Password = AuthPasswordHelper.HashPlaintext(passwordMaterial, secret),
            HashSecret = secret,
            Username = string.IsNullOrEmpty(username) ? null : username
        };
        return true;
    }

    private static string PasswordSourceEnvForLog() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvPassword)?.Trim())
            ? EnvPassword
            : EnvBasicAuthUsers;

    private static bool LooksLikePhcArgon2Hash(string value) =>
        value.StartsWith("$argon2", StringComparison.OrdinalIgnoreCase);

    private static bool PrehashedAuthAlreadyMatches(
        TendrilSettings settings,
        string? envUsername,
        string phcHash,
        string hashSecret)
    {
        if (settings.Auth == null) return false;
        if (!string.Equals(settings.Auth.Password, phcHash, StringComparison.Ordinal)) return false;
        if (!string.Equals(settings.Auth.HashSecret, hashSecret, StringComparison.Ordinal)) return false;

        var u = envUsername ?? "";
        var cfg = settings.Auth.Username ?? "";
        if (string.IsNullOrEmpty(u) && string.IsNullOrEmpty(cfg)) return true;
        return string.Equals(u, cfg, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBasicAuthUsers(out string? username, out string password)
    {
        username = null;
        password = "";

        var raw = Environment.GetEnvironmentVariable(EnvBasicAuthUsers)?.Trim();
        if (string.IsNullOrEmpty(raw))
            return false;

        var i = raw.IndexOf(':');
        if (i < 0)
        {
            password = raw;
            return true;
        }

        if (i == 0)
        {
            password = raw[(i + 1)..].Trim();
            return true;
        }

        username = raw[..i].Trim();
        password = raw[(i + 1)..].Trim();
        return !string.IsNullOrEmpty(password);
    }

    private static string ResolveHashSecret(TendrilSettings settings, ILogger logger)
    {
        foreach (var (envKey, value) in new (string Key, string? Value)[]
                 {
                     (EnvHashSecret, Environment.GetEnvironmentVariable(EnvHashSecret)?.Trim()),
                     (EnvBasicAuthHashSecret, Environment.GetEnvironmentVariable(EnvBasicAuthHashSecret)?.Trim()),
                     (EnvBasicAuthJwtSecret, Environment.GetEnvironmentVariable(EnvBasicAuthJwtSecret)?.Trim()),
                 })
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (TryUseBase64Secret(envKey, value, logger, out var ok))
                return ok;
        }

        if (!string.IsNullOrEmpty(settings.Auth?.HashSecret))
            return settings.Auth.HashSecret;

        return AuthPasswordHelper.GenerateSecret();
    }

    private static bool TryUseBase64Secret(string envKey, string value, ILogger logger, out string secret)
    {
        secret = value;
        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            logger.LogWarning("{EnvKey} must be a base64 string; ignoring.", envKey);
            return false;
        }
    }
}

/// <summary>Argon2 for session auth (same as Security UI / <c>tendril hash-password</c>).</summary>
internal static class AuthPasswordHelper
{
    internal static string GenerateSecret()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    internal static string HashPlaintext(string plaintext, string hashSecretBase64)
    {
        var secretBytes = Convert.FromBase64String(hashSecretBase64);
        var salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        return Argon2.Hash(new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 65536,
            Lanes = 1,
            Threads = 1,
            Password = Encoding.UTF8.GetBytes(plaintext),
            Salt = salt,
            Secret = secretBytes,
            HashLength = 32
        });
    }

    internal static bool StoredHashMatchesPlaintext(AuthConfig auth, string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return false;
        try
        {
            var secretBytes = Convert.FromBase64String(auth.HashSecret);
            return Argon2.Verify(auth.Password, new Argon2Config
            {
                Password = Encoding.UTF8.GetBytes(plaintext),
                Secret = secretBytes,
            });
        }
        catch
        {
            return false;
        }
    }
}
