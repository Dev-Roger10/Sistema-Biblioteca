using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Sancion
    {
        [Key]
        public int IdSancion { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        public int? IdPrestamo { get; set; }

        [Required(ErrorMessage = "El tipo de sanción es obligatorio")]
        [StringLength(50)]
        public string TipoSancion { get; set; } // Retraso, Daño, Pérdida

        [Required]
        [Range(0, 10000)]
        public decimal Monto { get; set; }

        public DateTime FechaSancion { get; set; } = DateTime.Now;

        [StringLength(20)]
        public string EstadoSancion { get; set; } = "Pendiente"; // Pendiente, Pagada, Anulada

        [StringLength(300)]
        public string Descripcion { get; set; }

        // Navegación
        [ForeignKey("IdUsuario")]
        public virtual Usuario Usuario { get; set; }

        [ForeignKey("IdPrestamo")]
        public virtual Prestamo Prestamo { get; set; }

        public virtual ICollection<Pago> Pagos { get; set; }

        [NotMapped]
        public decimal MontoRestante { get; set; }
    }
}
