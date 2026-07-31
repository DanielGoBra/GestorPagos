using System.Collections.Generic;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    public interface IPagoService
    {
        /// <summary>
        /// Guarda los pagos importados en la base de datos con ID único y trazabilidad
        /// </summary>
        Task<int> GuardarPagosAsync(List<Pago> pagos, string usuario, string equipo, string archivoOrigen);

        /// <summary>
        /// Obtiene el siguiente número de secuencia para generar códigos de pago
        /// </summary>
        Task<int> ObtenerSiguienteSecuenciaAsync();

        /// <summary>
        /// Busca pagos por diferentes criterios
        /// </summary>
        Task<List<Pago>> BuscarPagosAsync(string? cedula = null, string? convenio = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null);

        /// <summary>
        /// Obtiene el historial de un pago específico
        /// </summary>
        Task<List<HistorialPago>> ObtenerHistorialAsync(Guid pagoId);

        /// <summary>
        /// Actualiza un pago y registra el cambio en el historial
        /// </summary>
        Task<bool> ActualizarPagoAsync(Pago pago, string usuario, string equipo, string descripcionCambio);

        /// <summary>
        /// Marca los pagos como exportados
        /// </summary>
        Task<int> MarcarComoExportadosAsync(List<Guid> pagoIds, string usuario, string archivoExportado);

        /// <summary>
        /// Verifica si un pago ya existe (para evitar duplicados)
        /// </summary>
        Task<bool> ExistePagoAsync(string cedula, decimal monto, DateTime fechaConsignacion, string convenio);

        /// <summary>
        /// Obtiene un pago existente por su HashUnico (Cédula + Monto + FechaConsignación)
        /// </summary>
        Task<Pago?> ObtenerPorHashAsync(string hashUnico);

        /// <summary>
        /// Obtiene pagos existentes por sus hashes únicos (para importación masiva)
        /// </summary>
        Task<Dictionary<string, Pago>> ObtenerPagosExistentesPorHashesAsync(IEnumerable<string> hashes);

        /// <summary>
        /// Ejecuta actualizaciones de esquema manuales (cuando fallan las migraciones)
        /// </summary>
        Task VerificarEsquemaAsync();

        /// <summary>
        /// Actualiza SOLO el titular del pago (útil para pagos bloqueados/aplicados)
        /// Ejecuta un comando SQL directo para evitar restricciones de bloqueo lógico
        /// </summary>
        Task<bool> ActualizarTitularAsync(Guid id, string titular);

        /// <summary>
        /// Actualiza SOLO la cédula correcta (para pagos importados antes de esta lógica)
        /// </summary>
        Task<bool> ActualizarCedulaCorrectaAsync(Guid id, string cedulaCorrecta);

        /// <summary>
        /// Bloquea en masa los pagos conciliados (Aplicado + EstaCuadrado) dentro de un rango de fechas.
        /// NO llama APIs externas, solo trabaja contra la BD local.
        /// </summary>
        /// <returns>Cantidad de pagos bloqueados en esta operación</returns>
        Task<int> BloquearPagosConciliadosAsync(DateTime startDate, DateTime endDate, string usuario);
    }
}
