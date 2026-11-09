using System.Security.Cryptography;
using System.Text;

namespace backend.Security
{
    /// <summary>
    /// Codigo de ativacao: 12 chars Crockford base32 (sem I, L, O, U), de uso unico e 48h.
    /// So o HMAC-SHA256(chave do servidor, usuario|codigo) e guardado; a comparacao e em tempo constante.
    /// </summary>
    public class ActivationCodes
    {
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        public const int Length = 12;
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

        private readonly byte[]? _key;

        public ActivationCodes(IConfiguration config)
        {
            var raw = config["Auth:CodeHmacKey"];
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                var key = Convert.FromBase64String(raw);
                if (key.Length >= 32) _key = key;
            }
            catch (FormatException) { /* chave invalida = nao configurada */ }
        }

        public bool IsConfigured => _key is not null;

        /// <summary>Codigo novo em formato exibivel (XXXX-XXXX-XXXX).</summary>
        public static string Generate()
        {
            var raw = new string(RandomNumberGenerator.GetItems<char>(Alphabet, Length));
            return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
        }

        /// <summary>Remove hifens/espacos, poe em maiusculas e valida o formato. Nulo se invalido.</summary>
        public static string? Normalize(string? input)
        {
            if (input is null) return null;
            var s = new string(input.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            return s.Length == Length && s.All(c => Alphabet.Contains(c)) ? s : null;
        }

        public string Hash(string usuario, string normalizedCode)
        {
            var mac = HMACSHA256.HashData(_key!, Encoding.UTF8.GetBytes($"{usuario}|{normalizedCode}"));
            return Convert.ToBase64String(mac);
        }

        /// <summary>Comparacao em tempo constante; quando nao ha hash guardado compara contra um valor falso.</summary>
        public bool Matches(string? storedHash, string usuario, string normalizedCode)
        {
            var candidate = Convert.FromBase64String(Hash(usuario, normalizedCode));
            var stored = new byte[candidate.Length];
            var has = false;
            if (storedHash is not null)
            {
                try
                {
                    var b = Convert.FromBase64String(storedHash);
                    if (b.Length == candidate.Length) { stored = b; has = true; }
                }
                catch (FormatException) { }
            }
            return CryptographicOperations.FixedTimeEquals(candidate, stored) && has;
        }
    }
}
