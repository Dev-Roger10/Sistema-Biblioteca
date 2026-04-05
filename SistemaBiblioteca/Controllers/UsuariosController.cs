using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using System.Security.Cryptography;
using SistemaBiblioteca.Models.ViewModels;
using System.Text;

namespace SistemaBiblioteca.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly BibliotecaContext _context;
        private readonly ILogger<UsuariosController> _logger;

        public UsuariosController(BibliotecaContext context, ILogger<UsuariosController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // AUTENTICACIÓN
        // GET: Login
        public IActionResult Login()
        {
            if (HttpContext.Session.GetInt32("UsuarioId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string correo, string contrasena)
        {
            try
            {
                if (string.IsNullOrEmpty(correo) || string.IsNullOrEmpty(contrasena))
                {
                    TempData["Error"] = "Debe ingresar correo y contraseña";
                    return View();
                }

                // Encriptar contraseña
                string contrasenaEncriptada = EncriptarSHA256(contrasena);

                // Buscar usuario
                var usuario = await _context.Usuarios
             .Include(u => u.Rol)
                 .FirstOrDefaultAsync(u => u.Correo == correo &&
                   u.Contrasena == contrasenaEncriptada &&
      u.Activo);

                if (usuario == null)
                {
                    TempData["Error"] = "Credenciales inválidas";
                    return View();
                }

                // Actualizar último acceso
                usuario.UltimoAcceso = DateTime.Now;
                await _context.SaveChangesAsync();

                // Crear sesión
                HttpContext.Session.SetInt32("UsuarioId", usuario.IdUsuario);
                HttpContext.Session.SetString("UsuarioNombre", usuario.NombreCompleto);
                HttpContext.Session.SetString("UsuarioRol", usuario.Rol.NombreRol);
                HttpContext.Session.SetInt32("RolId", usuario.IdRol);

                // Registrar en historial
                await RegistrarAccion(usuario.IdUsuario, "Inicio de sesión", "Usuarios", usuario.IdUsuario, "Usuario inició sesión");

                TempData["Success"] = $"Bienvenido {usuario.NombreCompleto}";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Login");
                TempData["Error"] = "Error al iniciar sesión";
                return View();
            }
        }

        // GET: Logout
        public async Task<IActionResult> Logout()
        {
            int? userId = HttpContext.Session.GetInt32("UsuarioId");

            if (userId.HasValue)
            {
                await RegistrarAccion(userId.Value, "Cierre de sesión", "Usuarios", userId.Value, "Usuario cerró sesión");
            }

            HttpContext.Session.Clear();
            TempData["Success"] = "Sesión cerrada correctamente";
            return RedirectToAction("Login");
        }

        // GET: Registro
        public async Task<IActionResult> Registro()
        {
            ViewBag.Roles = await _context.Roles
       .Where(r => r.Activo)
      .ToListAsync();

            return View(new RegistroViewModel());
        }

        // POST: Registro
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registro(RegistroViewModel model)
        {
            try
            {
                ViewBag.Roles = await _context.Roles
                   .Where(r => r.Activo)
                   .ToListAsync();

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (await _context.Usuarios.AnyAsync(u => u.Correo == model.Correo))
                {
                    ModelState.AddModelError("Correo", "El correo ya está registrado");
                    return View(model);
                }

                if (await _context.Usuarios.AnyAsync(u => u.DNI == model.DNI))
                {
                    ModelState.AddModelError("DNI", "El DNI ya está registrado");
                    return View(model);
                }

                var usuario = new Usuario
                {
                    Nombres = model.Nombres,
                    Apellidos = model.Apellidos,
                    DNI = model.DNI,
                    Telefono = model.Telefono,
                    Direccion = model.Direccion,
                    IdRol = model.IdRol,
                    Correo = model.Correo,
                    Contrasena = EncriptarSHA256(model.Contrasena),
                    FechaRegistro = DateTime.Now,
                    Activo = true
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Usuario registrado: {usuario.Correo}");

                TempData["Success"] = "¡Cuenta creada! Ahora puedes iniciar sesión";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Registro");
                ModelState.AddModelError("", $"Error: {ex.Message}");

                ViewBag.Roles = await _context.Roles
         .Where(r => r.Activo && r.IdRol != 1)
             .ToListAsync();

                return View(model);
            }
        }

        // GESTIÓN DE USUARIOS - Administrador

        // GET: Index - Lista de usuarios
        public async Task<IActionResult> Index(string buscar, int? rolFiltro)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                TempData["Error"] = "No tiene permisos para acceder";
                return RedirectToAction("Index", "Home");
            }

            var usuarios = _context.Usuarios
           .Include(u => u.Rol)
      .Where(u => u.Activo)
 .AsQueryable();

            if (!string.IsNullOrEmpty(buscar))
            {
                usuarios = usuarios.Where(u =>
                       u.Nombres.Contains(buscar) ||
               u.Apellidos.Contains(buscar) ||
                     u.DNI.Contains(buscar) ||
                     u.Correo.Contains(buscar));
            }

            if (rolFiltro.HasValue)
            {
                usuarios = usuarios.Where(u => u.IdRol == rolFiltro.Value);
            }

            ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
            return View(await usuarios.OrderBy(u => u.Apellidos).ToListAsync());
        }

        // GET: Details
        public async Task<IActionResult> Details(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (id == null) return NotFound();

            var usuario = await _context.Usuarios
              .Include(u => u.Rol)
              .Include(u => u.Prestamos).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                 .Include(u => u.Sanciones)
             .FirstOrDefaultAsync(u => u.IdUsuario == id);

            if (usuario == null) return NotFound();

            ViewBag.PrestamosActivos = usuario.Prestamos.Count(p => p.EstadoPrestamo == "Activo");
            ViewBag.SancionesPendientes = usuario.Sanciones.Count(s => s.EstadoSancion == "Pendiente");
            ViewBag.TotalSanciones = usuario.Sanciones.Where(s => s.EstadoSancion == "Pendiente").Sum(s => s.Monto);

            return View(usuario);
        }

        // GET: Create - Crear nuevo usuario (Admin)
        public async Task<IActionResult> Create()
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para crear usuarios";
                return RedirectToAction("Index");
            }

            ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
            return View(new Usuario());
        }

        // POST: Create - Guardar nuevo usuario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Usuario usuario, string contrasena)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para crear usuarios";
                return RedirectToAction("Index");
            }

            try
            {
                if (await _context.Usuarios.AnyAsync(u => u.Correo == usuario.Correo))
                {
                    ModelState.AddModelError("Correo", "Este correo ya está registrado");
                    ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
                    return View(usuario);
                }

                if (await _context.Usuarios.AnyAsync(u => u.DNI == usuario.DNI))
                {
                    ModelState.AddModelError("DNI", "Este DNI ya está registrado");
                    ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
                    return View(usuario);
                }

                if (string.IsNullOrEmpty(contrasena) || contrasena.Length < 6)
                {
                    ModelState.AddModelError("Contrasena", "La contraseña debe tener al menos 6 caracteres");
                    ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
                    return View(usuario);
                }

                usuario.Contrasena = EncriptarSHA256(contrasena);
                usuario.FechaRegistro = DateTime.Now;
                usuario.Activo = true;

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                await RegistrarAccion(
                  HttpContext.Session.GetInt32("UsuarioId").Value,
                       "Creación de usuario",
                      "Usuarios",
                            usuario.IdUsuario,
                        $"Usuario {usuario.NombreCompleto} creado"
                              );

                TempData["Success"] = $"Usuario {usuario.NombreCompleto} creado correctamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear usuario");
                ModelState.AddModelError("", "Error al crear el usuario");
                ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
                return View(usuario);
            }
        }

        // GET: Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id == null) return NotFound();

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario == null) return NotFound();

            ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
            return View(usuario);
        }

        // POST: Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Usuario usuario, bool cambiarContrasena, string nuevaContrasena)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                TempData["Error"] = "No tiene permisos para esta acción";
                return RedirectToAction("Index");
            }

            if (id != usuario.IdUsuario) return NotFound();

            try
            {
                var usuarioDb = await _context.Usuarios.FindAsync(id);
                if (usuarioDb == null) return NotFound();

                usuarioDb.IdRol = usuario.IdRol;
                usuarioDb.Nombres = usuario.Nombres;
                usuarioDb.Apellidos = usuario.Apellidos;
                usuarioDb.DNI = usuario.DNI;
                usuarioDb.Correo = usuario.Correo;
                usuarioDb.Telefono = usuario.Telefono;
                usuarioDb.Direccion = usuario.Direccion;
                usuarioDb.Activo = usuario.Activo;

                if (cambiarContrasena && !string.IsNullOrEmpty(nuevaContrasena))
                {
                    usuarioDb.Contrasena = EncriptarSHA256(nuevaContrasena);
                }

                await _context.SaveChangesAsync();

                await RegistrarAccion(
                HttpContext.Session.GetInt32("UsuarioId").Value,
            "Edición de usuario",
              "Usuarios",
                usuario.IdUsuario,
                 $"Usuario {usuario.NombreCompleto} editado"
                 );

                TempData["Success"] = "Usuario actualizado correctamente";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Edit Usuario");
                ModelState.AddModelError("", "Error al actualizar usuario");
                ViewBag.Roles = await _context.Roles.Where(r => r.Activo).ToListAsync();
                return View(usuario);
            }
        }

        // POST: Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");
            if (!ValidarRol(new[] { "Administrador" }))
            {
                return Json(new { success = false, message = "No tiene permisos para esta acción" });
            }

            try
            {
                var usuario = await _context.Usuarios.FindAsync(id);
                if (usuario == null)
                {
                    return Json(new { success = false, message = "Usuario no encontrado" });
                }

                if (id == HttpContext.Session.GetInt32("UsuarioId"))
                {
                    return Json(new { success = false, message = "No puede eliminar su propio usuario" });
                }

                usuario.Activo = false;
                await _context.SaveChangesAsync();

                await RegistrarAccion(
               HttpContext.Session.GetInt32("UsuarioId").Value,
           "Eliminación de usuario",
          "Usuarios",
id,
         $"Usuario {usuario.NombreCompleto} eliminado"
     );

                return Json(new { success = true, message = "Usuario eliminado correctamente" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Delete Usuario");
                return Json(new { success = false, message = "Error al eliminar usuario" });
            }
        }

        // GET: Perfil
        public async Task<IActionResult> Perfil()
        {
            if (!ValidarSesion()) return RedirectToAction("Login");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var usuario = await _context.Usuarios
                .Include(u => u.Rol)
           .FirstOrDefaultAsync(u => u.IdUsuario == userId);

            if (usuario == null) return NotFound();

            ViewBag.PrestamosActivos = await _context.Prestamos
 .CountAsync(p => p.IdUsuario == userId && p.EstadoPrestamo == "Activo");

            ViewBag.ReservasActivas = await _context.Reservas
               .CountAsync(r => r.IdUsuario == userId && r.EstadoReserva == "Pendiente");

            ViewBag.SancionesPendientes = await _context.Sanciones
            .CountAsync(s => s.IdUsuario == userId && s.EstadoSancion == "Pendiente");

            ViewBag.ActividadReciente = await _context.HistorialAcciones
      .Where(h => h.IdUsuario == userId)
        .OrderByDescending(h => h.FechaAccion)
          .Take(10)
   .ToListAsync();

            return View(usuario);
        }

        // GET: EditarPerfil
        public async Task<IActionResult> EditarPerfil()
        {
            if (!ValidarSesion()) return RedirectToAction("Login");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;
            var usuario = await _context.Usuarios.FindAsync(userId);

            if (usuario == null) return NotFound();

            return View(usuario);
        }

        // POST: EditarPerfil
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarPerfil(Usuario usuario)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            if (userId != usuario.IdUsuario) return Unauthorized();

            try
            {
                var usuarioDb = await _context.Usuarios.FindAsync(userId);
                if (usuarioDb == null) return NotFound();

                usuarioDb.Nombres = usuario.Nombres;
                usuarioDb.Apellidos = usuario.Apellidos;
                usuarioDb.Telefono = usuario.Telefono;
                usuarioDb.Direccion = usuario.Direccion;
                usuarioDb.Correo = usuario.Correo;

                await _context.SaveChangesAsync();

                HttpContext.Session.SetString("UsuarioNombre", usuarioDb.NombreCompleto);

                await RegistrarAccion(userId, "Edición de perfil", "Usuarios", userId, "Perfil actualizado");

                TempData["Success"] = "Perfil actualizado correctamente";
                return RedirectToAction("Perfil");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al editar perfil");
                TempData["Error"] = "Error al actualizar el perfil";
                return View(usuario);
            }
        }

        // POST: CambiarContrasena
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarContrasena(string contrasenaActual, string nuevaContrasena, string confirmarContrasena)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");

            try
            {
                int userId = HttpContext.Session.GetInt32("UsuarioId").Value;
                var usuario = await _context.Usuarios.FindAsync(userId);

                if (usuario == null) return NotFound();

                if (usuario.Contrasena != EncriptarSHA256(contrasenaActual))
                {
                    TempData["Error"] = "La contraseña actual es incorrecta";
                    return RedirectToAction("Perfil");
                }

                if (nuevaContrasena != confirmarContrasena)
                {
                    TempData["Error"] = "Las contraseñas no coinciden";
                    return RedirectToAction("Perfil");
                }

                if (nuevaContrasena.Length < 6)
                {
                    TempData["Error"] = "La contraseña debe tener al menos 6 caracteres";
                    return RedirectToAction("Perfil");
                }

                usuario.Contrasena = EncriptarSHA256(nuevaContrasena);
                await _context.SaveChangesAsync();

                await RegistrarAccion(userId, "Cambio de contraseña", "Usuarios", userId, "Contraseña actualizada");

                TempData["Success"] = "Contraseña actualizada correctamente";
                return RedirectToAction("Perfil");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar contraseña");
                TempData["Error"] = "Error al cambiar la contraseña";
                return RedirectToAction("Perfil");
            }
        }

        // GET: Notificaciones
        public async Task<IActionResult> Notificaciones(string filtro)
        {
            if (!ValidarSesion()) return RedirectToAction("Login");

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var notificaciones = _context.Notificaciones
           .Where(n => n.IdUsuario == userId)
        .AsQueryable();

            if (filtro == "noLeidas")
            {
                notificaciones = notificaciones.Where(n => !n.Leida);
            }

            ViewBag.Filtro = filtro;
            ViewBag.NoLeidas = await _context.Notificaciones
                 .CountAsync(n => n.IdUsuario == userId && !n.Leida);

            return View(await notificaciones
        .OrderByDescending(n => n.FechaEnvio)
              .ToListAsync());
        }

        // POST: MarcarLeida
        [HttpPost]
        public async Task<IActionResult> MarcarLeida(int id)
        {
            if (!ValidarSesion()) return Json(new { success = false });

            var notif = await _context.Notificaciones.FindAsync(id);
            if (notif != null)
            {
                notif.Leida = true;
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        // POST: MarcarTodasLeidas
        [HttpPost]
        public async Task<IActionResult> MarcarTodasLeidas()
        {
            if (!ValidarSesion()) return Json(new { success = false });

            int userId = HttpContext.Session.GetInt32("UsuarioId").Value;

            var notificaciones = await _context.Notificaciones
              .Where(n => n.IdUsuario == userId && !n.Leida)
          .ToListAsync();

            foreach (var notif in notificaciones)
            {
                notif.Leida = true;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // POST: EliminarNotificacion
        [HttpPost]
        public async Task<IActionResult> EliminarNotificacion(int id)
        {
            if (!ValidarSesion()) return Json(new { success = false });

            var notif = await _context.Notificaciones.FindAsync(id);
            if (notif != null)
            {
                _context.Notificaciones.Remove(notif);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        // =============================================
        // MÉTODOS AUXILIARES
        // =============================================

        private string EncriptarSHA256(string texto)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(texto));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
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

        private async Task RegistrarAccion(int usuarioId, string accion, string tabla, int? registroId, string detalles)
        {
            try
            {
                var historial = new HistorialAccion
                {
                    IdUsuario = usuarioId,
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
                _logger.LogError(ex, "Error al registrar acción en historial");
            }
        }

        private bool UsuarioExists(int id)
        {
            return _context.Usuarios.Any(e => e.IdUsuario == id);
        }
    }
}
