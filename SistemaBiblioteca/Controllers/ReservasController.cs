using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Controllers
{
    public class ReservasController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<ReservasController> _logger;

        public ReservasController(BibliotecaContext context, ILogger<ReservasController> logger)
        {
            _context = context;
            _logger = logger;
        }


        // LISTADO DE RESERVAS

        // GET: Index 
        public async Task<IActionResult> Index(string filtro, string buscar)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("MisReservas");
            }

            var reservas = _context.Reservas
                .Include(r => r.Usuario)
                .Include(r => r.Libro).ThenInclude(l => l.Categoria)
                .AsQueryable();

            // Filtros
            if (!string.IsNullOrEmpty(filtro))
            {
                reservas = filtro switch
                {
                    "pendientes" => reservas.Where(r => r.EstadoReserva == "Pendiente"),
                    "confirmadas" => reservas.Where(r => r.EstadoReserva == "Confirmada"),
                    "canceladas" => reservas.Where(r => r.EstadoReserva == "Cancelada"),
                    "vencidas" => reservas.Where(r => r.EstadoReserva == "Vencida"),
                    _ => reservas
                };
            }

            if (!string.IsNullOrEmpty(buscar))
            {
                reservas = reservas.Where(r =>
                    r.Usuario.Nombres.Contains(buscar) ||
                    r.Usuario.Apellidos.Contains(buscar) ||
                    r.Usuario.DNI.Contains(buscar) ||
                    r.Libro.Titulo.Contains(buscar));
            }

            var resultado = await reservas
                .OrderByDescending(r => r.FechaReserva)
                .ToListAsync();

            // Actualizar reservas vencidas
            await ActualizarReservasVencidas();

            ViewBag.Filtro = filtro;
            ViewBag.ReservasPendientes = resultado.Count(r => r.EstadoReserva == "Pendiente");
            ViewBag.ReservasVencidas = resultado.Count(r => r.EstadoReserva == "Vencida");

            return View(resultado);
        }

        // GET: MisReservas (Para usuarios)
        public async Task<IActionResult> MisReservas()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var reservas = await _context.Reservas
                .Include(r => r.Libro).ThenInclude(l => l.Categoria)
                .Include(r => r.Libro).ThenInclude(l => l.Editorial)
                .Where(r => r.IdUsuario == userId)
                .OrderByDescending(r => r.FechaReserva)
                .ToListAsync();

            await ActualizarReservasVencidas();

            ViewBag.ReservasPendientes = reservas.Count(r => r.EstadoReserva == "Pendiente");

            return View(reservas);
        }

        // =============================================
        // CREAR RESERVA
        // =============================================

        // POST: Create (Desde el catálogo)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int idLibro, string tipoReserva)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            try
            {
                int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

                // Verificar que el libro exista y esté activo
                var libro = await _context.Libros
                    .FirstOrDefaultAsync(l => l.IdLibro == idLibro && l.Activo);

                if (libro == null)
                {
                    _logger.LogWarning($"Libro no encontrado: {idLibro}");
                    TempData["Error"] = "Libro no encontrado";
                    return RedirectToAction("Catalogo", "Libros");
                }

                // Verificar límite de reservas
                var reservasActivas = await _context.Reservas
                    .CountAsync(r => r.IdUsuario == userId && r.EstadoReserva == "Pendiente");

                var limiteReservas = await _context.ConfiguracionSistema
                    .FirstOrDefaultAsync(c => c.Parametro == "LimiteReservas");
                int limite = int.Parse(limiteReservas?.Valor ?? "3");

                if (reservasActivas >= limite)
                {
                    _logger.LogWarning($"Usuario {userId} alcanzó límite de reservas");
                    TempData["Error"] = $"Ha alcanzado el límite de {limite} reservas simultáneas";
                    return RedirectToAction("DetalleLibro", "Libros", new { id = idLibro });
                }

                // Verificar que no tenga ya una reserva del mismo libro
                var reservaExistente = await _context.Reservas
                    .AnyAsync(r => r.IdUsuario == userId && r.IdLibro == idLibro && r.EstadoReserva == "Pendiente");

                if (reservaExistente)
                {
                    _logger.LogWarning($"Usuario {userId} ya tiene reserva pendiente de libro {idLibro}");
                    TempData["Error"] = "Ya tiene una reserva pendiente de este libro";
                    return RedirectToAction("DetalleLibro", "Libros", new { id = idLibro });
                }

                // Verificar sanciones pendientes
                var sancionesPendientes = await _context.Sanciones
                    .AnyAsync(s => s.IdUsuario == userId && s.EstadoSancion == "Pendiente");

                if (sancionesPendientes)
                {
                    _logger.LogWarning($"Usuario {userId} tiene sanciones pendientes");
                    TempData["Error"] = "No puede realizar reservas mientras tenga sanciones pendientes de pago";
                    return RedirectToAction("DetalleLibro", "Libros", new { id = idLibro });
                }

                // Obtener días de validez de la reserva
                var diasValidez = await _context.ConfiguracionSistema
                    .FirstOrDefaultAsync(c => c.Parametro == "DiasValidezReserva");
                int dias = int.Parse(diasValidez?.Valor ?? "3");

                // Crear reserva
                var reserva = new Reserva
                {
                    IdUsuario = userId,
                    IdLibro = idLibro,
                    TipoReserva = tipoReserva ?? "Local",
                    FechaReserva = DateTime.Now,
                    FechaVencimiento = DateTime.Now.AddDays(dias),
                    EstadoReserva = "Pendiente",
                    Observaciones = ""
                };

                _context.Reservas.Add(reserva);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Reserva creada: {reserva.IdReserva} para usuario {userId}, libro {idLibro}");

                // Enviar notificación
                await EnviarNotificacion(userId, "Reserva registrada",
                    $"Su reserva del libro '{libro.Titulo}' ha sido registrada. Válida hasta: {reserva.FechaVencimiento:dd/MM/yyyy}. Tipo: {tipoReserva}");

                await RegistrarAccion("Crear reserva", "Reservas", reserva.IdReserva,
                    $"Reserva creada para libro '{libro.Titulo}', tipo: {tipoReserva}");

                TempData["Success"] = $"✅ Reserva registrada correctamente. Válida hasta: {reserva.FechaVencimiento:dd/MM/yyyy}";
                return RedirectToAction("MisReservas");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "❌ Error de BD al crear reserva");
                _logger.LogError(dbEx.InnerException, "Inner Exception: ");
                TempData["Error"] = $"❌ Error de base de datos: {dbEx.InnerException?.Message ?? dbEx.Message}";
                return RedirectToAction("DetalleLibro", "Libros", new { id = idLibro });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error en Create Reserva. IdLibro: {idLibro}, TipoReserva: {tipoReserva}");
                TempData["Error"] = $"❌ Error al crear la reserva: {ex.Message}";
                return RedirectToAction("DetalleLibro", "Libros", new { id = idLibro });
            }
        }

        // =============================================
        // CONFIRMAR RESERVA (Bibliotecario)
        // =============================================

        // GET: Confirmar
        public async Task<IActionResult> Confirmar(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("MisReservas");
            }

            if (id == null) return NotFound();

            var reserva = await _context.Reservas
                .Include(r => r.Usuario)
                .Include(r => r.Libro)
                .FirstOrDefaultAsync(r => r.IdReserva == id);

            if (reserva == null) return NotFound();

            if (reserva.EstadoReserva != "Pendiente")
            {
                TempData["Error"] = "Esta reserva no está pendiente";
                return RedirectToAction(nameof(Index));
            }

            // Buscar ejemplares disponibles del libro
            ViewBag.EjemplaresDisponibles = await _context.Ejemplares
                .Include(e => e.Sede)
                .Where(e => e.IdLibro == reserva.IdLibro && e.Estado == "Disponible" && e.Activo)
                .ToListAsync();

            return View(reserva);
        }

        // POST: ConfirmarYPrestar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarYPrestar(int idReserva, int idEjemplar)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                var reserva = await _context.Reservas
                    .Include(r => r.Usuario).ThenInclude(u => u.Rol)
                    .Include(r => r.Libro)
                    .FirstOrDefaultAsync(r => r.IdReserva == idReserva);

                if (reserva == null)
                {
                    TempData["Error"] = "Reserva no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                // Verificar que el ejemplar esté disponible
                var ejemplar = await _context.Ejemplares
                    .FirstOrDefaultAsync(e => e.IdEjemplar == idEjemplar && e.IdLibro == reserva.IdLibro);

                if (ejemplar == null || ejemplar.Estado != "Disponible")
                {
                    TempData["Error"] = "El ejemplar no está disponible";
                    return RedirectToAction(nameof(Confirmar), new { id = idReserva });
                }

                // Obtener días de préstamo según rol
                int diasPrestamo = 14;
                if (reserva.Usuario.Rol.NombreRol == "Docente")
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

                // Crear préstamo
                var prestamo = new Prestamo
                {
                    IdUsuario = reserva.IdUsuario,
                    IdEjemplar = idEjemplar,
                    IdReserva = idReserva,
                    FechaPrestamo = DateTime.Now,
                    FechaDevolucionEsperada = DateTime.Now.AddDays(diasPrestamo),
                    EstadoPrestamo = "Activo",
                    Observaciones = $"Generado desde reserva #{idReserva}"
                };

                _context.Prestamos.Add(prestamo);

                // Actualizar reserva
                reserva.EstadoReserva = "Confirmada";

                // Actualizar ejemplar
                ejemplar.Estado = "Prestado";

                await _context.SaveChangesAsync();

                // Enviar notificación
                await EnviarNotificacion(reserva.IdUsuario, "Reserva confirmada - Préstamo registrado",
                    $"Su reserva del libro '{reserva.Libro.Titulo}' ha sido confirmada y el préstamo ha sido registrado. Fecha de devolución: {prestamo.FechaDevolucionEsperada:dd/MM/yyyy}");

                await RegistrarAccion("Confirmar reserva y crear préstamo", "Reservas", idReserva,
                    $"Reserva confirmada y préstamo #{prestamo.IdPrestamo} creado para '{reserva.Libro.Titulo}'");

                TempData["Success"] = $"Reserva confirmada y préstamo registrado. Devolución: {prestamo.FechaDevolucionEsperada:dd/MM/yyyy}";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en ConfirmarYPrestar");
                TempData["Error"] = "Error al confirmar la reserva";
                return RedirectToAction(nameof(Confirmar), new { id = idReserva });
            }
        }

        // =============================================
        // CANCELAR RESERVA
        // =============================================

        // POST: Cancelar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int id, string motivo = "Cancelada por administrador")
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            try
            {
                var reserva = await _context.Reservas
   .Include(r => r.Usuario)
       .Include(r => r.Libro)
             .FirstOrDefaultAsync(r => r.IdReserva == id);

                if (reserva == null)
      {
    _logger.LogWarning($"Reserva no encontrada: {id}");
                TempData["Error"] = "❌ Reserva no encontrada";
     return RedirectToAction(nameof(Index));
    }

    int userId = HttpContext.Session.GetInt32("UsuarioId").Value;
        string rol = HttpContext.Session.GetString("UsuarioRol");

   // Verificar permisos
         if (reserva.IdUsuario != userId && rol != "Administrador" && rol != "Bibliotecario")
        {
           _logger.LogWarning($"Usuario {userId} sin permisos para cancelar reserva {id}");
   TempData["Error"] = "❌ No tiene permisos para cancelar esta reserva";
        return RedirectToAction(nameof(Index));
      }

   if (reserva.EstadoReserva != "Pendiente")
 {
 _logger.LogWarning($"Intento de cancelar reserva {id} con estado {reserva.EstadoReserva}");
      TempData["Error"] = $"❌ Esta reserva no puede ser cancelada (Estado: {reserva.EstadoReserva})";
 return RedirectToAction(nameof(Index));
                }

// Cancelar reserva
 reserva.EstadoReserva = "Cancelada";
 reserva.Observaciones = $"Cancelada: {motivo}";

      await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Reserva {id} cancelada por usuario {userId}");

// Notificar
      await EnviarNotificacion(reserva.IdUsuario, "Reserva cancelada",
    $"Su reserva del libro '{reserva.Libro.Titulo}' ha sido cancelada. Motivo: {motivo}");

 await RegistrarAccion("Cancelar reserva", "Reservas", id,
 $"Reserva #{id} cancelada. Motivo: {motivo}");

    TempData["Success"] = "✅ Reserva cancelada correctamente";
     return RedirectToAction(nameof(Index));
            }
     catch (Exception ex)
      {
                _logger.LogError(ex, $"❌ Error en Cancelar Reserva {id}");
         TempData["Error"] = $"❌ Error al cancelar la reserva: {ex.Message}";
  return RedirectToAction(nameof(Index));
}
        }

        // MÉTODOS AUXILIARES

        private async Task ActualizarReservasVencidas()
        {
            try
            {
                var reservasVencidas = await _context.Reservas
                    .Where(r => r.EstadoReserva == "Pendiente" && r.FechaVencimiento < DateTime.Now)
                    .ToListAsync();

                foreach (var reserva in reservasVencidas)
                {
                    reserva.EstadoReserva = "Vencida";
                }

                if (reservasVencidas.Any())
                {
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar reservas vencidas");
            }
        }

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
                    TipoNotificacion = "Reserva",
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