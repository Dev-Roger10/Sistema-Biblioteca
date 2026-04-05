using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace SistemaBiblioteca.Controllers
{
    public class PrestamosController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<PrestamosController> _logger;

        public PrestamosController(BibliotecaContext context, ILogger<PrestamosController> logger)
        {
            _context = context;
            _logger = logger;
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
                    "activos" => prestamos.Where(p => p.EstadoPrestamo == "Activo"),
                    "vencidos" => prestamos.Where(p => p.EstadoPrestamo == "Activo" && DateTime.Now > p.FechaDevolucionEsperada),
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

            // Actualizar estados vencidos
            var vencidos = resultado.Where(p => p.EstadoPrestamo == "Activo" && DateTime.Now > p.FechaDevolucionEsperada);
            foreach (var prestamo in vencidos)
            {
                if (prestamo.EstadoPrestamo != "Vencido")
                {
                    prestamo.EstadoPrestamo = "Vencido";
                    prestamo.DiasRetraso = (DateTime.Now - prestamo.FechaDevolucionEsperada).Days;
                }
            }
            await _context.SaveChangesAsync();

            ViewBag.Filtro = filtro;
            ViewBag.PrestamosActivos = resultado.Count(p => p.EstadoPrestamo == "Activo");
            ViewBag.PrestamosVencidos = resultado.Count(p => p.EstadoPrestamo == "Vencido" || (p.EstadoPrestamo == "Activo" && p.EstaVencido));

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

            ViewBag.PrestamosActivos = prestamos.Count(p => p.EstadoPrestamo == "Activo");
            ViewBag.PrestamosVencidos = prestamos.Count(p => p.EstaVencido && p.EstadoPrestamo == "Activo");

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
                // Verificar que el ejemplar esté disponible
                var ejemplar = await _context.Ejemplares
                    .Include(e => e.Libro)
                    .FirstOrDefaultAsync(e => e.IdEjemplar == idEjemplar);

                if (ejemplar == null || ejemplar.Estado != "Disponible")
                {
                    TempData["Error"] = "El ejemplar no está disponible";
                    return RedirectToAction(nameof(Create));
                }

                // Obtener configuración de días según rol
                var usuario = await _context.Usuarios.Include(u => u.Rol).FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);
                if (usuario == null)
                {
                    TempData["Error"] = "Usuario no encontrado";
                    return RedirectToAction(nameof(Create));
                }

                int diasPrestamo = 14; // Por defecto
                if (usuario.Rol.NombreRol == "Docente")
                {
                    var configDocente = await _context.ConfiguracionSistema
                        .FirstOrDefaultAsync(c => c.Parametro == "DiasMaximoPrestamoDocente");
                    diasPrestamo = int.Parse(configDocente?.Valor ?? "30");
                }
                else
                {
                    var config = await _context.ConfiguracionSistema
                        .FirstOrDefaultAsync(c => c.Parametro == "DiasMaximoPrestamo");
                    diasPrestamo = int.Parse(config?.Valor ?? "14");
                }

                // Verificar límite de préstamos
                var prestamosActivos = await _context.Prestamos
                    .CountAsync(p => p.IdUsuario == idUsuario && p.EstadoPrestamo == "Activo");

                var limitePrestamos = await _context.ConfiguracionSistema
                    .FirstOrDefaultAsync(c => c.Parametro == "LimitePrestamos");
                int limite = int.Parse(limitePrestamos?.Valor ?? "5");

                if (prestamosActivos >= limite)
                {
                    TempData["Error"] = $"El usuario ha alcanzado el límite de {limite} préstamos simultáneos";
                    return RedirectToAction(nameof(Create));
                }

                // Verificar sanciones pendientes
                var sancionesPendientes = await _context.Sanciones
                    .AnyAsync(s => s.IdUsuario == idUsuario && s.EstadoSancion == "Pendiente");

                if (sancionesPendientes)
                {
                    TempData["Warning"] = "Advertencia: El usuario tiene sanciones pendientes";
                }

                // Crear préstamo
                var prestamo = new Prestamo
                {
                    IdUsuario = idUsuario,
                    IdEjemplar = idEjemplar,
                    IdReserva = idReserva,
                    FechaPrestamo = DateTime.Now,
                    FechaDevolucionEsperada = DateTime.Now.AddDays(diasPrestamo),
                    EstadoPrestamo = "Activo",
                    Observaciones = observaciones
                };

                _context.Prestamos.Add(prestamo);

                // Actualizar estado del ejemplar
                ejemplar.Estado = "Prestado";

                // Si viene de una reserva, actualizarla
                if (idReserva.HasValue)
                {
                    var reserva = await _context.Reservas.FindAsync(idReserva.Value);
                    if (reserva != null)
                    {
                        reserva.EstadoReserva = "Confirmada";
                    }
                }

                await _context.SaveChangesAsync();

                // Enviar notificación al usuario
                await EnviarNotificacion(idUsuario, "Préstamo registrado",
                    $"Se ha registrado el préstamo del libro '{ejemplar.Libro.Titulo}'. Fecha de devolución: {prestamo.FechaDevolucionEsperada:dd/MM/yyyy}");

                await RegistrarAccion("Crear préstamo", "Prestamos", prestamo.IdPrestamo,
                    $"Préstamo creado para usuario {usuario.NombreCompleto}, libro '{ejemplar.Libro.Titulo}'");

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

            if (prestamo.EstadoPrestamo != "Activo" && prestamo.EstadoPrestamo != "Vencido")
            {
                TempData["Error"] = "Este préstamo ya fue devuelto";
                return RedirectToAction(nameof(Index));
            }

            // Calcular días de retraso y multa
            int diasRetraso = 0;
            decimal multa = 0;

            if (DateTime.Now > prestamo.FechaDevolucionEsperada)
            {
                diasRetraso = (DateTime.Now - prestamo.FechaDevolucionEsperada).Days;
                var multaDiaria = await _context.ConfiguracionSistema
                    .FirstOrDefaultAsync(c => c.Parametro == "MultaDiaria");
                decimal multaPorDia = decimal.Parse(multaDiaria?.Valor ?? "2.00");
                multa = multaPorDia * diasRetraso;
            }

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
                var prestamo = await _context.Prestamos
                    .Include(p => p.Usuario)
                    .Include(p => p.Ejemplar).ThenInclude(e => e.Libro)
                    .FirstOrDefaultAsync(p => p.IdPrestamo == id);

                if (prestamo == null)
                {
                    TempData["Error"] = "Préstamo no encontrado";
                    return RedirectToAction(nameof(Index));
                }

                // Calcular días de retraso
                int diasRetraso = 0;
                if (DateTime.Now > prestamo.FechaDevolucionEsperada)
                {
                    diasRetraso = (DateTime.Now - prestamo.FechaDevolucionEsperada).Days;
                }

                // Actualizar préstamo
                prestamo.FechaDevolucionReal = DateTime.Now;
                prestamo.DiasRetraso = diasRetraso;
                prestamo.EstadoPrestamo = "Devuelto";
                prestamo.Observaciones += $"\n[Devolución] {observacionesDevolucion}";

                // Actualizar estado del ejemplar
                prestamo.Ejemplar.Estado = estadoEjemplar ?? "Disponible";
                if (!string.IsNullOrEmpty(observacionesDevolucion))
                {
                    prestamo.Ejemplar.Observaciones += $"\n[{DateTime.Now:dd/MM/yyyy}] {observacionesDevolucion}";
                }

                await _context.SaveChangesAsync();

                // Si hay retraso, crear sanción
                if (diasRetraso > 0)
                {
                    var multaDiaria = await _context.ConfiguracionSistema
                        .FirstOrDefaultAsync(c => c.Parametro == "MultaDiaria");
                    decimal multaPorDia = decimal.Parse(multaDiaria?.Valor ?? "2.00");
                    decimal multa = multaPorDia * diasRetraso;

                    var sancion = new Sancion
                    {
                        IdUsuario = prestamo.IdUsuario,
                        IdPrestamo = prestamo.IdPrestamo,
                        TipoSancion = "Retraso",
                        Monto = multa,
                        FechaSancion = DateTime.Now,
                        EstadoSancion = "Pendiente",
                        Descripcion = $"Sanción por {diasRetraso} días de retraso en la devolución del libro '{prestamo.Ejemplar.Libro.Titulo}'"
                    };

                    _context.Sanciones.Add(sancion);
                    await _context.SaveChangesAsync();

                    // Notificar sanción
                    await EnviarNotificacion(prestamo.IdUsuario, "Sanción por retraso",
                        $"Se ha generado una sanción de S/{multa:F2} por {diasRetraso} días de retraso en la devolución del libro '{prestamo.Ejemplar.Libro.Titulo}'");
                }
                else
                {
                    // Notificar devolución exitosa
                    await EnviarNotificacion(prestamo.IdUsuario, "Devolución registrada",
                        $"Se ha registrado la devolución del libro '{prestamo.Ejemplar.Libro.Titulo}'. Gracias por devolver a tiempo.");
                }

                await RegistrarAccion("Registrar devolución", "Prestamos", prestamo.IdPrestamo,
                    $"Devolución registrada. Días de retraso: {diasRetraso}. Libro: '{prestamo.Ejemplar.Libro.Titulo}'");

                string mensaje = diasRetraso > 0
                    ? $"Devolución registrada con {diasRetraso} días de retraso. Sanción generada."
                    : "Devolución registrada correctamente";

                TempData["Success"] = mensaje;
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