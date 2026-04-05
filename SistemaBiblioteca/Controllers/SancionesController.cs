using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Controllers
{
    public class SancionesController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<SancionesController> _logger;

        public SancionesController(BibliotecaContext context, ILogger<SancionesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // =============================================
        // LISTADO DE SANCIONES
        // =============================================

        // GET: Index (Para administradores/bibliotecarios)
        public async Task<IActionResult> Index(string filtro, string buscar)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("MisSanciones");
            }

            var sanciones = _context.Sanciones
                .Include(s => s.Usuario)
                .Include(s => s.Prestamo).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(s => s.Pagos)
                .AsQueryable();

            // Filtros
            if (!string.IsNullOrEmpty(filtro))
            {
                sanciones = filtro switch
                {
                    "pendientes" => sanciones.Where(s => s.EstadoSancion == "Pendiente"),
                    "pagadas" => sanciones.Where(s => s.EstadoSancion == "Pagada"),
                    "anuladas" => sanciones.Where(s => s.EstadoSancion == "Anulada"),
                    _ => sanciones
                };
            }

            if (!string.IsNullOrEmpty(buscar))
            {
                sanciones = sanciones.Where(s =>
                    s.Usuario.Nombres.Contains(buscar) ||
                    s.Usuario.Apellidos.Contains(buscar) ||
                    s.Usuario.DNI.Contains(buscar));
            }

            var resultado = await sanciones
                .OrderByDescending(s => s.FechaSancion)
                .ToListAsync();

            // Calcular montos restantes
            foreach (var sancion in resultado)
            {
                var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
                sancion.MontoRestante = sancion.Monto - totalPagado;
            }

            ViewBag.Filtro = filtro;
            ViewBag.SancionesPendientes = resultado.Count(s => s.EstadoSancion == "Pendiente");
            ViewBag.TotalPendiente = resultado.Where(s => s.EstadoSancion == "Pendiente").Sum(s => s.MontoRestante);

            return View(resultado);
        }

        // GET: MisSanciones (Para usuarios)
        public async Task<IActionResult> MisSanciones()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var sanciones = await _context.Sanciones
                .Include(s => s.Prestamo).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(s => s.Pagos)
                .Where(s => s.IdUsuario == userId)
                .OrderByDescending(s => s.FechaSancion)
                .ToListAsync();

            // Calcular montos restantes
            foreach (var sancion in sanciones)
            {
                var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
                sancion.MontoRestante = sancion.Monto - totalPagado;
            }

            ViewBag.SancionesPendientes = sanciones.Count(s => s.EstadoSancion == "Pendiente");
            ViewBag.TotalDeuda = sanciones.Where(s => s.EstadoSancion == "Pendiente").Sum(s => s.MontoRestante);

            return View(sanciones);
        }

        // =============================================
        // CREAR SANCIÓN MANUAL
        // =============================================

        // GET: Create
        public async Task<IActionResult> Create()
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

            ViewBag.TiposSancion = new List<string> { "Retraso", "Daño", "Pérdida", "Otro" };

            return View();
        }

        // POST: Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Sancion sancion)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.Usuarios = await _context.Usuarios
                        .Include(u => u.Rol)
                        .Where(u => u.Activo)
                        .OrderBy(u => u.Apellidos)
                        .ToListAsync();
                    ViewBag.TiposSancion = new List<string> { "Retraso", "Daño", "Pérdida", "Otro" };
                    return View(sancion);
                }

                sancion.FechaSancion = DateTime.Now;
                sancion.EstadoSancion = "Pendiente";

                _context.Sanciones.Add(sancion);
                await _context.SaveChangesAsync();

                // Notificar al usuario
                var usuario = await _context.Usuarios.FindAsync(sancion.IdUsuario);
                await EnviarNotificacion(sancion.IdUsuario, "Nueva sanción registrada",
                    $"Se ha registrado una sanción de tipo '{sancion.TipoSancion}' por un monto de S/{sancion.Monto:F2}. Descripción: {sancion.Descripcion}");

                await RegistrarAccion("Crear sanción", "Sanciones", sancion.IdSancion,
                    $"Sanción creada para {usuario.NombreCompleto}. Tipo: {sancion.TipoSancion}, Monto: S/{sancion.Monto:F2}");

                TempData["Success"] = "Sanción registrada correctamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Create Sancion");
                ModelState.AddModelError("", "Error al crear la sanción");
                ViewBag.Usuarios = await _context.Usuarios
                    .Include(u => u.Rol)
                    .Where(u => u.Activo)
                    .OrderBy(u => u.Apellidos)
                    .ToListAsync();
                ViewBag.TiposSancion = new List<string> { "Retraso", "Daño", "Pérdida", "Otro" };
                return View(sancion);
            }
        }

        // =============================================
        // REGISTRAR PAGO
        // =============================================

        // GET: RegistrarPago
        public async Task<IActionResult> RegistrarPago(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var sancion = await _context.Sanciones
                .Include(s => s.Usuario)
                .Include(s => s.Prestamo).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(s => s.Pagos)
                .FirstOrDefaultAsync(s => s.IdSancion == id);

            if (sancion == null) return NotFound();

            if (sancion.EstadoSancion != "Pendiente")
            {
                TempData["Error"] = "Esta sanción ya fue pagada o anulada";
                return RedirectToAction(nameof(Index));
            }

            // Calcular monto restante
            var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
            sancion.MontoRestante = sancion.Monto - totalPagado;

            ViewBag.MetodosPago = new List<string> { "Efectivo", "Tarjeta", "Transferencia", "Yape", "Plin" };

            return View(sancion);
        }

        // POST: RegistrarPago
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPago(int idSancion, decimal montoPagado, string metodoPago, string comprobante, string observaciones)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                var sancion = await _context.Sanciones
                    .Include(s => s.Usuario)
                    .Include(s => s.Pagos)
                    .FirstOrDefaultAsync(s => s.IdSancion == idSancion);

                if (sancion == null)
                {
                    TempData["Error"] = "Sanción no encontrada";
                    return RedirectToAction(nameof(Index));
                }

                // Calcular monto restante
                var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
                var montoRestante = sancion.Monto - totalPagado;

                if (montoPagado <= 0 || montoPagado > montoRestante)
                {
                    TempData["Error"] = $"El monto debe ser mayor a 0 y no exceder S/{montoRestante:F2}";
                    return RedirectToAction(nameof(RegistrarPago), new { id = idSancion });
                }

                // Registrar pago
                var pago = new Pago
                {
                    IdSancion = idSancion,
                    MontoPagado = montoPagado,
                    FechaPago = DateTime.Now,
                    MetodoPago = metodoPago,
                    Comprobante = comprobante,
                    Observaciones = observaciones
                };

                _context.Pagos.Add(pago);

                // Actualizar estado de la sanción si se pagó completamente
                totalPagado += montoPagado;
                if (totalPagado >= sancion.Monto)
                {
                    sancion.EstadoSancion = "Pagada";
                }

                await _context.SaveChangesAsync();

                // Notificar al usuario
                string mensaje = totalPagado >= sancion.Monto
                    ? $"Su sanción por {sancion.TipoSancion} ha sido pagada completamente. Monto: S/{montoPagado:F2}"
                    : $"Se ha registrado un pago parcial de S/{montoPagado:F2}. Monto restante: S/{(sancion.Monto - totalPagado):F2}";

                await EnviarNotificacion(sancion.IdUsuario, "Pago de sanción registrado", mensaje);

                await RegistrarAccion("Registrar pago", "Pagos", pago.IdPago,
                    $"Pago de S/{montoPagado:F2} registrado para sanción #{idSancion}. Usuario: {sancion.Usuario.NombreCompleto}");

                TempData["Success"] = totalPagado >= sancion.Monto
                    ? "Pago registrado. La sanción ha sido pagada completamente"
                    : $"Pago registrado. Monto restante: S/{(sancion.Monto - totalPagado):F2}";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en RegistrarPago");
                TempData["Error"] = "Error al registrar el pago";
                return RedirectToAction(nameof(RegistrarPago), new { id = idSancion });
            }
        }

        // =============================================
        // ANULAR SANCIÓN
        // =============================================

        // POST: Anular
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Anular(int id, string motivo)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                return Json(new { success = false, message = "Solo los administradores pueden anular sanciones" });
            }

            try
            {
                var sancion = await _context.Sanciones
                    .Include(s => s.Usuario)
                    .FirstOrDefaultAsync(s => s.IdSancion == id);

                if (sancion == null)
                {
                    return Json(new { success = false, message = "Sanción no encontrada" });
                }

                if (sancion.EstadoSancion == "Pagada")
                {
                    return Json(new { success = false, message = "No se puede anular una sanción pagada" });
                }

                sancion.EstadoSancion = "Anulada";
                sancion.Descripcion += $"\n[Anulada] {motivo}";

                await _context.SaveChangesAsync();

                // Notificar
                await EnviarNotificacion(sancion.IdUsuario, "Sanción anulada",
                    $"Su sanción de tipo '{sancion.TipoSancion}' por S/{sancion.Monto:F2} ha sido anulada. Motivo: {motivo}");

                await RegistrarAccion("Anular sanción", "Sanciones", id,
                    $"Sanción #{id} anulada. Usuario: {sancion.Usuario.NombreCompleto}. Motivo: {motivo}");

                return Json(new { success = true, message = "Sanción anulada correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Anular Sancion");
                return Json(new { success = false, message = "Error al anular la sanción" });
            }
        }

        // =============================================
        // DETALLE DE SANCIÓN
        // =============================================

        // GET: Details
        public async Task<IActionResult> Details(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");

            if (id == null) return NotFound();

            var sancion = await _context.Sanciones
                .Include(s => s.Usuario).ThenInclude(u => u.Rol)
                .Include(s => s.Prestamo).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                .Include(s => s.Pagos)
                .FirstOrDefaultAsync(s => s.IdSancion == id);

            if (sancion == null) return NotFound();

            // Verificar permisos
            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;
            string rol = HttpContext.Session.GetString("UsuarioRol");

            if (sancion.IdUsuario != userId && rol != "Administrador" && rol != "Bibliotecario")
            {
                TempData["Error"] = "No tiene permisos para ver esta sanción";
                return RedirectToAction("MisSanciones");
            }

            // Calcular monto restante
            var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
            sancion.MontoRestante = sancion.Monto - totalPagado;

            return View(sancion);
        }

        // =============================================
        // MÉTODOS AUXILIARES
        // =============================================

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
                    TipoNotificacion = "Sanción",
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