using System.Collections.Generic;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    /// <summary>
    /// Interface para el servicio de procesamiento de archivos planos
    /// </summary>
    public interface IArchivoPlanoService
    {
        /// <summary>
        /// Detecta si un archivo es plano (posición fija) o CSV/delimitado
        /// </summary>
        bool EsArchivoPlano(string rutaArchivo);

        /// <summary>
        /// Detecta el tipo de archivo plano basándose en el contenido
        /// </summary>
        TipoArchivoPlano DetectarTipoArchivoPlano(string rutaArchivo);

        /// <summary>
        /// Procesa un archivo plano de Bancolombia
        /// </summary>
        Task<ImportResult> ProcesarArchivoPlanoBancolombiaAsync(string rutaArchivo);

        /// <summary>
        /// Procesa un archivo plano de Efecty (.lst)
        /// </summary>
        Task<ImportResult> ProcesarArchivoPlanoEfectyAsync(string rutaArchivo);

        /// <summary>
        /// Genera el reporte CSV de pagos con múltiples créditos.
        /// </summary>
        Task<string> GenerarReporteMultiplesCreditosAsync(List<Pago> pagosMultiples, string carpetaDestino, string nombreConvenio);
    }
}
