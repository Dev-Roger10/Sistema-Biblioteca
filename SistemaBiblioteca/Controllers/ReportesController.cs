using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using System.Text;

namespace SistemaBiblioteca.Controllers
{
    public class ReportesController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<ReportesController> _logger;

        public ReportesController(BibliotecaContext context, ILogger<ReportesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // =============================================
        // LISTADO DE REPORTES
        // =============================================

        // GET: Index
        public IActionResult Index()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder a reportes";
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // =============================================
        // REPORTE: SANCIONES POR USUARIO
        // =============================================

        // GET: SancionesPorUsuario
        public async Task<IActionResult> SancionesPorUsuario()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder a reportes";
                return RedirectToAction("Index", "Home");
            }

            // Obtener usuarios con sanciones pendientes
            var usuarios = await _context.Usuarios
             .Include(u => u.Sanciones).ThenInclude(s => s.Pagos)
          .Include(u => u.Sanciones).ThenInclude(s => s.Prestamo)
               .Where(u => u.Activo && u.Sanciones.Any(s => s.EstadoSancion == "Pendiente"))
           .OrderBy(u => u.Apellidos)
       .ToListAsync();

            return View(usuarios);
        }

        // GET: DescargarReporteSanciones (Excel)
        public async Task<IActionResult> DescargarReporteSanciones(string formato = "excel")
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                return Json(new { success = false, message = "No tiene permisos" });
            }

            try
            {
                // Obtener datos
                var sanciones = await _context.Sanciones
               .Include(s => s.Usuario)
                         .Include(s => s.Prestamo).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
            .Include(s => s.Pagos)
                  .Where(s => s.EstadoSancion == "Pendiente")
               .OrderByDescending(s => s.FechaSancion)
                 .ToListAsync();

                if (formato.ToLower() == "excel")
                {
                    return GenerarExcel(sanciones);
                }
                else
                {
                    return GenerarCSV(sanciones);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar reporte");
                return BadRequest("Error al generar el reporte");
            }
        }

        private FileResult GenerarExcel(List<Sancion> sanciones)
        {
            var sb = new StringBuilder();

            // Encabezado
            sb.AppendLine("REPORTE DE SANCIONES PENDIENTES");
            sb.AppendLine("Fecha de generación: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            sb.AppendLine("");
            sb.AppendLine("");

            // Títulos de columnas
            sb.AppendLine("DNI,Usuario,Email,Tipo Sanción,Monto,Fecha Sanción,Estado,Libro,Descripción,Monto Pagado,Monto Pendiente");

            // Datos
            foreach (var sancion in sanciones)
            {
                var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
                var montoPendiente = sancion.Monto - totalPagado;

                sb.AppendLine($"\"{sancion.Usuario?.DNI}\"," +
                            $"\"{sancion.Usuario?.NombreCompleto}\"," +
                       $"\"{sancion.Usuario?.Correo}\"," +
                            $"\"{sancion.TipoSancion}\"," +
                $"{sancion.Monto:F2}," +
                     $"\"{sancion.FechaSancion:dd/MM/yyyy}\"," +
                    $"\"{sancion.EstadoSancion}\"," +
                     $"\"{sancion.Prestamo?.Ejemplar?.Libro?.Titulo ?? "N/A"}\"," +
                    $"\"{sancion.Descripcion ?? ""}\"," +
                        $"{totalPagado:F2}," +
                   $"{montoPendiente:F2}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "application/octet-stream", $"Reporte_Sanciones_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        private FileResult GenerarCSV(List<Sancion> sanciones)
        {
            var sb = new StringBuilder();

            // Títulos de columnas
            sb.AppendLine("DNI,Usuario,Email,Tipo Sanción,Monto,Fecha Sanción,Estado,Libro,Descripción,Monto Pagado,Monto Pendiente");

            // Datos
            foreach (var sancion in sanciones)
            {
                var totalPagado = sancion.Pagos?.Sum(p => p.MontoPagado) ?? 0;
                var montoPendiente = sancion.Monto - totalPagado;

                sb.AppendLine($"\"{sancion.Usuario?.DNI}\"," +
            $"\"{sancion.Usuario?.NombreCompleto}\"," +
                   $"\"{sancion.Usuario?.Correo}\"," +
                     $"\"{sancion.TipoSancion}\"," +
          $"{sancion.Monto:F2}," +
                    $"\"{sancion.FechaSancion:dd/MM/yyyy}\"," +
                     $"\"{sancion.EstadoSancion}\"," +
              $"\"{sancion.Prestamo?.Ejemplar?.Libro?.Titulo ?? "N/A"}\"," +
                     $"\"{sancion.Descripcion ?? ""}\"," +
                $"{totalPagado:F2}," +
             $"{montoPendiente:F2}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"Reporte_Sanciones_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
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
    }
}
