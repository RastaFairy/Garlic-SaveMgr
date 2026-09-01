using System.Security.Cryptography;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Services;

/// <summary>
/// Constantes de referencia y utilidades de verificación del payload Garlic.
///
/// Mantiene datos de referencia de la versión conocida del payload Garlic para
/// mostrar información de compatibilidad y permitir verificaciones manuales.
/// El flujo de arranque puede descargar y transferir el payload cuando el usuario lo autoriza; la versión y el hash se obtienen dinámicamente del catálogo PLDMGR.
/// </summary>
public static class PayloadCompatibility
{
    // ── Versión de referencia / fallback ──────────

    /// <summary>Etiqueta de la última versión conocida al compilar.</summary>
    public const string CurrentKnownGarlicVersion = "v1.13";

    /// <summary>URL principal del catálogo PLDMGR.</summary>
    public const string OfficialReleaseUrl = "https://shark-ps.github.io/PS5-PLDMGR-AutoUpdater/json/ps5_saves.json";

    /// <summary>
    /// URL directa de fallback a una versión conocida; el flujo normal usa la URL del catálogo.
    /// </summary>
    public const string CurrentElfUrl = "https://shark-ps.github.io/PS5-PLDMGR-AutoUpdater/json/ps5_saves.json/download/v1.13/garlic-savemgr.elf";

    /// <summary>SHA-256 esperado cuando se conoce el binario concreto. Vacío significa que no se fija hash en compilación.</summary>
    public const string CurrentSha256 = "";

    // ── Utilidades ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Calcula el SHA-256 del archivo en <paramref name="path"/> y lo compara con
    /// <see cref="CurrentSha256"/> cuando hay un hash fijado en compilación.
    /// </summary>
    public static bool VerifyElf(string path, out string sha256)
    {
        sha256 = "";
        try
        {
            using var stream = File.OpenRead(path);
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(CurrentSha256) &&
                   string.Equals(sha256, CurrentSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Write($"No se pudo verificar payload Garlic: {ex.Message}", "WARN");
            return false;
        }
    }
}
