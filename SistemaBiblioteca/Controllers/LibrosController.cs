using Microsoft.AspNetCore.Mvc;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using Microsoft.EntityFrameworkCore;

namespace SistemaBiblioteca.Controllers
{
    public class LibrosController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<LibrosController> _logger;

        public LibrosController(BibliotecaContext context, ILogger<LibrosController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Catalogo
        public async Task<IActionResult> Catalogo(string buscar, int? categoria, int? editorial, string orden)
        {
            var libros = _context.Libros
                .Include(l => l.Categoria)
                .Include(l => l.Editorial)
                .Include(l => l.Ejemplares)
                .Where(l => l.Activo)
                .AsQueryable();

            // Filtros
            if (!string.IsNullOrEmpty(buscar))
            {
                libros = libros.Where(l =>
                    l.Titulo.Contains(buscar) ||
                    l.Autor.Contains(buscar) ||
                    l.ISBN.Contains(buscar));
            }

            if (categoria.HasValue)
            {
                libros = libros.Where(l => l.IdCategoria == categoria.Value);
            }

            if (editorial.HasValue)
            {
                libros = libros.Where(l => l.IdEditorial == editorial.Value);
            }

            // Ordenamiento
            libros = orden switch
            {
                "titulo_desc" => libros.OrderByDescending(l => l.Titulo),
                "autor" => libros.OrderBy(l => l.Autor),
                "autor_desc" => libros.OrderByDescending(l => l.Autor),
                "fecha" => libros.OrderBy(l => l.FechaRegistro),
                "fecha_desc" => libros.OrderByDescending(l => l.FechaRegistro),
                _ => libros.OrderBy(l => l.Titulo)
            };

            var resultado = await libros.ToListAsync();

            // Calcular disponibilidad
            foreach (var libro in resultado)
            {
                libro.TotalEjemplares = libro.Ejemplares.Count(e => e.Activo);
                libro.EjemplaresDisponibles = libro.Ejemplares.Count(e => e.Activo && e.Estado == "Disponible");
            }

            ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
            ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
            ViewBag.Buscar = buscar;
            ViewBag.CategoriaSeleccionada = categoria;
            ViewBag.EditorialSeleccionada = editorial;
            ViewBag.OrdenActual = orden;

            return View(resultado);
        }

        // GET: DetalleLibro
        public async Task<IActionResult> DetalleLibro(int? id)
        {
            if (id == null) return NotFound();

            var libro = await _context.Libros
                .Include(l => l.Categoria)
                .Include(l => l.Editorial)
                .Include(l => l.Ejemplares.Where(e => e.Activo))
                    .ThenInclude(e => e.Sede)
                .FirstOrDefaultAsync(l => l.IdLibro == id && l.Activo);

            if (libro == null) return NotFound();

            // Calcular disponibilidad
            ViewBag.EjemplaresDisponibles = libro.Ejemplares.Count(e => e.Estado == "Disponible");
            ViewBag.TotalEjemplares = libro.Ejemplares.Count;

            // Verificar si el usuario puede reservar
            if (HttpContext.Session.GetInt32("UsuarioId") != null)
            {
                int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

                // Verificar límite de reservas
                var reservasActivas = await _context.Reservas
                    .CountAsync(r => r.IdUsuario == userId &&
                                    r.EstadoReserva == "Pendiente");

                var limiteReservas = int.Parse(
                    await _context.ConfiguracionSistema
                        .Where(c => c.Parametro == "LimiteReservas")
                        .Select(c => c.Valor)
                        .FirstOrDefaultAsync() ?? "3"
                );

                ViewBag.PuedeReservar = reservasActivas < limiteReservas;
                ViewBag.ReservasActivas = reservasActivas;
                ViewBag.LimiteReservas = limiteReservas;

                // Verificar si ya tiene reserva de este libro
                ViewBag.YaReservo = await _context.Reservas
                    .AnyAsync(r => r.IdUsuario == userId &&
                                  r.IdLibro == id &&
                                  r.EstadoReserva == "Pendiente");
            }

            return View(libro);
        }


        // GESTIÓN DE LIBROS - Administrador/Bibliotecario

        // GET: Index (Gestión)
        public async Task<IActionResult> Index(string buscar, int? categoria)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("Catalogo");
            }

            var libros = _context.Libros
                .Include(l => l.Categoria)
                .Include(l => l.Editorial)
                .Include(l => l.Ejemplares)
                .Where(l => l.Activo)
                .AsQueryable();

