using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Controllers
{
  public class EditorialesController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<EditorialesController> _logger;

  public EditorialesController(BibliotecaContext context, ILogger<EditorialesController> logger)
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

        // GET: Index - Listar editoriales
        public async Task<IActionResult> Index(string buscar)
        {
        if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
      {
        TempData["Error"] = "No tiene permisos para acceder";
       return RedirectToAction("Index", "Home");
     }

  var editoriales = _context.Editoriales.AsQueryable();

       if (!string.IsNullOrEmpty(buscar))
          {
                editoriales = editoriales.Where(e => e.NombreEditorial.Contains(buscar));
            }

  var resultado = await editoriales.OrderBy(e => e.NombreEditorial).ToListAsync();

         ViewBag.TotalEditoriales = resultado.Count;
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
        public async Task<IActionResult> Create(Editorial editorial)
        {
         if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
      if (!ValidarRol(new[] { "Administrador" }))
        {
 TempData["Error"] = "No tiene permisos para esta acción";
            return RedirectToAction("Index");
            }

  try
 {
    // Validar que no exista editorial con el mismo nombre
           if (await _context.Editoriales.AnyAsync(e => e.NombreEditorial == editorial.NombreEditorial))
    {
      ModelState.AddModelError("NombreEditorial", "Esta editorial ya existe");
        return View(editorial);
      }

          if (ModelState.IsValid)
          {
              editorial.Activo = true;
   _context.Editoriales.Add(editorial);
         await _context.SaveChangesAsync();

 _logger.LogInformation($"Editorial creada: {editorial.NombreEditorial}");
      TempData["Success"] = "Editorial creada exitosamente";
     return RedirectToAction(nameof(Index));
 }

       return View(editorial);
       }
   catch (Exception ex)
   {
         _logger.LogError(ex, "Error al crear editorial");
TempData["Error"] = "Error al crear la editorial";
   return View(editorial);
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

    var editorial = await _context.Editoriales.FindAsync(id);
            if (editorial == null) return NotFound();

         return View(editorial);
        }

        // POST: Edit
   [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Editorial editorial)
        {
if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador" }))
            {
       TempData["Error"] = "No tiene permisos para esta acción";
return RedirectToAction("Index");
            }

            if (id != editorial.IdEditorial) return NotFound();

   try
            {
   // Validar que no exista otra editorial con el mismo nombre
 if (await _context.Editoriales.AnyAsync(e => e.NombreEditorial == editorial.NombreEditorial && e.IdEditorial != id))
          {
          ModelState.AddModelError("NombreEditorial", "Esta editorial ya existe");
  return View(editorial);
      }

     if (ModelState.IsValid)
      {
        _context.Update(editorial);
     await _context.SaveChangesAsync();

     _logger.LogInformation($"Editorial actualizada: {editorial.NombreEditorial}");
       TempData["Success"] = "Editorial actualizada exitosamente";
     return RedirectToAction(nameof(Index));
                }

     return View(editorial);
      }
     catch (Exception ex)
    {
             _logger.LogError(ex, "Error al actualizar editorial");
      TempData["Error"] = "Error al actualizar la editorial";
                return View(editorial);
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

            var editorial = await _context.Editoriales.FindAsync(id);
  if (editorial == null) return NotFound();

return View(editorial);
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
              var editorial = await _context.Editoriales.FindAsync(id);
                if (editorial == null) return NotFound();

      // Verificar si hay libros de esta editorial
           if (await _context.Libros.AnyAsync(l => l.IdEditorial == id))
           {
 TempData["Error"] = "No se puede eliminar: existen libros de esta editorial";
 return RedirectToAction(nameof(Index));
       }

   _context.Editoriales.Remove(editorial);
    await _context.SaveChangesAsync();

        _logger.LogInformation($"Editorial eliminada: {editorial.NombreEditorial}");
      TempData["Success"] = "Editorial eliminada exitosamente";
        return RedirectToAction(nameof(Index));
}
          catch (Exception ex)
       {
        _logger.LogError(ex, "Error al eliminar editorial");
                TempData["Error"] = "Error al eliminar la editorial";
       return RedirectToAction(nameof(Index));
            }
        }
    }
}
