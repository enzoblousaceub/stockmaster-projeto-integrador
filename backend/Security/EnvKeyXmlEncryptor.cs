using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace backend.Security
{
    /// <summary>
    /// Cifra as chaves do Data Protection (AES-256-GCM) com Auth__KeyEncryptionKey antes de grava-las no Postgres.
    /// Sem isso, quem le o banco (dump/backup) conseguiria forjar o cookie de sessao.
    /// </summary>
    public class EnvKeyXmlEncryptor : IXmlEncryptor
    {
        private readonly byte[] _key;
        public EnvKeyXmlEncryptor(byte[] key) => _key = key;

        public static byte[]? ParseKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                var key = Convert.FromBase64String(raw);
                return key.Length >= 32 ? SHA256.HashData(key) : null;
            }
            catch (FormatException) { return null; }
        }

        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[data.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(_key, 16)) aes.Encrypt(nonce, data, cipher, tag);
            var payload = nonce.Concat(tag).Concat(cipher).ToArray();
            return new EncryptedXmlInfo(new XElement("enc", Convert.ToBase64String(payload)), typeof(EnvKeyXmlDecryptor));
        }
    }

    public class EnvKeyXmlDecryptor : IXmlDecryptor
    {
        private readonly byte[]? _key;

        public EnvKeyXmlDecryptor(IServiceProvider services) =>
            _key = EnvKeyXmlEncryptor.ParseKey(services.GetRequiredService<IConfiguration>()["Auth:KeyEncryptionKey"]);

        public XElement Decrypt(XElement encryptedElement)
        {
            if (_key is null)
                throw new InvalidOperationException("Auth__KeyEncryptionKey ausente: nao e possivel ler as chaves do Data Protection.");
            var payload = Convert.FromBase64String(encryptedElement.Value);
            var nonce = payload[..12];
            var tag = payload[12..28];
            var cipher = payload[28..];
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(_key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            return XElement.Parse(System.Text.Encoding.UTF8.GetString(plain));
        }
    }

    public class HasherBusyHandler : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
        {
            if (ex is not HasherBusyException) return false;
            await ProblemWriter.WriteAsync(ctx, 503, "server_busy", "Servidor ocupado", "Tente novamente em instantes.", retryAfterSeconds: 5);
            return true;
        }
    }
}
