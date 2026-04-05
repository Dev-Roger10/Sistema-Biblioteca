using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Prestamo
    {
        [Key]
        public int IdPrestamo { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        [Required]
        public int IdEjemplar { get; set; }

        public int? IdReserva { get; set; }

        public DateTime FechaPrestamo { get; set; } = DateTime.Now;

        [Required]
        public DateTime FechaDevolucionEsperada { get; set; }

        public DateTime? FechaDevolucionReal { get; set; }

        [StringLength(20)]
        public string EstadoPrestamo { get; set; } = "Activo"; // Activo, Devuelto, Vencido

        public int DiasRetraso { get; set; } = 0;

        [StringLength(300)]
        public string Observaciones { get; set; }

        // Navegación
        [ForeignKey("IdUsuario")]
        public virtual Usuario Usuario { get; set; }

        [ForeignKey("IdEjemplar")]
        public virtual Ejemplar Ejemplar { get; set; }

        [ForeignKey("IdReserva")]
        public virtual Reserva Reserva { get; set; }

        public virtual ICollection<Sancion> Sanciones { get; set; }

        [NotMapped]
        public bool EstaVencido => FechaDevolucionReal == null && DateTime.Now > FechaDevolucionEsperada;
    }
}
