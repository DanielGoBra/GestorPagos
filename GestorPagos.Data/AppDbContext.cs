using GestorPagos.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestorPagos.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Pago> Pagos { get; set; }
        public DbSet<HistorialPago> HistorialPagos { get; set; }
        public DbSet<ExcepcionPago> ExcepcionesPagos { get; set; }
        public DbSet<ConfiguracionColumna> ConfiguracionColumnas { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Pago Configuration
            modelBuilder.Entity<Pago>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CodigoPago).HasMaxLength(20);
                entity.HasIndex(e => e.CodigoPago).IsUnique();
                entity.Property(e => e.Monto).HasColumnType("decimal(19,4)");
                entity.Property(e => e.CedulaCliente).HasMaxLength(20);
                entity.Property(e => e.CedulaCorrecta).HasMaxLength(20);
                entity.Property(e => e.Convenio).HasMaxLength(50);
                entity.Property(e => e.NumeroAprobacion).HasMaxLength(100);
                entity.Property(e => e.CC).HasMaxLength(50);

                // Hash único para detectar duplicados (Cédula + Monto + FechaConsignación)
                entity.Property(e => e.HashUnico).HasMaxLength(16);
                entity.HasIndex(e => e.HashUnico).IsUnique();

                // Nuevas propiedades del desglose
                entity.Property(e => e.Capital).HasColumnType("decimal(19,4)");
                entity.Property(e => e.Intereses).HasColumnType("decimal(19,4)");
                entity.Property(e => e.Cargos).HasColumnType("decimal(19,4)");
                entity.Property(e => e.Mora).HasColumnType("decimal(19,4)");
                entity.Property(e => e.Condonacion).HasColumnType("decimal(19,4)");
                entity.Property(e => e.TotalAplicado).HasColumnType("decimal(19,4)");

                // Campos de auditoría
                entity.Property(e => e.UsuarioDesglose).HasMaxLength(100);
                entity.Property(e => e.UsuarioBloqueo).HasMaxLength(100);

                // Índices para búsquedas
                entity.HasIndex(e => new { e.CedulaCliente, e.FechaConsignacion, e.Convenio });
                entity.HasIndex(e => e.CedulaCorrecta);
                entity.HasIndex(e => e.FechaImportacion);
                entity.HasIndex(e => e.Estado);
                entity.HasIndex(e => e.CC);
                entity.HasIndex(e => e.Bloqueado);
                entity.HasIndex(e => e.FechaDesglose);
            });

            // HistorialPago Configuration
            modelBuilder.Entity<HistorialPago>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CodigoPago).HasMaxLength(20);
                entity.HasIndex(e => e.PagoId);
                entity.HasIndex(e => e.FechaAccion);
            });

            // ExcepcionPago Configuration
            modelBuilder.Entity<ExcepcionPago>(entity =>
            {
                entity.HasKey(e => e.Id_Excepcion);
            });

            // ConfiguracionColumna Configuration
            modelBuilder.Entity<ConfiguracionColumna>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }
}
