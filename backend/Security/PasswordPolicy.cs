namespace backend.Security
{
    public static class PasswordRules
    {
        public const int Min = 8;
        public const int Max = 128;
    }

    /// <summary>Politica de senha: tamanho 8..128, fora da lista de senhas comuns, diferente do usuario e nao so um caractere repetido.</summary>
    public static class PasswordPolicy
    {
        private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
        {
            "12345678", "123456789", "1234567890", "12345678910", "87654321", "11111111", "00000000", "123123123",
            "password", "password1", "password123", "passw0rd", "p@ssw0rd", "p@ssword", "qwertyui", "qwerty123",
            "qwertyuiop", "asdfghjk", "asdfghjkl", "zxcvbnm1", "abc12345", "abcd1234", "abcdefgh", "iloveyou",
            "admin123", "administrator", "admin1234", "letmein1", "welcome1", "welcome123", "monkey123", "dragon123",
            "senha123", "senha1234", "senha@123", "minhasenha", "mudar123", "mudar@123", "trocar123",
            "brasil123", "brasil2024", "brasil2025", "brasil2026", "stockmaster", "stockmaster1", "stockmaster123",
            "estoque123", "usuario123", "12341234", "1q2w3e4r", "1qaz2wsx", "q1w2e3r4", "football1", "superman1"
        };

        /// <summary>Retorna null se valida, ou o code do erro ("password_too_short" | "password_weak").</summary>
        public static string? Validate(string password, string? usuario)
        {
            if (password.Length < PasswordRules.Min || password.Length > PasswordRules.Max) return "password_too_short";
            if (Common.Contains(password)) return "password_weak";
            if (usuario is not null && string.Equals(password, usuario, StringComparison.OrdinalIgnoreCase)) return "password_weak";
            if (password.Distinct().Count() == 1) return "password_weak";
            return null;
        }

        public static string Message(string code) => code == "password_too_short"
            ? $"A senha deve ter entre {PasswordRules.Min} e {PasswordRules.Max} caracteres."
            : "Escolha uma senha menos previsivel (nao use senhas comuns, o proprio usuario ou um caractere repetido).";
    }
}
