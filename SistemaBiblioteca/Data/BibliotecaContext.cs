using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Data
{
    public class BibliotecaContext : DbContext
    {
        public BibliotecaContext(DbContextOptions<BibliotecaContext> options) : base(options)
        {
        }

        // DbSets
        public DbSet<Rol> Roles { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Editorial> Editoriales { get; set; }
        public DbSet<Libro> Libros { get; set; }
        public DbSet<Sede> Sedes { get; set; }
        public DbSet<Ejemplar> Ejemplares { get; set; }
        public DbSet<Reserva> Reservas { get; set; }
        public DbSet<Prestamo> Prestamos { get; set; }
        public DbSet<Sancion> Sanciones { get; set; }
        public DbSet<Pago> Pagos { get; set; }
        public DbSet<Notificacion> Notificaciones { get; set; }
        public DbSet<ConfiguracionSistema> ConfiguracionSistema { get; set; }
        public DbSet<HistorialAccion> HistorialAcciones { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuraciones de relaciones con restricciones de eliminación

            // Usuario -> Rol (NO ACTION para evitar ciclos)
            modelBuilder.Entity<Usuario>()
                .HasOne(u => u.Rol)
                .WithMany(r => r.Usuarios)
                .HasForeignKey(u => u.IdRol)
                .OnDelete(DeleteBehavior.Restrict);

            // Libro -> Editorial (NO ACTION)
            modelBuilder.Entity<Libro>()
                .HasOne(l => l.Editorial)
                .WithMany(e => e.Libros)
                .HasForeignKey(l => l.IdEditorial)
                .OnDelete(DeleteBehavior.Restrict);

            // Libro -> Categoria (NO ACTION)
            modelBuilder.Entity<Libro>()
                .HasOne(l => l.Categoria)
                .WithMany(c => c.Libros)
                .HasForeignKey(l => l.IdCategoria)
                .OnDelete(DeleteBehavior.Restrict);

            // Ejemplar -> Libro (NO ACTION para evitar conflictos con Prestamos)
            modelBuilder.Entity<Ejemplar>()
                .HasOne(e => e.Libro)
                .WithMany(l => l.Ejemplares)
                .HasForeignKey(e => e.IdLibro)
                .OnDelete(DeleteBehavior.Restrict);

            // Ejemplar -> Sede (NO ACTION)
            modelBuilder.Entity<Ejemplar>()
                .HasOne(e => e.Sede)
                .WithMany(s => s.Ejemplares)
                .HasForeignKey(e => e.IdSede)
                .OnDelete(DeleteBehavior.Restrict);

            // Prestamo -> Usuario (NO ACTION)
            modelBuilder.Entity<Prestamo>()
                .HasOne(p => p.Usuario)
                .WithMany(u => u.Prestamos)
                .HasForeignKey(p => p.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            // Prestamo -> Ejemplar (NO ACTION)
            modelBuilder.Entity<Prestamo>()
                .HasOne(p => p.Ejemplar)
                .WithMany(e => e.Prestamos)
                .HasForeignKey(p => p.IdEjemplar)
                .OnDelete(DeleteBehavior.Restrict);

            // Prestamo -> Reserva (NO ACTION - CRÍTICO para evitar ciclos)
            modelBuilder.Entity<Prestamo>()
                .HasOne(p => p.Reserva)
                .WithMany(r => r.Prestamos)
                .HasForeignKey(p => p.IdReserva)
                .OnDelete(DeleteBehavior.Restrict);

            // Reserva -> Usuario (NO ACTION)
            modelBuilder.Entity<Reserva>()
                .HasOne(r => r.Usuario)
                .WithMany(u => u.Reservas)
                .HasForeignKey(r => r.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            // Reserva -> Libro (NO ACTION)
            modelBuilder.Entity<Reserva>()
                .HasOne(r => r.Libro)
                .WithMany(l => l.Reservas)
                .HasForeignKey(r => r.IdLibro)
                .OnDelete(DeleteBehavior.Restrict);

            // Sancion -> Usuario (NO ACTION)
            modelBuilder.Entity<Sancion>()
                .HasOne(s => s.Usuario)
                .WithMany(u => u.Sanciones)
                .HasForeignKey(s => s.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            // Sancion -> Prestamo (NO ACTION)
            modelBuilder.Entity<Sancion>()
                .HasOne(s => s.Prestamo)
                .WithMany(p => p.Sanciones)
                .HasForeignKey(s => s.IdPrestamo)
                .OnDelete(DeleteBehavior.Restrict);

            // Pago -> Sancion (NO ACTION)
            modelBuilder.Entity<Pago>()
                .HasOne(p => p.Sancion)
                .WithMany(s => s.Pagos)
                .HasForeignKey(p => p.IdSancion)
                .OnDelete(DeleteBehavior.Restrict);

            // Notificacion -> Usuario (NO ACTION)
            modelBuilder.Entity<Notificacion>()
                .HasOne(n => n.Usuario)
                .WithMany()
                .HasForeignKey(n => n.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            // HistorialAccion -> Usuario (NO ACTION)
            modelBuilder.Entity<HistorialAccion>()
                .HasOne(h => h.Usuario)
                .WithMany()
                .HasForeignKey(h => h.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);

            // Configurar propiedades decimales
            modelBuilder.Entity<Sancion>()
                .Property(s => s.Monto)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<Pago>()
                .Property(p => p.MontoPagado)
                .HasColumnType("decimal(10,2)");

            // Índices únicos
            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.Correo)
                .IsUnique();

            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.DNI)
                .IsUnique();

            modelBuilder.Entity<Libro>()
                .HasIndex(l => l.ISBN)
                .IsUnique();

            modelBuilder.Entity<Ejemplar>()
                .HasIndex(e => e.CodigoEjemplar)
                .IsUnique();

            modelBuilder.Entity<ConfiguracionSistema>()
                .HasIndex(c => c.Parametro)
                .IsUnique();
        }
    }
}
