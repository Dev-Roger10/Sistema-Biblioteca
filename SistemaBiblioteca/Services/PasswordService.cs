using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace SistemaBiblioteca.Services
{
    /// <summary>
    /// Resultado de verificar una contraseña contra su hash almacenado.
    /// </summary>
    public enum PasswordCheck
    {
        /// <summary>La contraseña no coincide.</summary>
        Invalid,

        /// <summary>La contraseña coincide y el hash está al día.</summary>
        Valid,

        /// <summary>
        /// La contraseña coincide, pero el hash usa un formato antiguo o parámetros
        /// débiles: el llamador debe volver a hashearla y guardarla.
        /// </summary>
        ValidNeedsRehash
    }

    /// <summary>
    /// Contrato para hashear y verificar contraseñas. Centraliza el algoritmo para que
    /// ningún controlador conozca detalles criptográficos.
    /// </summary>
    public interface IPasswordService
    {
        /// <summary>Devuelve el hash seguro (con sal y parámetros incluidos) de la contraseña.</summary>
        string Hash(string password);

        /// <summary>Comprueba la contraseña contra el hash almacenado.</summary>
        PasswordCheck Verify(string storedHash, string password);
    }

    /// <summary>
    /// Implementación basada en PBKDF2 (HMAC-SHA512) con sal aleatoria por contraseña,
    /// provista por <see cref="PasswordHasher{TUser}"/> de ASP.NET Core.
    ///
    /// Compatibilidad: las cuentas creadas antes de este cambio guardan SHA-256 sin sal
    /// (64 caracteres hexadecimales). Se siguen aceptando para no bloquear a nadie, pero
    /// <see cref="Verify"/> pide el rehash: en el siguiente login correcto el hash
    /// se migra solo al formato nuevo, sin intervención manual ni migración de BD.
    /// </summary>
    public class PasswordService : IPasswordService
    {
        // El hasher no usa la instancia de usuario; object evita acoplarse al modelo.
        private static readonly object NoUser = new();
        private readonly PasswordHasher<object> _hasher = new();

        public string Hash(string password)
        {
            ArgumentException.ThrowIfNullOrEmpty(password);
            return _hasher.HashPassword(NoUser, password);
        }

        public PasswordCheck Verify(string storedHash, string password)
        {
            if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(password))
                return PasswordCheck.Invalid;

            if (EsHashLegado(storedHash))
            {
                return VerificarLegado(storedHash, password)
                    ? PasswordCheck.ValidNeedsRehash
                    : PasswordCheck.Invalid;
            }

            return _hasher.VerifyHashedPassword(NoUser, storedHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Valid,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.ValidNeedsRehash,
                _ => PasswordCheck.Invalid
            };
        }

        // SHA-256 en hexadecimal son exactamente 64 caracteres [0-9a-f].
        private static bool EsHashLegado(string hash) =>
            hash.Length == 64 && hash.All(Uri.IsHexDigit);

        private static bool VerificarLegado(string storedHash, string password)
        {
            byte[] calculado = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            byte[] esperado = Convert.FromHexString(storedHash);

            // Comparación en tiempo constante: evita filtrar información por tiempos de respuesta.
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
    }
}
