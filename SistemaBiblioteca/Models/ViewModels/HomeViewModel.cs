namespace SistemaBiblioteca.Models.ViewModels
{
    // Cifras reales de la base de datos para la página de inicio.
    public class HomeViewModel
    {
        // Públicas (se muestran también sin sesión)
        public int LibrosActivos { get; set; }
        public int EjemplaresDisponibles { get; set; }
        public int Categorias { get; set; }
        public int Sedes { get; set; }

        // Dashboard (con sesión). Para personal son globales; para lectores, solo las propias.
        public bool EsPersonal { get; set; }
        public int PrestamosActivos { get; set; }
        public int PrestamosVencidos { get; set; }
        public int ReservasPendientes { get; set; }
        public int SancionesPendientes { get; set; }
        public int LibrosNuevosMes { get; set; }
    }
}
