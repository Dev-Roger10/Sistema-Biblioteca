using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Libro
    {
        [Key]
        public int IdLibro { get; set; }

        [StringLength(20)]
        public string ISBN { get; set; }

        [Required(ErrorMessage = "El título es obligatorio")]
        [StringLength(200)]
        public string Titulo { get; set; }

        [Required(ErrorMessage = "El autor es obligatorio")]
        [StringLength(200)]
        public string Autor { get; set; }

        public int? IdEditorial { get; set; }
        public int? IdCategoria { get; set; }

        [Range(1800, 2100, ErrorMessage = "Año inválido")]
        public int? AñoPublicacion { get; set; }

        public int? NumPaginas { get; set; }

        [StringLength(50)]
        public string Idioma { get; set; }

        public string Descripcion { get; set; }

        [StringLength(500)]
        public string ImagenPortada { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;

        // Navegación
        [ForeignKey("IdEditorial")]
        public virtual Editorial? Editorial { get; set; }

        [ForeignKey("IdCategoria")]
        public virtual Categoria? Categoria { get; set; }

        public virtual ICollection<Ejemplar>? Ejemplares { get; set; } = new List<Ejemplar>();
        public virtual ICollection<Reserva>? Reservas { get; set; } = new List<Reserva>();

        [NotMapped]
        public int EjemplaresDisponibles { get; set; }

        [NotMapped]
        public int TotalEjemplares { get; set; }
    }
}
