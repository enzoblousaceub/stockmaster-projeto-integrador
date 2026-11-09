using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using backend.Models;
using backend.Security;

namespace backend.DTOs
{
    public static class UsuarioRules
    {
        public const string Pattern = "^[a-zA-Z0-9._-]{3,32}$";
        public static string Normalize(string usuario) => usuario.Trim().ToLowerInvariant();
        public static bool IsValid(string? usuario) =>
            usuario is not null && System.Text.RegularExpressions.Regex.IsMatch(usuario.Trim(), Pattern);
    }

    public class LoginDto
    {
        [Required, RegularExpression(UsuarioRules.Pattern)]
        public string Usuario { get; set; } = string.Empty;

        [Required, MaxLength(PasswordRules.Max)]
        public string Senha { get; set; } = string.Empty;
    }

    public record MeDto(string Usuario, string Perfil);

    public class AtivarDto
    {
        [Required, RegularExpression(UsuarioRules.Pattern)]
        public string Usuario { get; set; } = string.Empty;

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
        [Required, RegularExpression(UsuarioRules.Pattern)]
        public string Usuario { get; set; } = string.Empty;

        [Required]
        public string Perfil { get; set; } = string.Empty;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public class UpdateUserDto
    {
        public string? Perfil { get; set; }
        public bool? Ativo { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Usuario { get; set; } = string.Empty;
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

        public static UserDto From(AppUser u, DateTime? lockedUntil = null, string? code = null) => new()
        {
            Id = u.Id,
            Usuario = u.Usuario,
            Perfil = u.Role,
            Status = u.Status,
            BloqueadoAte = lockedUntil,
            CriadoEm = u.CreatedAt,
            CriadoPor = u.CreatedBy,
            AtualizadoEm = u.UpdatedAt,
            AtualizadoPor = u.UpdatedBy,
            UltimoLoginEm = u.LastLoginAt,
            CodigoExpiraEm = u.Status == UserStatus.Pending && u.ActivationUsedAt is null ? u.ActivationExpiresAt : null,
            CodigoAtivacao = code
        };
    }

    public record AuditDto(long Id, DateTime OcorridoEm, string Evento, string? AtorUsuario, string? AlvoUsuario,
        string? Ip, string Resultado, string? Detalhe);
}
