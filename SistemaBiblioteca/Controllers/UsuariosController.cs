using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Models;
using SistemaBiblioteca.Models.ViewModels;
using SistemaBiblioteca.Services;

namespace SistemaBiblioteca.Controllers
{
    public class UsuariosController : Controller
    {
        // Roles que NO se pueden elegir al registrarse por cuenta propia.
        // Los cargos con privilegios solo los asigna un Administrador desde Create/Edit.
        private static readonly string[] RolesRestringidos = { "Administrador", "Bibliotecario" };

        private readonly BibliotecaContext _context;
        private readonly ILogger<UsuariosController> _logger;
        private readonly IPasswordService _passwords;

        public UsuariosController(
            BibliotecaContext context,
            ILogger<UsuariosController> logger,
            IPasswordService passwords)
        {
            _context = context;
            _logger = logger;
            _passwords = passwords;
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

                // Con sal el hash es distinto en cada cuenta, así que ya no se puede
                // buscar por hash en SQL: se carga el usuario y se verifica en memoria.
                var usuario = await _context.Usuarios
                    .Include(u => u.Rol)
                    .FirstOrDefaultAsync(u => u.Correo == correo && u.Activo);

                // Mismo mensaje si el usuario no existe o la contraseña falla,
                // para no revelar qué correos están registrados.
                var resultado = usuario == null
                    ? PasswordCheck.Invalid
                    : _passwords.Verify(usuario.Contrasena, contrasena);

                if (usuario == null || resultado == PasswordCheck.Invalid)
                {
                    TempData["Error"] = "Credenciales inválidas";
                    return View();
                }

                // Migración transparente: hash antiguo (SHA-256) o parámetros débiles
                // se reemplazan por el formato actual con la contraseña recién validada.
                if (resultado == PasswordCheck.ValidNeedsRehash)
                {
                    usuario.Contrasena = _passwords.Hash(contrasena);
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
            ViewBag.Roles = await ObtenerRolesPublicosAsync();
            return View(new RegistroViewModel());
        }

        // POST: Registro (autoservicio, sin sesión)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registro(RegistroViewModel model)
        {
            // Lista blanca de roles: se calcula en el servidor y se reutiliza tanto
            // para validar el IdRol recibido como para repoblar el formulario.
            var rolesPublicos = await ObtenerRolesPublicosAsync();
            ViewBag.Roles = rolesPublicos;

            try
            {
                // Nunca confiar en el IdRol del formulario: un atacante puede enviar
                // el id de Administrador aunque el <select> no lo muestre.
                if (!rolesPublicos.Any(r => r.IdRol == model.IdRol))
                {
                    ModelState.AddModelError(nameof(model.IdRol), "Seleccione un tipo de usuario válido");
                }

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
                    // Las columnas Telefono/Direccion son NOT NULL en la BD: un campo omitido
                    // se guarda como cadena vacía en lugar de provocar un error al insertar.
                    Telefono = model.Telefono?.Trim() ?? string.Empty,
                    Direccion = model.Direccion?.Trim() ?? string.Empty,
                    IdRol = model.IdRol,
                    Correo = model.Correo,
                    Contrasena = _passwords.Hash(model.Contrasena),
                    FechaRegistro = DateTime.Now,
                    Activo = true
                };

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Usuario registrado: {Correo}", usuario.Correo);

                TempData["Success"] = "¡Cuenta creada! Ahora puedes iniciar sesión";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                // El detalle va al log; al usuario solo un mensaje genérico
                // (ex.Message puede exponer nombres de tablas o del servidor).
                _logger.LogError(ex, "Error en Registro");
                ModelState.AddModelError("", "No se pudo crear la cuenta. Intente nuevamente.");
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

            // La ficha expone DNI, correo y deuda de la persona: solo la ve el personal.
            // Un usuario común consulta lo suyo desde "Mi Perfil"; sin esta comprobación
            // cualquiera con sesión podía recorrer ids y leer datos ajenos.
            if (!ValidarRol(new[] { "Administrador", "Bibliotecario" }))
            {
                if (id == HttpContext.Session.GetInt32("UsuarioId")) return RedirectToAction(nameof(Perfil));

                TempData["Error"] = "No tiene permisos para ver este usuario";
                return RedirectToAction("Index", "Home");
            }

            var usuario = await _context.Usuarios
              .Include(u => u.Rol)
              .Include(u => u.Prestamos).ThenInclude(p => p.Ejemplar).ThenInclude(e => e.Libro)
                 .Include(u => u.Sanciones)
             .FirstOrDefaultAsync(u => u.IdUsuario == id);

            if (usuario == null) return NotFound();

            // Un préstamo vencido sigue en poder del usuario: cuenta como activo.
            ViewBag.PrestamosActivos = usuario.Prestamos.Count(p => p.EstadoPrestamo != EstadoPrestamo.Devuelto);
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

                usuario.Contrasena = _passwords.Hash(contrasena);
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
                    usuarioDb.Contrasena = _passwords.Hash(nuevaContrasena);
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

                if (_passwords.Verify(usuario.Contrasena, contrasenaActual) == PasswordCheck.Invalid)
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

                usuario.Contrasena = _passwords.Hash(nuevaContrasena);
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarcarLeida(int id)
        {
            if (!ValidarSesion()) return Json(new { success = false });

            // Filtrar también por dueño: evita que un usuario manipule notificaciones
            // ajenas adivinando ids (IDOR).
            int userId = HttpContext.Session.GetInt32("UsuarioId")!.Value;
            var notif = await _context.Notificaciones
                .FirstOrDefaultAsync(n => n.IdNotificacion == id && n.IdUsuario == userId);

            if (notif == null) return Json(new { success = false });

            notif.Leida = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // POST: MarcarTodasLeidas
        [HttpPost]
        [ValidateAntiForgeryToken]
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarNotificacion(int id)
        {
            if (!ValidarSesion()) return Json(new { success = false });

            // Filtrar también por dueño: evita que un usuario manipule notificaciones
            // ajenas adivinando ids (IDOR).
            int userId = HttpContext.Session.GetInt32("UsuarioId")!.Value;
            var notif = await _context.Notificaciones
                .FirstOrDefaultAsync(n => n.IdNotificacion == id && n.IdUsuario == userId);

            if (notif == null) return Json(new { success = false });

            _context.Notificaciones.Remove(notif);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // =============================================
        // MÉTODOS AUXILIARES
        // =============================================

        /// <summary>
        /// Roles que una persona puede elegir al registrarse sola: activos y sin privilegios.
        /// </summary>
        private async Task<List<Rol>> ObtenerRolesPublicosAsync()
        {
            return await _context.Roles
                .Where(r => r.Activo && !RolesRestringidos.Contains(r.NombreRol))
                .OrderBy(r => r.NombreRol)
                .ToListAsync();
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
