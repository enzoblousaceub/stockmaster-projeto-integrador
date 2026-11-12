using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using backend.Models;
using backend.Security;

namespace backend.DTOs
{
    public static class EmailRules
    {
        public const int MaxLength = 254;
        public static string Normalize(string email) => email.Trim().ToLowerInvariant();
        public static bool IsValid(string? email)
        {
            var t = email?.Trim();
            if (string.IsNullOrEmpty(t) || t.Length > MaxLength || t.Any(char.IsWhiteSpace) || !new EmailAddressAttribute().IsValid(t)) return false;
            var at = t.LastIndexOf('@');
            return at > 0 && t.IndexOf('.', at) > at + 1 && !t.EndsWith('.');
        }
    }

    public class LoginDto
    {
        [Required, EmailAddress, MaxLength(EmailRules.MaxLength)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(PasswordRules.Max)]
        public string Senha { get; set; } = string.Empty;
    }

    public record MeDto(string Email, string Perfil);

    public class AtivarDto
    {
        [Required, EmailAddress, MaxLength(EmailRules.MaxLength)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(40)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        public string NovaSenha { get; set; } = string.Empty;
    }

    public class ChangePasswordDto
    {
        [Required, MaxLength(PasswordRules.Max)]
        public string SenhaAtual { get; set; } = string.Empty;

        [Required]
        public string NovaSenha { get; set; } = string.Empty;
    }

    public class CreateUserDto
    {
        [Required, EmailAddress, MaxLength(EmailRules.MaxLength)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Perfil { get; set; } = string.Empty;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public class UpdateUserDto
    {
        public string? Perfil { get; set; }
        public bool? Ativo { get; set; }
        [EmailAddress, MaxLength(EmailRules.MaxLength)]
        public string? Email { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Perfil { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? BloqueadoAte { get; set; }
        public DateTime CriadoEm { get; set; }
        public string? CriadoPor { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public string? AtualizadoPor { get; set; }
        public DateTime? UltimoLoginEm { get; set; }
        public DateTime? CodigoExpiraEm { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CodigoAtivacao { get; set; }
        /// <summary>true = convite enviado por e-mail (o codigo nao e devolvido); false = falhou (o codigo vem junto).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EmailEnviado { get; set; }

        public static UserDto From(AppUser u, DateTime? lockedUntil = null, string? code = null, bool? emailSent = null) => new()
        {
            Id = u.Id,
            Email = u.Email,
            Perfil = u.Role,
            Status = u.Status,
            BloqueadoAte = lockedUntil,
            CriadoEm = u.CreatedAt,
            CriadoPor = u.CreatedBy,
            AtualizadoEm = u.UpdatedAt,
            AtualizadoPor = u.UpdatedBy,
            UltimoLoginEm = u.LastLoginAt,
            CodigoExpiraEm = u.Status == UserStatus.Pending && u.ActivationUsedAt is null ? u.ActivationExpiresAt : null,
            CodigoAtivacao = code,
            EmailEnviado = emailSent
        };
    }

    public record AuditDto(long Id, DateTime OcorridoEm, string Evento, string? AtorUsuario, string? AlvoUsuario,
        string? Ip, string Resultado, string? Detalhe);
}
