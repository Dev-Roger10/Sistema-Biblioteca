using System.ComponentModel.DataAnnotations;

namespace SistemaBiblioteca.Models
{
    public class Editorial
    {
        [Key]
        public int IdEditorial { get; set; }

        [Required(ErrorMessage = "El nombre de la editorial es obligatorio")]
        [StringLength(100)]
        public string NombreEditorial { get; set; }

        [StringLength(50)]
        public string Pais { get; set; }

        public bool Activo { get; set; } = true;

        // Navegación
        public virtual ICollection<Libro> Libros { get; set; }
    }

}
