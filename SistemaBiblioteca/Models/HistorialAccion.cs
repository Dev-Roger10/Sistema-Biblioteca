using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class HistorialAccion
    {
        [Key]
        public int IdHistorial { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        [Required]
        [StringLength(100)]
        public string Accion { get; set; }

        [StringLength(50)]
        public string TablaAfectada { get; set; }

        public int? RegistroAfectado { get; set; }

        public string Detalles { get; set; }

        public DateTime FechaAccion { get; set; } = DateTime.Now;

        // Navegación
        [ForeignKey("IdUsuario")]
        public virtual Usuario Usuario { get; set; }
    }
}
