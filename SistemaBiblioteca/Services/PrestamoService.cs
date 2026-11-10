using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Services
{
    /// <summary>
    /// Resultado de una operación de negocio: éxito con un valor, o fallo con un mensaje
    /// apto para mostrar al usuario. Evita usar excepciones para flujos esperados
    /// (ejemplar ocupado, límite alcanzado, etc.).
    /// </summary>
    public sealed record ResultadoOperacion<T>(bool Exito, T? Valor, string? Error, string? Advertencia = null)
    {
        public static ResultadoOperacion<T> Ok(T valor, string? advertencia = null) => new(true, valor, null, advertencia);
        public static ResultadoOperacion<T> Falla(string error) => new(false, default, error);
    }

    /// <summary>Datos de una devolución ya confirmada.</summary>
    public sealed record ResultadoDevolucion(Prestamo Prestamo, int DiasRetraso, decimal Multa, Sancion? Sancion);

    /// <summary>
    /// Reglas de negocio de préstamos y devoluciones. Vive fuera de los controladores
    /// porque PrestamosController y ReservasController necesitan exactamente las mismas
    /// reglas; duplicarlas ya había provocado que una ruta se saltara validaciones.
    /// </summary>
    public interface IPrestamoService
    {
        /// <summary>
        /// Registra un préstamo (opcionalmente asociado a una reserva) de forma atómica.
        /// </summary>
        Task<ResultadoOperacion<Prestamo>> RegistrarPrestamoAsync(
            int idUsuario, int idEjemplar, int? idReserva, string? observaciones);

        /// <summary>
        /// Registra la devolución y, si hubo retraso, genera la sanción, todo en una transacción.
        /// </summary>
        Task<ResultadoOperacion<ResultadoDevolucion>> RegistrarDevolucionAsync(
            int idPrestamo, string? observaciones, string? estadoEjemplar);

        /// <summary>Calcula días de retraso y multa para una fecha de devolución esperada.</summary>
        Task<(int DiasRetraso, decimal Multa)> CalcularRetrasoAsync(DateTime fechaEsperada);
    }

    public class PrestamoService : IPrestamoService
    {
        private const int MaxObservaciones = 300;   // Longitud de las columnas Observaciones/Descripcion.
        private const string RolDocente = "Docente";

        // Estados en los que puede quedar un ejemplar al devolverlo (lista blanca:
        // el valor llega de un formulario y no se debe confiar en él).
        private static readonly string[] EstadosDevolucionValidos =
            { EstadoEjemplar.Disponible, EstadoEjemplar.Mantenimiento, EstadoEjemplar.Danado };

        private readonly BibliotecaContext _context;

        public PrestamoService(BibliotecaContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        // PRÉSTAMO
        // ------------------------------------------------------------------

        public async Task<ResultadoOperacion<Prestamo>> RegistrarPrestamoAsync(
            int idUsuario, int idEjemplar, int? idReserva, string? observaciones)
        {
            var ahora = DateTime.Now;

            var usuario = await _context.Usuarios
                .Include(u => u.Rol)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario && u.Activo);
            if (usuario == null)
                return ResultadoOperacion<Prestamo>.Falla("Usuario no encontrado o inactivo");

            var ejemplar = await _context.Ejemplares
                .Include(e => e.Libro)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdEjemplar == idEjemplar);
            if (ejemplar == null || !ejemplar.Activo || ejemplar.Estado != EstadoEjemplar.Disponible)
                return ResultadoOperacion<Prestamo>.Falla("El ejemplar no está disponible");

            // Si el préstamo viene de una reserva, debe ser coherente con ella.
            Reserva? reserva = null;
            if (idReserva.HasValue)
            {
                reserva = await _context.Reservas.FirstOrDefaultAsync(r => r.IdReserva == idReserva.Value);
                if (reserva == null)
                    return ResultadoOperacion<Prestamo>.Falla("Reserva no encontrada");
                if (reserva.EstadoReserva != EstadoReserva.Pendiente)
                    return ResultadoOperacion<Prestamo>.Falla("La reserva ya no está pendiente");
                if (reserva.IdUsuario != idUsuario)
                    return ResultadoOperacion<Prestamo>.Falla("La reserva pertenece a otro usuario");
                if (reserva.IdLibro != ejemplar.IdLibro)
                    return ResultadoOperacion<Prestamo>.Falla("El ejemplar no corresponde al libro reservado");
            }

            // Límite de préstamos simultáneos. Un préstamo vencido sigue en poder del
            // usuario, así que cuenta igual que uno activo.
            int limite = await ObtenerEnteroAsync("LimitePrestamos", 5);
            int enPoder = await _context.Prestamos.CountAsync(p =>
                p.IdUsuario == idUsuario &&
                (p.EstadoPrestamo == EstadoPrestamo.Activo || p.EstadoPrestamo == EstadoPrestamo.Vencido));
            if (enPoder >= limite)
                return ResultadoOperacion<Prestamo>.Falla(
                    $"El usuario ha alcanzado el límite de {limite} préstamos simultáneos");

            // Las sanciones pendientes no bloquean el préstamo, pero se avisa al bibliotecario.
            bool conSanciones = await _context.Sanciones.AnyAsync(s =>
                s.IdUsuario == idUsuario && s.EstadoSancion == EstadoSancion.Pendiente);

            int dias = usuario.Rol.NombreRol == RolDocente
                ? await ObtenerEnteroAsync("DiasMaximoPrestamoDocente", 30)
                : await ObtenerEnteroAsync("DiasMaximoPrestamo", 14);

            await using var tx = await _context.Database.BeginTransactionAsync();

            // Reserva atómica del ejemplar: el UPDATE condicional solo afecta la fila si
            // sigue "Disponible". Si dos bibliotecarios prestan el mismo ejemplar a la vez,
            // SQL Server serializa los UPDATE y el segundo ve 0 filas afectadas.
            // (Leer, comprobar y escribir en pasos separados sí permitiría el doble préstamo.)
            int reservados = await _context.Ejemplares
                .Where(e => e.IdEjemplar == idEjemplar && e.Activo && e.Estado == EstadoEjemplar.Disponible)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, EstadoEjemplar.Prestado));
            if (reservados == 0)
                return ResultadoOperacion<Prestamo>.Falla("El ejemplar acaba de ser prestado a otra persona");

            var prestamo = new Prestamo
            {
                IdUsuario = idUsuario,
                IdEjemplar = idEjemplar,
                IdReserva = idReserva,
                FechaPrestamo = ahora,
                FechaDevolucionEsperada = ahora.AddDays(dias),
                EstadoPrestamo = EstadoPrestamo.Activo,
                Observaciones = Recortar(observaciones, MaxObservaciones)
            };
            _context.Prestamos.Add(prestamo);

            if (reserva != null)
                reserva.EstadoReserva = EstadoReserva.Confirmada;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // Para que el llamador pueda armar mensajes sin otra consulta.
            prestamo.Ejemplar = ejemplar;
            prestamo.Usuario = usuario;

            return ResultadoOperacion<Prestamo>.Ok(
                prestamo,
                conSanciones ? "Advertencia: el usuario tiene sanciones pendientes" : null);
        }

        // ------------------------------------------------------------------
        // DEVOLUCIÓN
        // ------------------------------------------------------------------

        public async Task<ResultadoOperacion<ResultadoDevolucion>> RegistrarDevolucionAsync(
            int idPrestamo, string? observaciones, string? estadoEjemplar)
        {
            string estadoFinal = string.IsNullOrWhiteSpace(estadoEjemplar)
                ? EstadoEjemplar.Disponible
                : estadoEjemplar;
            if (!EstadosDevolucionValidos.Contains(estadoFinal))
                return ResultadoOperacion<ResultadoDevolucion>.Falla("El estado del ejemplar no es válido");

            var ahora = DateTime.Now;

            await using var tx = await _context.Database.BeginTransactionAsync();

            // "Reclama" la devolución con un UPDATE condicional: si el formulario se envía
            // dos veces (doble clic, recarga), solo una petición pasa y no se duplica la sanción.
            int reclamados = await _context.Prestamos
                .Where(p => p.IdPrestamo == idPrestamo && p.EstadoPrestamo != EstadoPrestamo.Devuelto)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.EstadoPrestamo, EstadoPrestamo.Devuelto)
                    .SetProperty(p => p.FechaDevolucionReal, (DateTime?)ahora));

            if (reclamados == 0)
            {
                bool existe = await _context.Prestamos.AnyAsync(p => p.IdPrestamo == idPrestamo);
                return ResultadoOperacion<ResultadoDevolucion>.Falla(
                    existe ? "Este préstamo ya fue devuelto" : "Préstamo no encontrado");
            }

            // Se carga después del UPDATE para trabajar con el estado ya confirmado.
            var prestamo = await _context.Prestamos
                .Include(p => p.Usuario)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .FirstAsync(p => p.IdPrestamo == idPrestamo);

            var (diasRetraso, multa) = await CalcularRetrasoAsync(prestamo.FechaDevolucionEsperada, ahora);

            prestamo.DiasRetraso = diasRetraso;
            prestamo.Ejemplar.Estado = estadoFinal;

            if (!string.IsNullOrWhiteSpace(observaciones))
            {
                prestamo.Observaciones = AnexarNota(prestamo.Observaciones, $"[Devolución] {observaciones}");
                prestamo.Ejemplar.Observaciones = AnexarNota(
                    prestamo.Ejemplar.Observaciones, $"[{ahora:dd/MM/yyyy}] {observaciones}");
            }

            Sancion? sancion = null;
            if (diasRetraso > 0)
            {
                sancion = new Sancion
                {
                    IdUsuario = prestamo.IdUsuario,
                    IdPrestamo = prestamo.IdPrestamo,
                    TipoSancion = "Retraso",
                    Monto = multa,
                    FechaSancion = ahora,
                    EstadoSancion = EstadoSancion.Pendiente,
                    Descripcion = Recortar(
                        $"Sanción por {diasRetraso} días de retraso en la devolución del libro '{prestamo.Ejemplar.Libro.Titulo}'",
                        MaxObservaciones)
                };
                _context.Sanciones.Add(sancion);
            }

            // Devolución + ejemplar + sanción se confirman juntos o no se confirma nada.
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return ResultadoOperacion<ResultadoDevolucion>.Ok(
                new ResultadoDevolucion(prestamo, diasRetraso, multa, sancion));
        }

        public Task<(int DiasRetraso, decimal Multa)> CalcularRetrasoAsync(DateTime fechaEsperada) =>
            CalcularRetrasoAsync(fechaEsperada, DateTime.Now);

        // ------------------------------------------------------------------
        // AUXILIARES
        // ------------------------------------------------------------------

        // Se cuentan días completos de retraso; una devolución dentro del plazo no genera multa.
        private async Task<(int DiasRetraso, decimal Multa)> CalcularRetrasoAsync(DateTime fechaEsperada, DateTime ahora)
        {
            int dias = Math.Max(0, (ahora - fechaEsperada).Days);
            if (dias == 0) return (0, 0m);

            decimal multaDiaria = await ObtenerDecimalAsync("MultaDiaria", 2.00m);
            return (dias, multaDiaria * dias);
        }

        // Los parámetros viven en BD como texto: se parsea con cultura invariante y se cae
        // al valor por defecto si falta o está mal escrito, en vez de reventar con FormatException.
        private async Task<int> ObtenerEnteroAsync(string parametro, int porDefecto)
        {
            string? valor = await LeerParametroAsync(parametro);
            return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
                ? n
                : porDefecto;
        }

        private async Task<decimal> ObtenerDecimalAsync(string parametro, decimal porDefecto)
        {
            string? valor = await LeerParametroAsync(parametro);
            return decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) && d >= 0
                ? d
                : porDefecto;
        }

        private Task<string?> LeerParametroAsync(string parametro) =>
            _context.ConfiguracionSistema
                .AsNoTracking()
                .Where(c => c.Parametro == parametro)
                .Select(c => c.Valor)
                .FirstOrDefaultAsync();

        // Agrega una línea a un campo de texto con límite de longitud. Si no cabe, descarta
        // lo más antiguo: lo reciente es lo relevante y evita un error de truncamiento en SQL.
        private static string AnexarNota(string? actual, string nota)
        {
            string combinado = string.IsNullOrWhiteSpace(actual) ? nota : $"{actual}\n{nota}";
            return combinado.Length <= MaxObservaciones ? combinado : combinado[^MaxObservaciones..];
        }

        private static string Recortar(string? texto, int max) =>
            string.IsNullOrEmpty(texto) ? string.Empty : (texto.Length <= max ? texto : texto[..max]);
    }
}
