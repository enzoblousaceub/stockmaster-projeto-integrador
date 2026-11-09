namespace backend.Models
{
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Viewer = "Visualizador";
        public static bool IsValid(string? role) => role is Admin or Viewer;
    }

    public static class UserStatus
    {
        public const string Pending = "Pendente";
        public const string Active = "Ativo";
        public const string Inactive = "Inativo";
    }

    public class AppUser
    {
        public int Id { get; set; }
        /// <summary>Login, sempre em minusculas (unico). 3-32 chars [a-z0-9._-].</summary>
        public string Usuario { get; set; } = string.Empty;
        /// <summary>Nulo enquanto o usuario esta Pendente (sem senha definida).</summary>
        public string? PasswordHash { get; set; }
        public string Role { get; set; } = Roles.Viewer;
        /// <summary>false = Inativo (desativado).</summary>
        public bool IsActive { get; set; } = true;
        /// <summary>Muda ao sair/desativar/resetar/trocar senha/mudar perfil: invalida as sessoes existentes.</summary>
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>HMAC-SHA256 do codigo de ativacao (nunca o codigo em si).</summary>
        public string? ActivationCodeHash { get; set; }
        public DateTime? ActivationExpiresAt { get; set; }
        public DateTime? ActivationUsedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }

        public string Status => !IsActive ? UserStatus.Inactive : PasswordHash is null ? UserStatus.Pending : UserStatus.Active;

        /// <summary>Admin que consegue entrar (ativo e com senha).</summary>
        public bool IsLoginCapableAdmin => IsActive && PasswordHash is not null && Role == Roles.Admin;
    }

    /// <summary>Contador de falhas (login/ativar) por nome de usuario, exista ele ou nao (anti-enumeracao).</summary>
    public class AuthThrottle
    {
        public string Kind { get; set; } = string.Empty;   // "login" | "ativar"
        public string Key { get; set; } = string.Empty;    // usuario normalizado
        public int Failures { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LockedUntil { get; set; }
    }

    public class AuditLog
    {
        public long Id { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Evento { get; set; } = string.Empty;
        public string? AtorUsuario { get; set; }
        public string? AlvoUsuario { get; set; }
        public string? Ip { get; set; }
        public string Resultado { get; set; } = "ok";
        public string? Detalhe { get; set; }
    }
}
