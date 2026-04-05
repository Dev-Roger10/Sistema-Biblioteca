using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;

namespace SistemaBiblioteca.Controllers
{
    public class SedesController : Controller
    {
        private readonly BibliotecaContext _context;
     private readonly ILogger<SedesController> _logger;

public SedesController(BibliotecaContext context, ILogger<SedesController> logger)
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

        // GET: Index - Listar sedes
     public async Task<IActionResult> Index(string buscar)
        {
if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
      {
       TempData["Error"] = "No tiene permisos para acceder";
      return RedirectToAction("Index", "Home");
  }

            var sedes = _context.Sedes.AsQueryable();

   if (!string.IsNullOrEmpty(buscar))
        {
    sedes = sedes.Where(s => s.NombreSede.Contains(buscar) || s.Direccion.Contains(buscar));
         }

    var resultado = await sedes.OrderBy(s => s.NombreSede).ToListAsync();

    ViewBag.TotalSedes = resultado.Count;
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
        public async Task<IActionResult> Create(Sede sede)
     {
if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
           if (!ValidarRol(new[] { "Administrador" }))
    {
TempData["Error"] = "No tiene permisos para esta acción";
   return RedirectToAction("Index");
           }

      try
        {
        // Validar que no exista sede con el mismo nombre
    if (await _context.Sedes.AnyAsync(s => s.NombreSede == sede.NombreSede))
 {
ModelState.AddModelError("NombreSede", "Esta sede ya existe");
    return View(sede);
     }

  if (ModelState.IsValid)
        {
           sede.Activo = true;
         _context.Sedes.Add(sede);
      await _context.SaveChangesAsync();

   _logger.LogInformation($"Sede creada: {sede.NombreSede}");
  TempData["Success"] = "Sede creada exitosamente";
  return RedirectToAction(nameof(Index));
      }

    return View(sede);
    }
  catch (Exception ex)
     {
 _logger.LogError(ex, "Error al crear sede");
        TempData["Error"] = "Error al crear la sede";
      return View(sede);
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

            var sede = await _context.Sedes.FindAsync(id);
      if (sede == null) return NotFound();

       return View(sede);
       }

        // POST: Edit
       [HttpPost]
        [ValidateAntiForgeryToken]
         public async Task<IActionResult> Edit(int id, Sede sede)
        {
       if (!ValidarSesion()) return RedirectToAction("Login", "Usuarios");
      if (!ValidarRol(new[] { "Administrador" }))
              {
           TempData["Error"] = "No tiene permisos para esta acción";
      return RedirectToAction("Index");
 }

     if (id != sede.IdSede) return NotFound();

      try
            {
  // Validar que no exista otra sede con el mismo nombre
    if (await _context.Sedes.AnyAsync(s => s.NombreSede == sede.NombreSede && s.IdSede != id))
 {
      ModelState.AddModelError("NombreSede", "Esta sede ya existe");
      return View(sede);
       }

 if (ModelState.IsValid)
  {
 _context.Update(sede);
      await _context.SaveChangesAsync();

     _logger.LogInformation($"Sede actualizada: {sede.NombreSede}");
TempData["Success"] = "Sede actualizada exitosamente";
        return RedirectToAction(nameof(Index));
   }

   return View(sede);
  }
      catch (Exception ex)
      {
   _logger.LogError(ex, "Error al actualizar sede");
   TempData["Error"] = "Error al actualizar la sede";
           return View(sede);
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

      var sede = await _context.Sedes.FindAsync(id);
if (sede == null) return NotFound();

 return View(sede);
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
 var sede = await _context.Sedes.FindAsync(id);
         if (sede == null) return NotFound();

       // Verificar si hay ejemplares en esta sede
 if (await _context.Ejemplares.AnyAsync(e => e.IdSede == id))
         {
  TempData["Error"] = "No se puede eliminar: existen ejemplares en esta sede";
         return RedirectToAction(nameof(Index));
           }

    _context.Sedes.Remove(sede);
        await _context.SaveChangesAsync();

      _logger.LogInformation($"Sede eliminada: {sede.NombreSede}");
      TempData["Success"] = "Sede eliminada exitosamente";
         return RedirectToAction(nameof(Index));
   }
        catch (Exception ex)
         {
    _logger.LogError(ex, "Error al eliminar sede");
       TempData["Error"] = "Error al eliminar la sede";
         return RedirectToAction(nameof(Index));
        }
     }
   }
}
