using System.ComponentModel.DataAnnotations;

namespace SistemaBiblioteca.Models
{
    public class Rol
    {
        [Key]
        public int IdRol { get; set; }

        [Required(ErrorMessage = "El nombre del rol es obligatorio")]
        [StringLength(50)]
        public string NombreRol { get; set; }

        [StringLength(200)]
        public string Descripcion { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;

        // Navegación
        public virtual ICollection<Usuario> Usuarios { get; set; }
    }
}
