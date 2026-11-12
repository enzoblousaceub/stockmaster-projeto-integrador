using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data
{
    public class AppDbContext : DbContext, Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.IDataProtectionKeyContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<AppUser> Users { get; set; }
        public DbSet<AuthThrottle> AuthThrottles { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Price).HasColumnType("numeric(18,2)");
                entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
                entity.HasIndex(e => e.UpdatedAt);
                entity.HasIndex(e => e.Category);
            });

            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).HasMaxLength(254).IsRequired();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.PasswordHash).HasMaxLength(300);
                entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
                entity.Property(e => e.SecurityStamp).HasMaxLength(64).IsRequired();
                entity.Property(e => e.ActivationCodeHash).HasMaxLength(64);
                entity.Property(e => e.CreatedBy).HasMaxLength(254);
                entity.Property(e => e.UpdatedBy).HasMaxLength(254);
                entity.Ignore(e => e.Status);
                entity.Ignore(e => e.IsLoginCapableAdmin);
            });

            modelBuilder.Entity<AuthThrottle>(entity =>
            {
                entity.HasKey(e => new { e.Kind, e.Key });
                entity.Property(e => e.Kind).HasMaxLength(10);
                entity.Property(e => e.Key).HasMaxLength(254);
            });

            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Evento).HasMaxLength(40).IsRequired();
                entity.Property(e => e.AtorUsuario).HasMaxLength(254);
                entity.Property(e => e.AlvoUsuario).HasMaxLength(254);
                entity.Property(e => e.Ip).HasMaxLength(45);
                entity.Property(e => e.Resultado).HasMaxLength(10).IsRequired();
                entity.Property(e => e.Detalhe).HasMaxLength(200);
                entity.HasIndex(e => e.OccurredAt);
            });
        }
    }
}
