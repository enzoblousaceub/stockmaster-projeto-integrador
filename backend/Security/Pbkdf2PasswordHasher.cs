using System.Security.Cryptography;

namespace backend.Security
{
    /// <summary>Muitas verificacoes de senha simultaneas: a API responde 503 com Retry-After.</summary>
    public class HasherBusyException : Exception { }

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
        private readonly SemaphoreSlim _gate;
        private readonly TimeSpan _wait;
        private readonly int _maxQueue;
        private int _pending;

        public Pbkdf2PasswordHasher(IConfiguration config, IHostEnvironment env)
        {
            // O piso de 210k so e relaxado no ambiente de testes (acelera a suite)
            var floor = env.IsEnvironment("Testing") ? 1000 : MinIterations;
            _iterations = Math.Max(floor, config.GetValue("Auth:Pbkdf2Iterations", MinIterations));
            // PBKDF2 e caro de proposito: limita quantos rodam ao mesmo tempo (protege a CPU do plano free)
            var concurrency = Math.Max(1, config.GetValue("Auth:HashConcurrency", 3));
            _gate = new SemaphoreSlim(concurrency, concurrency);
            _wait = TimeSpan.FromMilliseconds(config.GetValue("Auth:HashWaitMs", 3000));
            _maxQueue = concurrency * 8;
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
                // hash/sal vazios ou truncados nunca validam (Pbkdf2 de 0 bytes igualaria 0 bytes)
                if (salt.Length < 8 || expected.Length < 16) return false;
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return false;   // hash corrompido nunca vira 500
            }
        }

        public Task<string> HashAsync(string password) => Gated(() => Hash(password));
        public Task<bool> VerifyAsync(string storedHash, string password) => Gated(() => Verify(storedHash, password));
        public Task VerifyDummyAsync(string password) => Gated(() => { VerifyDummy(password); return true; });

        private async Task<T> Gated<T>(Func<T> work)
        {
            if (Interlocked.Increment(ref _pending) > _maxQueue + _gate.CurrentCount)
            {
                Interlocked.Decrement(ref _pending);
                throw new HasherBusyException();
            }
            try
            {
                if (!await _gate.WaitAsync(_wait)) throw new HasherBusyException();
                try { return work(); }
                finally { _gate.Release(); }
            }
            finally { Interlocked.Decrement(ref _pending); }
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
