using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using SistemaBiblioteca.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace SistemaBiblioteca.Controllers
{
    public class PrestamosController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<PrestamosController> _logger;
        private readonly IPrestamoService _prestamos;

        public PrestamosController(
            BibliotecaContext context,
            ILogger<PrestamosController> logger,
            IPrestamoService prestamos)
        {
            _context = context;
            _logger = logger;
            _prestamos = prestamos;
        }

        // LISTADO DE PRÉSTAMOS

        // GET: Index (Lista de préstamos)
        public async Task<IActionResult> Index(string filtro, string buscar, DateTime? fechaDesde, DateTime? fechaHasta)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("Index", "Home");
            }

            var prestamos = _context.Prestamos
                .Include(p => p.Usuario)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Sede)
                .AsQueryable();

            // Filtros
            if (!string.IsNullOrEmpty(filtro))
            {
                prestamos = filtro switch
                {
                    "activos" => prestamos.Where(p => p.EstadoPrestamo == EstadoPrestamo.Activo),
                    // "Vencido" = sigue en poder del usuario y pasó la fecha. Se calcula por fecha;
                    // el valor "Vencido" guardado solo existe en registros antiguos.
                    "vencidos" => prestamos.Where(p =>
                        (p.EstadoPrestamo == EstadoPrestamo.Activo || p.EstadoPrestamo == EstadoPrestamo.Vencido) &&
                        DateTime.Now > p.FechaDevolucionEsperada),
                    "devueltos" => prestamos.Where(p => p.EstadoPrestamo == "Devuelto"),
                    _ => prestamos
                };
            }

            if (!string.IsNullOrEmpty(buscar))
            {
                prestamos = prestamos.Where(p =>
                    p.Usuario.Nombres.Contains(buscar) ||
                    p.Usuario.Apellidos.Contains(buscar) ||
                    p.Usuario.DNI.Contains(buscar) ||
                    p.Ejemplar.Libro.Titulo.Contains(buscar) ||
                    p.Ejemplar.CodigoEjemplar.Contains(buscar));
            }

            if (fechaDesde.HasValue)
            {
                prestamos = prestamos.Where(p => p.FechaPrestamo >= fechaDesde.Value);
            }

            if (fechaHasta.HasValue)
            {
                prestamos = prestamos.Where(p => p.FechaPrestamo <= fechaHasta.Value.AddDays(1));
            }

            var resultado = await prestamos
                .OrderByDescending(p => p.FechaPrestamo)
                .ToListAsync();

            // Un GET no debe modificar datos: el vencimiento se deriva de la fecha
            // (Prestamo.EstaVencido) y no se persiste. Antes se guardaba aquí, lo que
            // además sacaba al préstamo del conteo de "Activo" y burlaba el límite.
            ViewBag.Filtro = filtro;
            ViewBag.Buscar = buscar;
            ViewBag.FechaDesde = fechaDesde?.ToString("yyyy-MM-dd");
            ViewBag.FechaHasta = fechaHasta?.ToString("yyyy-MM-dd");

            // Los totales son de TODA la tabla: no deben cambiar al aplicar filtros
            // (antes se contaban sobre el resultado filtrado y salían cifras engañosas).
            var ahora = DateTime.Now;
            ViewBag.PrestamosActivos = await _context.Prestamos
                .CountAsync(p => p.FechaDevolucionReal == null);
            ViewBag.PrestamosVencidos = await _context.Prestamos
                .CountAsync(p => p.FechaDevolucionReal == null && p.FechaDevolucionEsperada < ahora);
            ViewBag.PrestamosDevueltos = await _context.Prestamos
                .CountAsync(p => p.EstadoPrestamo == EstadoPrestamo.Devuelto);

            return View(resultado);
        }

        // GET: MisPrestamos (Vista de usuario)
        public async Task<IActionResult> MisPrestamos()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var prestamos = await _context.Prestamos
                .Include(p => p.Ejemplar).ThenInclude(e => e.Libro).ThenInclude(l => l.Categoria)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Sede)
                .Where(p => p.IdUsuario == userId)
                .OrderByDescending(p => p.FechaPrestamo)
                .ToListAsync();

            ViewBag.PrestamosActivos = prestamos.Count(p => p.EstadoPrestamo != EstadoPrestamo.Devuelto);
            ViewBag.PrestamosVencidos = prestamos.Count(p => p.EstadoPrestamo != EstadoPrestamo.Devuelto && p.EstaVencido);

            return View(prestamos);
        }

        // =============================================
        // CREAR PRÉSTAMO
        // =============================================

        // GET: Create
        public async Task<IActionResult> Create(int? idReserva)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            ViewBag.Usuarios = await _context.Usuarios
                .Include(u => u.Rol)
                .Where(u => u.Activo)
                .OrderBy(u => u.Apellidos)
                .ToListAsync();

            if (idReserva.HasValue)
            {
                var reserva = await _context.Reservas
                    .Include(r => r.Libro)
                    .Include(r => r.Usuario)
                    .FirstOrDefaultAsync(r => r.IdReserva == idReserva.Value);

                if (reserva != null)
                {
                    ViewBag.ReservaSeleccionada = reserva;
                    ViewBag.EjemplaresDisponibles = await _context.Ejemplares
                        .Include(e => e.Sede)
                        .Include(e => e.Libro)
                        .Where(e => e.IdLibro == reserva.IdLibro && e.Estado == "Disponible" && e.Activo)
                        .ToListAsync();
                }
            }
            else
            {
                // Cargar todos los ejemplares disponibles para crear préstamo manual
                ViewBag.EjemplaresDisponibles = await _context.Ejemplares
                    .Include(e => e.Libro)
                    .Include(e => e.Sede)
                    .Where(e => e.Estado == "Disponible" && e.Activo)
                    .OrderBy(e => e.Libro.Titulo)
                    .ToListAsync();
            }

            return View();
        }

        // POST: Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int idUsuario, int idEjemplar, int? idReserva, string observaciones)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                // Todas las reglas (disponibilidad, límite, reserva, atomicidad) viven en el servicio.
                var resultado = await _prestamos.RegistrarPrestamoAsync(idUsuario, idEjemplar, idReserva, observaciones);

                if (!resultado.Exito)
                {
                    TempData["Error"] = resultado.Error;
                    return idReserva.HasValue
                        ? RedirectToAction(nameof(Create), new { idReserva })
                        : RedirectToAction(nameof(Create));
                }

                var prestamo = resultado.Valor!;
                string titulo = prestamo.Ejemplar.Libro.Titulo;

                if (resultado.Advertencia != null)
                    TempData["Warning"] = resultado.Advertencia;

                // Efectos secundarios: el préstamo ya está confirmado; si fallan no lo deshacen.
                await EnviarNotificacion(idUsuario, "Préstamo registrado",
                    $"Se ha registrado el préstamo del libro '{titulo}'. Fecha de devolución: {prestamo.FechaDevolucionEsperada:dd/MM/yyyy}");

                await RegistrarAccion("Crear préstamo", "Prestamos", prestamo.IdPrestamo,
                    $"Préstamo creado para usuario {prestamo.Usuario.NombreCompleto}, libro '{titulo}'");

                TempData["Success"] = $"Préstamo registrado correctamente. Devolución: {prestamo.FechaDevolucionEsperada:dd/MM/yyyy}";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Create Prestamo");
                TempData["Error"] = "Error al registrar el préstamo";
                return RedirectToAction(nameof(Create));
            }
        }

        // =============================================
        // REGISTRAR DEVOLUCIÓN
        // =============================================

        // GET: Devolver
        public async Task<IActionResult> Devolver(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var prestamo = await _context.Prestamos
                .Include(p => p.Usuario)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(p => p.Ejemplar).ThenInclude(e => e.Sede)
                .FirstOrDefaultAsync(p => p.IdPrestamo == id);

            if (prestamo == null) return NotFound();

            if (prestamo.EstadoPrestamo == EstadoPrestamo.Devuelto)
            {
                TempData["Error"] = "Este préstamo ya fue devuelto";
                return RedirectToAction(nameof(Index));
            }

            // Vista previa de retraso y multa (la devolución real la recalcula el servicio).
            var (diasRetraso, multa) = await _prestamos.CalcularRetrasoAsync(prestamo.FechaDevolucionEsperada);

            ViewBag.DiasRetraso = diasRetraso;
            ViewBag.Multa = multa;

            return View(prestamo);
        }

        // POST: Devolver
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Devolver(int id, string observacionesDevolucion, string estadoEjemplar)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                var resultado = await _prestamos.RegistrarDevolucionAsync(id, observacionesDevolucion, estadoEjemplar);

                if (!resultado.Exito)
                {
                    TempData["Error"] = resultado.Error;
                    return RedirectToAction(nameof(Index));
                }

                var devolucion = resultado.Valor!;
                var prestamo = devolucion.Prestamo;
                string titulo = prestamo.Ejemplar.Libro.Titulo;

                // Efectos secundarios: la devolución y la sanción ya están confirmadas.
                if (devolucion.Sancion != null)
                {
                    await EnviarNotificacion(prestamo.IdUsuario, "Sanción por retraso",
                        $"Se ha generado una sanción de S/{devolucion.Multa:F2} por {devolucion.DiasRetraso} días de retraso en la devolución del libro '{titulo}'");
                }
                else
                {
                    await EnviarNotificacion(prestamo.IdUsuario, "Devolución registrada",
                        $"Se ha registrado la devolución del libro '{titulo}'. Gracias por devolver a tiempo.");
                }

                await RegistrarAccion("Registrar devolución", "Prestamos", prestamo.IdPrestamo,
                    $"Devolución registrada. Días de retraso: {devolucion.DiasRetraso}. Libro: '{titulo}'");

                TempData["Success"] = devolucion.DiasRetraso > 0
                    ? $"Devolución registrada con {devolucion.DiasRetraso} días de retraso. Sanción generada."
                    : "Devolución registrada correctamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Devolver");
                TempData["Error"] = "Error al registrar la devolución";
                return RedirectToAction(nameof(Devolver), new { id });
            }
        }

        // =============================================
        // BUSCAR EJEMPLARES DISPONIBLES
        // =============================================

        [HttpGet]
        public async Task<IActionResult> BuscarEjemplaresDisponibles(string buscar)
        {
            if (!ValidarSesion()) return Unauthorized();

            var ejemplares = await _context.Ejemplares
                .Include(e => e.Libro).ThenInclude(l => l.Categoria)
                .Include(e => e.Sede)
                .Where(e => e.Activo && e.Estado == "Disponible" &&
                    (e.Libro.Titulo.Contains(buscar) ||
                     e.Libro.Autor.Contains(buscar) ||
                     e.CodigoEjemplar.Contains(buscar) ||
                     e.Libro.ISBN.Contains(buscar)))
                .Select(e => new
                {
                    e.IdEjemplar,
                    e.CodigoEjemplar,
                    Libro = e.Libro.Titulo,
                    Autor = e.Libro.Autor,
                    Categoria = e.Libro.Categoria.NombreCategoria,
                    Sede = e.Sede.NombreSede,
                    e.Estado
                })
                .Take(10)
                .ToListAsync();

            return Json(ejemplares);
        }

        // MÉTODOS AUXILIARES
  
        private bool ValidarSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioId") != null;
        }

        private bool ValidarRol(string[] rolesPermitidos)
        {
            string rolActual = HttpContext.Session.GetString("UsuarioRol");
            return rolActual != null && rolesPermitidos.Contains(rolActual);
        }

        private async Task EnviarNotificacion(int usuarioId, string asunto, string mensaje)
        {
            try
            {
                var notificacion = new Notificacion
                {
                    IdUsuario = usuarioId,
                    TipoNotificacion = "Préstamo",
                    Asunto = asunto,
                    Mensaje = mensaje,
                    FechaEnvio = DateTime.Now,
                    Leida = false
                };

                _context.Notificaciones.Add(notificacion);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar notificación");
            }
        }

        private async Task RegistrarAccion(string accion, string tabla, int? registroId, string detalles)
        {
            try
            {
                int userId = HttpContext.Session.GetInt32("UsuarioId") ?? 0;
                if (userId == 0) return;

                var historial = new HistorialAccion
                {
                    IdUsuario = userId,
                    Accion = accion,
                    TablaAfectada = tabla,
                    RegistroAfectado = registroId,
                    Detalles = detalles,
                    FechaAccion = DateTime.Now
                };

                _context.HistorialAcciones.Add(historial);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al registrar acción");
            }
        }
    }
}