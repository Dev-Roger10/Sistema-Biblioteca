using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Pago
    {
        [Key]
        public int IdPago { get; set; }

        [Required]
        public int IdSancion { get; set; }

        [Required]
        [Range(0.01, 10000)]
        public decimal MontoPagado { get; set; }

        public DateTime FechaPago { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string MetodoPago { get; set; } // Efectivo, Tarjeta, Transferencia

        [StringLength(100)]
        public string Comprobante { get; set; }

        [StringLength(300)]
        public string Observaciones { get; set; }

        // Navegación
        [ForeignKey("IdSancion")]
        public virtual Sancion Sancion { get; set; }
    }
}
