using System.Security.Cryptography;

namespace backend.Security
{
    /// <summary>
    /// Hash de senha PBKDF2-HMAC-SHA512 com sal aleatorio. Formato: pbkdf2-sha512$iteracoes$salt$hash (base64).
    /// As iteracoes ficam no proprio hash; se estiverem abaixo da configuracao atual, o hash e refeito no login.
    /// </summary>
    public class Pbkdf2PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha512";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        public const int MinIterations = 210_000;
        private readonly int _iterations;
        private readonly Lazy<string> _dummy;

        public Pbkdf2PasswordHasher(IConfiguration config, IHostEnvironment env)
        {
            // O piso de 210k so e relaxado no ambiente de testes (acelera a suite)
            var floor = env.IsEnvironment("Testing") ? 1000 : MinIterations;
            _iterations = Math.Max(floor, config.GetValue("Auth:Pbkdf2Iterations", MinIterations));
            _dummy = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))));
        }

        public string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA512, HashSize);
            return $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public bool Verify(string storedHash, string password)
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1000)
                return false;
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool NeedsRehash(string storedHash)
        {
            var parts = storedHash.Split('$');
            return parts.Length != 4 || !int.TryParse(parts[1], out var it) || it < _iterations;
        }

        /// <summary>Gasta o mesmo tempo de uma verificacao real (usuario inexistente/inativo/pendente: anti-timing).</summary>
        public void VerifyDummy(string password) => Verify(_dummy.Value, password);
    }
}
