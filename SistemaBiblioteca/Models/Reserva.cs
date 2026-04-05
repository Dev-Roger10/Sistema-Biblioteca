using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Reserva
    {
        [Key]
        public int IdReserva { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        [Required]
        public int IdLibro { get; set; }

        [Required(ErrorMessage = "El tipo de reserva es obligatorio")]
        [StringLength(20)]
        public string TipoReserva { get; set; } // Local, Virtual

        public DateTime FechaReserva { get; set; } = DateTime.Now;
        public DateTime? FechaVencimiento { get; set; }

        [StringLength(20)]
        public string EstadoReserva { get; set; } = "Pendiente"; // Pendiente, Confirmada, Cancelada, Vencida

        [StringLength(300)]
        public string Observaciones { get; set; }

        // Navegación
        [ForeignKey("IdUsuario")]
        public virtual Usuario? Usuario { get; set; }

        [ForeignKey("IdLibro")]
        public virtual Libro? Libro { get; set; }

        public virtual ICollection<Prestamo>? Prestamos { get; set; } = new List<Prestamo>();
    }
}
