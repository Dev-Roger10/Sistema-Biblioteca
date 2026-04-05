using System.ComponentModel.DataAnnotations;

namespace SistemaBiblioteca.Models
{
    public class ConfiguracionSistema
    {
        [Key]
        public int IdConfiguracion { get; set; }

        [Required]
        [StringLength(100)]
        public string Parametro { get; set; }

        [Required]
        [StringLength(200)]
        public string Valor { get; set; }

        [StringLength(300)]
        public string Descripcion { get; set; }

        public DateTime FechaModificacion { get; set; } = DateTime.Now;
    }
}