            if (!string.IsNullOrEmpty(buscar))
            {
                libros = libros.Where(l =>
                    l.Titulo.Contains(buscar) ||
                    l.Autor.Contains(buscar) ||
                    l.ISBN.Contains(buscar));
            }

            if (categoria.HasValue)
            {
                libros = libros.Where(l => l.IdCategoria == categoria.Value);
            }

            var resultado = await libros.OrderBy(l => l.Titulo).ToListAsync();

            foreach (var libro in resultado)
            {
                libro.TotalEjemplares = libro.Ejemplares.Count(e => e.Activo);
                libro.EjemplaresDisponibles = libro.Ejemplares.Count(e => e.Activo && e.Estado == "Disponible");
            }

            ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
            return View(resultado);
        }

        // GET: Create
        public async Task<IActionResult> Create()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
            ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
            ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
            return View();
        }

        // POST: Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Libro libro, int cantidadEjemplares, int idSede)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                // ✅ VALIDACIONES MANUALES
                if (string.IsNullOrWhiteSpace(libro?.Titulo))
                {
                    ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                    ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                    ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                    TempData["Error"] = "❌ El Título es obligatorio";
                    return View(libro);
                }

                if (string.IsNullOrWhiteSpace(libro?.Autor))
                {
                    ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                    ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                    ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                    TempData["Error"] = "❌ El Autor es obligatorio";
                    return View(libro);
                }

                if (cantidadEjemplares <= 0)
                {
                    ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                    ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                    ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                    TempData["Error"] = "❌ La cantidad de ejemplares debe ser mayor a 0";
                    return View(libro);
                }

                if (idSede <= 0)
                {
                    ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                    ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                    ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                    TempData["Error"] = "❌ Debe seleccionar una sede válida";
                    return View(libro);
                }

                // Validar ISBN único
                if (!string.IsNullOrEmpty(libro.ISBN))
                {
                    if (await _context.Libros.AnyAsync(l => l.ISBN == libro.ISBN))
                    {
                        ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                        ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                        ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                        TempData["Error"] = "❌ El ISBN ya está registrado";
                        return View(libro);
                    }
                }

                // ✅ LIMPIAR IDs OPCIONALES (establecer a null si son 0)
                if (libro.IdCategoria == 0) libro.IdCategoria = null;
                if (libro.IdEditorial == 0) libro.IdEditorial = null;

                // ✅ NO CARGAR NAVEGACIONES (dejar null)
                libro.Editorial = null;
                libro.Categoria = null;
                libro.Ejemplares = new List<Ejemplar>();
                libro.Reservas = new List<Reserva>();

                // ✅ GUARDAR LIBRO
                libro.FechaRegistro = DateTime.Now;
                libro.Activo = true;

                _context.Libros.Add(libro);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Libro guardado: {libro.IdLibro} - {libro.Titulo}");

                // ✅ CREAR EJEMPLARES
                if (cantidadEjemplares > 0 && idSede > 0)
                {
                    for (int i = 1; i <= cantidadEjemplares; i++)
                    {
                        var ejemplar = new Ejemplar
                        {
                            IdLibro = libro.IdLibro,
                            IdSede = idSede,
                            CodigoEjemplar = $"{libro.ISBN ?? libro.IdLibro.ToString()}-{i:D3}",
                            Estado = "Disponible",
                            FechaAdquisicion = DateTime.Now,
                            Activo = true
                        };
                        _context.Ejemplares.Add(ejemplar);
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"✅ Ejemplares creados: {cantidadEjemplares} para libro {libro.IdLibro}");
                }

                await RegistrarAccion("Creación de libro", "Libros", libro.IdLibro,
                    $"Libro '{libro.Titulo}' creado con {cantidadEjemplares} ejemplares");

                TempData["Success"] = $"✅ Libro '{libro.Titulo}' creado correctamente con {cantidadEjemplares} ejemplares";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "❌ Error de base de datos en Create Libro");
                _logger.LogError(dbEx.InnerException, "❌ Inner Exception: ");
                TempData["Error"] = $"❌ Error de base de datos: {dbEx.InnerException?.Message ?? dbEx.Message}";
                ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                return View(libro);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en Create Libro");
                TempData["Error"] = $"❌ Error al crear el libro: {ex.Message}";
                ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();
                return View(libro);
            }
        }

        // GET: Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var libro = await _context.Libros
                .Include(l => l.Ejemplares.Where(e => e.Activo))
                .FirstOrDefaultAsync(l => l.IdLibro == id);

            if (libro == null) return NotFound();

            ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
            ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
            ViewBag.Sedes = await _context.Sedes.Where(s => s.Activo).ToListAsync();

            return View(libro);
        }

        // POST: Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Libro libro)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id != libro.IdLibro) return NotFound();

            try
            {
                var libroDb = await _context.Libros.FindAsync(id);
                if (libroDb == null) return NotFound();

                // Actualizar datos
                libroDb.ISBN = libro.ISBN;
                libroDb.Titulo = libro.Titulo;
                libroDb.Autor = libro.Autor;
                libroDb.IdEditorial = libro.IdEditorial;
                libroDb.IdCategoria = libro.IdCategoria;
                libroDb.AñoPublicacion = libro.AñoPublicacion;
                libroDb.NumPaginas = libro.NumPaginas;
                libroDb.Idioma = libro.Idioma;
                libroDb.Descripcion = libro.Descripcion;
                libroDb.ImagenPortada = libro.ImagenPortada;
                libroDb.Activo = libro.Activo;

                await _context.SaveChangesAsync();

                await RegistrarAccion("Edición de libro", "Libros", libro.IdLibro,
                    $"Libro '{libro.Titulo}' editado");

                TempData["Success"] = "Libro actualizado correctamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Edit Libro");
                ModelState.AddModelError("", "Error al actualizar el libro");
                ViewBag.Categorias = await _context.Categorias.Where(c => c.Activo).ToListAsync();
                ViewBag.Editoriales = await _context.Editoriales.Where(e => e.Activo).ToListAsync();
                return View(libro);
            }
        }

        // POST: Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                return Json(new { success = false, message = "No tiene permisos para esta acción" });
            }

            try
            {
                var libro = await _context.Libros
                    .Include(l => l.Ejemplares)
                    .FirstOrDefaultAsync(l => l.IdLibro == id);

                if (libro == null)
                {
                    return Json(new { success = false, message = "Libro no encontrado" });
                }

                // Verificar que no haya préstamos activos
                var prestamosActivos = await _context.Prestamos
                    .AnyAsync(p => libro.Ejemplares.Select(e => e.IdEjemplar).Contains(p.IdEjemplar) &&
                                  p.EstadoPrestamo == "Activo");

                if (prestamosActivos)
                {
                    return Json(new { success = false, message = "No se puede eliminar: hay préstamos activos" });
                }

                // Eliminación lógica
                libro.Activo = false;
                foreach (var ejemplar in libro.Ejemplares)
                {
                    ejemplar.Activo = false;
                }

                await _context.SaveChangesAsync();

                await RegistrarAccion("Eliminación de libro", "Libros", id,
                    $"Libro '{libro.Titulo}' eliminado");

                return Json(new { success = true, message = "Libro eliminado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Delete Libro");
                return Json(new { success = false, message = "Error al eliminar el libro" });
            }
        }

        // =============================================
        // GESTIÓN DE EJEMPLARES
        // =============================================

        // POST: AgregarEjemplar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarEjemplar(int idLibro, int idSede, int cantidad)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                return Json(new { success = false, message = "No tiene permisos para esta acción" });
            }

            try
            {
                var libro = await _context.Libros.FindAsync(idLibro);
                if (libro == null)
                {
                    return Json(new { success = false, message = "Libro no encontrado" });
                }

                // Obtener último número de ejemplar
                var ultimoEjemplar = await _context.Ejemplares
                    .Where(e => e.IdLibro == idLibro)
                    .OrderByDescending(e => e.IdEjemplar)
                    .FirstOrDefaultAsync();

                int numeroInicial = 1;
                if (ultimoEjemplar != null)
                {
                    var partes = ultimoEjemplar.CodigoEjemplar.Split('-');
                    if (partes.Length > 1 && int.TryParse(partes.Last(), out int numero))
                    {
                        numeroInicial = numero + 1;
                    }
                }

                // Crear ejemplares
                for (int i = 0; i < cantidad; i++)
                {
                    var ejemplar = new Ejemplar
                    {
                        IdLibro = idLibro,
                        IdSede = idSede,
                        CodigoEjemplar = $"{libro.ISBN ?? libro.IdLibro.ToString()}-{(numeroInicial + i):D3}",
                        Estado = "Disponible",
                        FechaAdquisicion = DateTime.Now,
                        Activo = true
                    };
                    _context.Ejemplares.Add(ejemplar);
                }

                await _context.SaveChangesAsync();

                await RegistrarAccion("Agregar ejemplares", "Ejemplares", idLibro,
                    $"{cantidad} ejemplares agregados al libro '{libro.Titulo}'");

                return Json(new { success = true, message = $"{cantidad} ejemplares agregados correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en AgregarEjemplar");
                return Json(new { success = false, message = "Error al agregar ejemplares" });
            }
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
