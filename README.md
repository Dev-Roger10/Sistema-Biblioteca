<div align="center">

<img src="SistemaBiblioteca/wwwroot/icons8-biblioteca-16.png" alt="SIGB" width="32" height="32" />

# SIGB

### Sistema Integral de Gestión de Biblioteca

Catálogo, reservas, préstamos, sanciones y reportes de una biblioteca universitaria,
en una sola aplicación web con acceso según el rol de cada persona.

<br />

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-MVC-0883bc?style=for-the-badge)
![SQL Server](https://img.shields.io/badge/SQL_Server-EF_Core_9-CC2927?style=for-the-badge&logo=microsoftsqlserver&logoColor=white)
![Bootstrap](https://img.shields.io/badge/Bootstrap-5.3-7952B3?style=for-the-badge&logo=bootstrap&logoColor=white)

[Funcionalidades](#-funcionalidades) ·
[Roles](#-roles-y-permisos) ·
[Instalación](#-instalación) ·
[Arquitectura](#-arquitectura) ·
[Seguridad](#-seguridad)

</div>

---

## Sobre el proyecto

SIGB resuelve el día a día de una biblioteca universitaria: saber qué libros hay y dónde,
reservarlos, prestarlos, controlar las devoluciones y aplicar multas cuando hay retraso.

Antes, quien quería un libro tenía que ir hasta la biblioteca solo para averiguar si estaba
disponible y para pedirlo en préstamo. Con SIGB eso ya no hace falta: estudiantes y docentes
pueden consultar el catálogo y **reservar en línea** desde cualquier lugar, y pasan a la
biblioteca solo a recoger su libro. El personal, por su parte, administra el catálogo, los
préstamos y las sanciones desde un panel propio.


**Puntos clave**

- Panel de inicio con **cifras reales** de la base de datos, distintas según el rol.
- Reglas de préstamo centralizadas: límite por usuario, plazos por rol y multa diaria.
- Vencimientos calculados por fecha: el sistema siempre sabe qué está atrasado.
- Interfaz moderna y adaptable a celular, tableta y computadora.
- Sin credenciales en el código: la configuración sensible vive fuera del repositorio.

## ✨ Funcionalidades

| Módulo | Qué permite |
|---|---|
| **Inicio** | Portada para visitantes con datos del catálogo y panel personal al iniciar sesión, con métricas y accesos rápidos. |
| **Catálogo** | Búsqueda por título, autor o categoría, disponibilidad de ejemplares y ficha de cada libro. |
| **Reservas** | El lector aparta un libro (local o virtual); el personal la confirma o la cancela. |
| **Préstamos** | Registro, devolución, filtros por estado, búsqueda y detección de vencidos. |
| **Sanciones** | Multa automática por retraso, registro de pagos y consulta del estado. |
| **Notificaciones** | Avisos al usuario sobre préstamos, devoluciones y sanciones. |
| **Usuarios** | Alta, edición y baja de cuentas, y perfil propio. |
| **Catálogos de apoyo** | Categorías, editoriales y sedes. |
| **Reportes** | Resúmenes para administración y bibliotecarios. |
| **Historial** | Registro de acciones relevantes, como inicios y cierres de sesión. |

## 👥 Roles y permisos

| Rol | Qué puede hacer |
|---|---|
| **Estudiante** | Consultar el catálogo, reservar libros y ver sus reservas, préstamos y sanciones. |
| **Docente** | Lo mismo que el estudiante, con un plazo de préstamo más largo. |
| **Bibliotecario** | Lo anterior más gestión de libros, préstamos, reservas y sanciones, y acceso a reportes. |
| **Administrador** | Todo lo anterior más gestión de usuarios, categorías, editoriales y sedes. |

> El registro público solo ofrece los roles de lector. Las cuentas de Bibliotecario y
> Administrador las crea un administrador.

## 🧰 Tecnologías

| Capa | Tecnología |
|---|---|
| Backend | C#, ASP.NET Core MVC (.NET 8) |
| Acceso a datos | Entity Framework Core 9 con migraciones |
| Base de datos | SQL Server |
| Frontend | Razor, Bootstrap 5.3, Font Awesome 6, tipografía Inter |
| Autenticación | Sesión del servidor y hash PBKDF2 con sal |

## 🚀 Instalación

### Requisitos

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) o superior
- SQL Server (Express, LocalDB o Developer)
- Git
- Opcional: Visual Studio 2022 o Visual Studio Code con *C# Dev Kit*

### 1. Clonar

```powershell
git clone https://github.com/Dev-Roger10/Sistema-Biblioteca.git
cd Sistema-Biblioteca/SistemaBiblioteca
```

### 2. Configurar la conexión a la base de datos

La cadena de conexión no se guarda en el repositorio. En desarrollo se registra con
*user-secrets*, que viven fuera del proyecto:

```powershell
dotnet user-secrets set "ConnectionStrings:BibliotecaConnection" "Server=localhost;Database=Biblioteca;User Id=TU_USUARIO;Password=TU_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

<details>
<summary>Ejemplo con SQL Server Express y autenticación de Windows</summary>

```powershell
dotnet user-secrets set "ConnectionStrings:BibliotecaConnection" "Server=.\SQLEXPRESS;Database=Biblioteca;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

</details>

`appsettings.example.json` es una plantilla de referencia con valores de ejemplo.

### 3. Crear la base de datos

```powershell
dotnet tool restore
dotnet ef database update
```

### 4. Datos iniciales

La aplicación espera que en la tabla `Roles` existan **Administrador**, **Bibliotecario**,
**Docente** y **Estudiante**, y al menos una cuenta de administrador para gestionar al resto.
El catálogo (libros, categorías, editoriales, sedes y ejemplares) se carga desde la propia
aplicación.

### 5. Ejecutar

```powershell
dotnet run --launch-profile https
```

La aplicación abre en `https://localhost:7163`. Si el navegador avisa del certificado,
ejecuta una vez `dotnet dev-certs https --trust`.

En Visual Studio abre `SistemaBiblioteca.sln` y pulsa **F5**. En VS Code usa la terminal
integrada con los mismos comandos.

### Despliegue

Fuera de desarrollo no se usan user-secrets: define la cadena de conexión como variable de
entorno.

```powershell
$env:ConnectionStrings__BibliotecaConnection = "<cadena de conexión>"
```

Si falta, la aplicación se detiene al arrancar con un mensaje que explica cómo configurarla.

## ⚙️ Configuración del sistema

Estos parámetros se leen de la tabla `ConfiguracionSistema`; si no existen se usa el valor
por defecto.

| Clave | Por defecto | Descripción |
|---|---|---|
| `DiasMaximoPrestamo` | 14 | Días de préstamo para estudiantes |
| `DiasMaximoPrestamoDocente` | 30 | Días de préstamo para docentes |
| `LimitePrestamos` | 5 | Préstamos activos simultáneos por usuario |
| `MultaDiaria` | 2.00 | Multa por cada día de retraso |

## 🏗️ Arquitectura

Patrón **MVC** con una capa de servicios para las reglas de negocio.

```
Sistema-Biblioteca/
├─ SistemaBiblioteca.sln
└─ SistemaBiblioteca/
   ├─ Controllers/        Un controlador por módulo (Libros, Prestamos, Reservas, ...)
   ├─ Models/             Entidades y constantes de estado
   │  └─ ViewModels/      Modelos pensados para las vistas
   ├─ Services/           Reglas de negocio (PrestamoService, PasswordService)
   ├─ Data/               BibliotecaContext (EF Core)
   ├─ Migrations/         Esquema de la base de datos
   ├─ Views/              Vistas Razor por módulo y layout compartido
   ├─ wwwroot/            CSS (app.css, home.css, auth.css), JS e íconos
   ├─ Program.cs          Arranque y servicios
   └─ appsettings.example.json
```

### Reglas de negocio

- Un usuario no puede superar su límite de préstamos activos.
- El plazo de devolución depende del rol.
- Un préstamo está **vencido** cuando no se devolvió y pasó la fecha esperada. El estado se
  calcula por fecha, no se almacena.
- Al devolver con retraso se genera una **sanción** con multa diaria y se notifica al usuario.
- Toda la lógica de préstamos y devoluciones vive en `PrestamoService`, así todos los
  controladores aplican las mismas reglas.

## 🔒 Seguridad

- **Sin secretos en el repositorio.** `appsettings.json` y `appsettings.Production.json`
  están en `.gitignore`; la conexión se entrega por *user-secrets* o variable de entorno.
- **Contraseñas protegidas** con PBKDF2 (HMAC-SHA512) y sal aleatoria. Las cuentas con un hash
  antiguo se migran solas al iniciar sesión.
- **Login sin filtraciones:** el mensaje de error es el mismo si el correo no existe o la
  contraseña falla.
- **Protección CSRF** con token anti falsificación en los formularios de escritura.
- **Sesión segura:** cookie `HttpOnly`, solo por HTTPS y con expiración a los 30 minutos sin
  actividad.
- **Control por rol** en cada módulo; el registro público no permite elegir roles de personal.

> No subas nunca `appsettings.json`, `appsettings.Production.json`, copias de seguridad de la
> base de datos ni archivos `.env`.

## 🎨 Interfaz

- Paleta de marca: `#007f90` · `#0d456b` · `#0883bc` · `#08cdff` · `#0ffff8`
- Diseño responsivo para celular, tableta y computadora.
- Sistema de estilos compartido (`app.css`) con tarjetas de métrica, etiquetas de estado,
  filtros, avatares con iniciales, tablas y formularios homogéneos.
- Inicio, login y registro compactos para que quepan en pantalla a 100% de zoom.

## 🗺️ Próximas mejoras

- [ ] Límite de intentos en el inicio de sesión
- [ ] Cierre de sesión mediante `POST`
- [ ] Restringir `AllowedHosts` en producción
- [ ] Recuperación de contraseña
- [ ] Pruebas automatizadas de los servicios

## 🛠️ Solución de problemas

| Síntoma | Solución |
|---|---|
| `Falta la cadena de conexión 'BibliotecaConnection'` | Configura los user-secrets (paso 2). |
| `...SistemaBiblioteca.exe is being used by another process` | La app sigue en ejecución: deténla con `Ctrl+C` antes de volver a compilar. |
| Error de certificado en el navegador | Ejecuta `dotnet dev-certs https --trust`. |
| No conecta con SQL Server | Verifica que el servicio esté activo y el nombre del servidor en la cadena. |
| Los estilos no se actualizan | Recarga sin caché con `Ctrl+F5`. |

---

<div align="center">

Desarrollado por [Dev-Roger10](https://github.com/Dev-Roger10) como proyecto universitario.

</div>
