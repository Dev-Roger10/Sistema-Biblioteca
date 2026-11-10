using Microsoft.EntityFrameworkCore;
using SistemaBiblioteca.Data;
using SistemaBiblioteca.Services;

var builder = WebApplication.CreateBuilder(args);

// Servicios MVC
builder.Services.AddControllersWithViews();

// Cadena de conexión: nunca se versiona en el repositorio.
// Orden de resolución (la última fuente gana):
//   appsettings.json -> appsettings.{Entorno}.json -> user-secrets (solo Development)
//   -> variable de entorno ConnectionStrings__BibliotecaConnection
// Si falta, fallamos al arrancar con un mensaje claro en lugar de un error
// críptico la primera vez que se consulte la base de datos.
var connectionString = builder.Configuration.GetConnectionString("BibliotecaConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión 'BibliotecaConnection'. " +
        "En desarrollo use: dotnet user-secrets set \"ConnectionStrings:BibliotecaConnection\" \"<cadena>\". " +
        "En producción use la variable de entorno ConnectionStrings__BibliotecaConnection " +
        "o un appsettings.Production.json fuera del control de versiones. " +
        "Vea appsettings.example.json como referencia.");
}

builder.Services.AddDbContext<BibliotecaContext>(options =>
    options.UseSqlServer(connectionString));

// Sesión para autenticación
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Hash de contraseñas (PBKDF2 con sal). Sin estado, por eso Singleton.
builder.Services.AddSingleton<IPasswordService, PasswordService>();

// Reglas de préstamos y devoluciones. Scoped porque comparte el DbContext de la petición.
builder.Services.AddScoped<IPrestamoService, PrestamoService>();

// Acceso al HttpContext fuera de los controladores
builder.Services.AddHttpContextAccessor();

// Caché en memoria
builder.Services.AddMemoryCache();

var app = builder.Build();

// Pipeline HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// La sesión debe habilitarse antes de la autorización y de los endpoints
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
