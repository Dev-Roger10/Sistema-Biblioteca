using System.ComponentModel.DataAnnotations;

namespace SistemaBiblioteca.Models
{
    public class Categoria
    {
        [Key]
        public int IdCategoria { get; set; }

        [Required(ErrorMessage = "El nombre de la categoría es obligatorio")]
        [StringLength(100)]
        public string NombreCategoria { get; set; }

        [StringLength(300)]
        public string Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        // Navegación
        public virtual ICollection<Libro> Libros { get; set; }
    }
}
