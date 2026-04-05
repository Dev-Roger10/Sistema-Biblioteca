using System.ComponentModel.DataAnnotations;

namespace SistemaBiblioteca.Models
{
    public class Sede
    {
        [Key]
        public int IdSede { get; set; }

        [Required(ErrorMessage = "El nombre de la sede es obligatorio")]
        [StringLength(100)]
        public string NombreSede { get; set; }

        [StringLength(200)]
        public string Direccion { get; set; }

        [StringLength(20)]
        public string Telefono { get; set; }

        public bool Activo { get; set; } = true;

        // Navegación
        public virtual ICollection<Ejemplar> Ejemplares { get; set; }
    }
}
