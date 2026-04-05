using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Ejemplar
    {
        [Key]
        public int IdEjemplar { get; set; }

        [Required]
        public int IdLibro { get; set; }

        [Required]
        public int IdSede { get; set; }

        [Required(ErrorMessage = "El código del ejemplar es obligatorio")]
        [StringLength(50)]
        public string CodigoEjemplar { get; set; }

        [StringLength(20)]
        public string Estado { get; set; } = "Disponible"; // Disponible, Prestado, Reservado, Mantenimiento, Perdido

        [StringLength(300)]
        public string Observaciones { get; set; }

        public DateTime FechaAdquisicion { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;

        // Navegación
        [ForeignKey("IdLibro")]
        public virtual Libro Libro { get; set; }

        [ForeignKey("IdSede")]
        public virtual Sede Sede { get; set; }

        public virtual ICollection<Prestamo> Prestamos { get; set; }
    }
}
