using System.Collections.Generic;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    public interface IExcelExportService
    {
        /// <summary>
        /// Exporta todos los pagos a Excel (para area operativa)
        /// </summary>
        Task ExportarPagosAsync(List<Pago> pagos, string rutaSalida);
        
        /// <summary>
        /// Exporta datos simplificados a CSV para Google Sheets (para area de BI)
        /// Columnas: Cedula, Resultado, Error, SaldoPendiente, Credito, ProximaCuota
        /// </summary>
        Task ExportarParaGoogleSheetsAsync(List<Pago> pagos, string rutaSalida);
        
        /// <summary>
        /// Agrega pagos APLICADOS a un archivo Excel acumulativo semanal (para reporte fiduciaria)
        /// El archivo se crea si no existe, o se agregan registros si ya existe
        /// Solo incluye pagos con Resultado = "Aplicado"
        /// </summary>
        /// <param name="pagos">Lista de pagos a agregar</param>
        /// <param name="carpetaDestino">Carpeta donde se guarda el archivo acumulativo</param>
        /// <returns>Ruta del archivo y cantidad de registros agregados</returns>
        Task<(string Ruta, int NuevosRegistros, int TotalRegistros)> AgregarPagosAcumulativoAsync(List<Pago> pagos, string carpetaDestino);

        /// <summary>
        /// Exporta pagos bloqueados/conciliados a un archivo Excel con formato estricto de fiduciaria.
        /// Maneja IOException si el archivo está abierto por el usuario.
        /// </summary>
        /// <param name="pagos">Lista de pagos ya filtrados (bloqueados + aplicados)</param>
        /// <param name="carpetaDestino">Carpeta donde se guarda el archivo</param>
        /// <param name="startDate">Fecha inicio del rango (para nombre del archivo)</param>
        /// <param name="endDate">Fecha fin del rango (para nombre del archivo)</param>
        /// <returns>Ruta del archivo generado y cantidad de registros</returns>
        Task<(string Ruta, int TotalRegistros)> ExportarPagosFiduciariaAsync(
            List<Pago> pagos, string carpetaDestino, DateTime startDate, DateTime endDate);
    }
}
