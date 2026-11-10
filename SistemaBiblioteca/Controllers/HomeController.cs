using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using SistemaBiblioteca.Models.ViewModels;

namespace SistemaBiblioteca.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly BibliotecaContext _context;

        public HomeController(ILogger<HomeController> logger, BibliotecaContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var vm = new HomeViewModel();

            try
            {
                vm.LibrosActivos = await _context.Libros.CountAsync(l => l.Activo);
                vm.EjemplaresDisponibles = await _context.Ejemplares
                    .CountAsync(e => e.Estado == EstadoEjemplar.Disponible);
                vm.Categorias = await _context.Categorias.CountAsync(c => c.Activo);
                vm.Sedes = await _context.Sedes.CountAsync(s => s.Activo);

                var userId = HttpContext.Session.GetInt32("UsuarioId");
                if (userId != null)
                {
                    var rol = HttpContext.Session.GetString("UsuarioRol");
                    vm.EsPersonal = rol == "Administrador" || rol == "Bibliotecario";
                    var ahora = DateTime.Now;

                    // Personal: cifras globales. Lector: solo las suyas.
                    var prestamos = _context.Prestamos.Where(p => p.FechaDevolucionReal == null);
                    var reservas = _context.Reservas.Where(r => r.EstadoReserva == EstadoReserva.Pendiente);
                    var sanciones = _context.Sanciones.Where(s => s.EstadoSancion == EstadoSancion.Pendiente);

                    if (!vm.EsPersonal)
                    {
                        prestamos = prestamos.Where(p => p.IdUsuario == userId);
                        reservas = reservas.Where(r => r.IdUsuario == userId);
                        sanciones = sanciones.Where(s => s.IdUsuario == userId);
                    }

                    vm.PrestamosActivos = await prestamos.CountAsync();
                    vm.PrestamosVencidos = await prestamos.CountAsync(p => p.FechaDevolucionEsperada < ahora);
                    vm.ReservasPendientes = await reservas.CountAsync();
                    vm.SancionesPendientes = await sanciones.CountAsync();

                    var inicioMes = new DateTime(ahora.Year, ahora.Month, 1);
                    vm.LibrosNuevosMes = await _context.Libros
                        .CountAsync(l => l.Activo && l.FechaRegistro >= inicioMes);
                }
            }
            catch (Exception ex)
            {
                // La portada debe abrir aunque la base de datos no responda.
                _logger.LogError(ex, "No se pudieron cargar las estadísticas de inicio");
            }

            return View(vm);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
