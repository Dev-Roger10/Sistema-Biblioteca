using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Controllers
{
    public class CategoriasController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<CategoriasController> _logger;

        public CategoriasController(BibliotecaContext context, ILogger<CategoriasController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Validar sesión
        private bool ValidarSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioId") != null;
        }

        // Validar rol
        private bool ValidarRol(string[] rolesPermitidos)
        {
            if (!ValidarSesion()) return false;
            string? rol = HttpContext.Session.GetString("UsuarioRol");
            return rolesPermitidos.Contains(rol);
        }

        // GET: Index - Listar categorías
        public async Task<IActionResult> Index(string buscar)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("Index", "Home");
            }

            var categorias = _context.Categorias.AsQueryable();

            if (!string.IsNullOrEmpty(buscar))
            {
                categorias = categorias.Where(c => c.NombreCategoria.Contains(buscar));
            }

            var resultado = await categorias.OrderBy(c => c.NombreCategoria).ToListAsync();

            ViewBag.TotalCategorias = resultado.Count;
            ViewBag.Buscar = buscar;

            return View(resultado);
        }

        // GET: Create
        public IActionResult Create()
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            return View();
        }

        // POST: Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Categoria categoria)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                // Validar que no exista categoría con el mismo nombre
                if (await _context.Categorias.AnyAsync(c => c.NombreCategoria == categoria.NombreCategoria))
                {
                    ModelState.AddModelError("NombreCategoria", "Esta categoría ya existe");
                    return View(categoria);
                }

                if (ModelState.IsValid)
                {
                    categoria.Activo = true;
                    _context.Categorias.Add(categoria);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Categoría creada: {categoria.NombreCategoria}");
                    TempData["Success"] = "Categoría creada exitosamente";
                    return RedirectToAction(nameof(Index));
                }

                return View(categoria);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear categoría");
                TempData["Error"] = "Error al crear la categoría";
                return View(categoria);
            }
        }

        // GET: Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var categoria = await _context.Categorias.FindAsync(id);
            if (categoria == null) return NotFound();

            return View(categoria);
        }

        // POST: Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Categoria categoria)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id != categoria.IdCategoria) return NotFound();

            try
            {
                // Validar que no exista otra categoría con el mismo nombre
                if (await _context.Categorias.AnyAsync(c => c.NombreCategoria == categoria.NombreCategoria && c.IdCategoria != id))
                {
                    ModelState.AddModelError("NombreCategoria", "Esta categoría ya existe");
                    return View(categoria);
                }

                if (ModelState.IsValid)
                {
                    _context.Update(categoria);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Categoría actualizada: {categoria.NombreCategoria}");
                    TempData["Success"] = "Categoría actualizada exitosamente";
                    return RedirectToAction(nameof(Index));
                }

                return View(categoria);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar categoría");
                TempData["Error"] = "Error al actualizar la categoría";
                return View(categoria);
            }
        }

        // GET: Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var categoria = await _context.Categorias.FindAsync(id);
            if (categoria == null) return NotFound();

            return View(categoria);
        }

        // POST: Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            try
            {
                var categoria = await _context.Categorias.FindAsync(id);
                if (categoria == null) return NotFound();

                // Verificar si hay libros con esta categoría
                if (await _context.Libros.AnyAsync(l => l.IdCategoria == id))
                {
                    TempData["Error"] = "No se puede eliminar: existen libros con esta categoría";
                    return RedirectToAction(nameof(Index));
                }

                _context.Categorias.Remove(categoria);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Categoría eliminada: {categoria.NombreCategoria}");
                TempData["Success"] = "Categoría eliminada exitosamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar categoría");
                TempData["Error"] = "Error al eliminar la categoría";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
