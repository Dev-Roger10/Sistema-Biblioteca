using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemaBiblioteca.Models
{
    public class Notificacion
    {
        [Key]
        public int IdNotificacion { get; set; }

        [Required]
        public int IdUsuario { get; set; }

        [StringLength(50)]
        public string TipoNotificacion { get; set; } // Reserva, Devolución, Sanción

        [StringLength(200)]
        public string Asunto { get; set; }

        public string Mensaje { get; set; }

        public DateTime FechaEnvio { get; set; } = DateTime.Now;
        public bool Leida { get; set; } = false;

        // Navegación
        [ForeignKey("IdUsuario")]
        public virtual Usuario Usuario { get; set; }
    }
}
