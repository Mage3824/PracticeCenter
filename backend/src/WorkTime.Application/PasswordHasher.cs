// BR34: Password hashing using Argon2id (OWASP recommended).
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

public static class PasswordHasher
{
    // OWASP minimum recommended parameters (as of 2024/2026).
    // These can be tuned to target ~0.5s on your hardware.
    private const int SaltSize = 16;          // 16 bytes (128 bits)
    private const int HashSize = 32;          // 32 bytes (256 bits)
    private const int MemorySize = 19456;     // 19 MiB (in KiB)
    private const int Iterations = 2;         // t=2
    private const int Parallelism = 1;        // p=1

    // Границы для сверки чужой PHC-строки: ограничивают память/CPU и размер соли и хеша.
    private const int MaxMemoryKiB = 65_536;  // 64 MiB
    private const int MaxIterations = 10;
    private const int MaxParallelism = 16;
    private const int MinSaltBytes = 8;
    private const int MaxSaltBytes = 64;
    private const int MinHashBytes = 16;
    private const int MaxHashBytes = 64;

    public static string TryHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);

        // PHC string format: $argon2id$v=19$m=...,t=...,p=...$salt$hash
        // This format embeds all parameters needed for verification.
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";
    }

    public static bool TryVerify(string password, string storedHash)
    {
        try
        {
            // Parse the PHC string to extract parameters and salt.
            var parts = storedHash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
            {
                return false;
            }

            // Parse parameters: m=...,t=...,p=...
            var paramParts = parts[3].Split(',');
            var parameters = new Dictionary<string, int>();
            foreach (var part in paramParts)
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1], out var value))
                {
                    parameters[kv[0]] = value;
                }
            }

            if (!parameters.TryGetValue("m", out var memory) ||
                !parameters.TryGetValue("t", out var iterations) ||
                !parameters.TryGetValue("p", out var parallelism))
            {
                return false;
            }

            // Верхние границы параметров: сверка не должна выделять память и считать по
            // чужой враждебно большой строке (DoS). Снаружи диапазона — хеш не совпадает.
            if (memory < 1 || memory > MaxMemoryKiB ||
                iterations < 1 || iterations > MaxIterations ||
                parallelism < 1 || parallelism > MaxParallelism)
            {
                return false;
            }

            // Decode salt (Base64 without padding).
            var saltBase64 = parts[4];
            var salt = Convert.FromBase64String(PadBase64(saltBase64));

            // Decode expected hash.
            var expectedHashBase64 = parts[5];
            var expectedHash = Convert.FromBase64String(PadBase64(expectedHashBase64));

            if (salt.Length < MinSaltBytes || salt.Length > MaxSaltBytes ||
                expectedHash.Length < MinHashBytes || expectedHash.Length > MaxHashBytes)
            {
                return false;
            }

            // Compute hash with the same parameters.
            var actualHash = HashPassword(password, salt, memory, iterations, parallelism);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (Exception)
        {
            // Any malformed or legacy hash should not match.
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt, int memorySize = MemorySize, int iterations = Iterations, int parallelism = Parallelism)
    {
        // Публичные Try* не должны бросать: null трактуется как пустая строка, а не как ArgumentNullException.
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password ?? string.Empty))
        {
            Salt = salt,
            MemorySize = memorySize,      // in KiB
            Iterations = iterations,      // time cost (t)
            DegreeOfParallelism = parallelism // p
        };

        return argon2.GetBytes(HashSize);
    }

    // Helper to add padding back to Base64 string (needed for Convert.FromBase64String).
    private static string PadBase64(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: return base64 + "==";
            case 3: return base64 + "=";
            default: return base64;
        }
    }
}