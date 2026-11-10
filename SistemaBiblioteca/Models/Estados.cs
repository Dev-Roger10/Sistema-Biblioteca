namespace SistemaBiblioteca.Models
{
    // Valores de estado que se guardan como texto en la base de datos.
    // Usar estas constantes en vez de literales evita errores de tipeo
    // ("Activo" vs "activo") que el compilador no detecta.

    public static class EstadoEjemplar
    {
        public const string Disponible = "Disponible";
        public const string Prestado = "Prestado";
        public const string Mantenimiento = "Mantenimiento";
        public const string Danado = "Dañado";
    }

    public static class EstadoPrestamo
    {
        public const string Activo = "Activo";
        public const string Vencido = "Vencido";   // Solo en registros históricos; hoy el vencimiento se calcula por fecha.
        public const string Devuelto = "Devuelto";
    }

    public static class EstadoReserva
    {
        public const string Pendiente = "Pendiente";
        public const string Confirmada = "Confirmada";
    }

    public static class EstadoSancion
    {
        public const string Pendiente = "Pendiente";
    }
}
