using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data
{
    public class AppDbContext : DbContext, Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.IDataProtectionKeyContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<Sale> Sales { get; set; }
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
                entity.Property(e => e.Cost).HasColumnType("numeric(18,2)");
                entity.Property(e => e.ImageUpdatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.Brand).HasMaxLength(100);
                entity.HasIndex(e => e.UpdatedAt);
                entity.HasIndex(e => e.Category);
                entity.HasMany(e => e.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Size).HasMaxLength(ProductVariant.MaxSizeLength).IsRequired();
                entity.Property(e => e.Material).HasMaxLength(ProductVariant.MaxMaterialLength).IsRequired();
                entity.Ignore(e => e.Label);
                // unicidade (tamanho+material) validada no codigo: um indice unico quebraria trocas na mesma edicao
                entity.HasIndex(e => e.ProductId);
            });

            modelBuilder.Entity<Sale>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PaymentMethod).HasMaxLength(10).IsRequired();
                entity.Property(e => e.CreatedBy).HasMaxLength(254);
                entity.Property(e => e.UndoneBy).HasMaxLength(254);
                entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.UndoneAt).HasColumnType("timestamp with time zone");
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => new { e.CreatedBy, e.CreatedAt });
                entity.HasMany(e => e.Movements).WithOne(m => m.Sale).HasForeignKey(m => m.SaleId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockMovement>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.SetNull);
                entity.Property(e => e.ProductName).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Type).HasMaxLength(10).IsRequired();
                entity.Property(e => e.Reason).HasMaxLength(200);
                entity.Property(e => e.CreatedBy).HasMaxLength(254);
                entity.Property(e => e.VariantLabel).HasMaxLength(ProductVariant.MaxSizeLength + ProductVariant.MaxMaterialLength + 3);
                entity.Property(e => e.PaymentMethod).HasMaxLength(10);
                entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(e => e.SaleId);
                entity.Property(e => e.OccurredAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
                entity.HasIndex(e => new { e.ProductId, e.OccurredAt });
                entity.HasIndex(e => e.OccurredAt);
            });

            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.HasKey(e => e.ProductId);
                entity.HasOne<Product>().WithOne().HasForeignKey<ProductImage>(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
                entity.Property(e => e.ContentType).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Data).IsRequired();
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
