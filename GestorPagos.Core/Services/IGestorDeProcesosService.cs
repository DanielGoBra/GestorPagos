using GestorPagos.Core.Entities;
using GestorPagos.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GestorPagos.Core.Services
{
    public interface IGestorDeProcesosService
    {
        /// <summary>
        /// Procesa el desglose de pagos consultando al ERP.
        /// </summary>
        Task<ResumenDesglose> ProcesarDesgloseAsync(
            IEnumerable<Pago> pagos, 
            string usuario, 
            DateTime fechaActual,
            IProgress<string> logger, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Valida los pagos contra el ERP (titular, créditos, mora, etc).
        /// </summary>
        Task<ResumenValidacion> ValidarPagosAsync(
            IEnumerable<Pago> pagos,
            string usuario,
            string equipo,
            IProgress<string> logger,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Consulta posibles desgloses para un pago manual (agrupados por crédito).
        /// </summary>
        Task<List<DesglosePagoXEO>> ConsultarPosibleDesgloseAsync(
            string cedula, 
            DateTime fecha, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Aplica un desglose (o múltiples en caso de división) a un pago existente, creando nuevos pagos si es necesario.
        /// Retorna la lista de NUEVOS pagos credos (si hubo división).
        /// </summary>
        Task<List<Pago>> AplicarDesgloseEnPagoAsync(
            Pago pagoOriginal, 
            List<DesglosePagoXEO> itemsAProcesar, 
            string usuario,
            string equipo,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Concilia pagos importados contra movimientos CC del API.
        /// Flujo: obtener movimientos CC por fecha -> cruzar por Cedula+Monto ->
        ///        obtener nombres -> obtener desglose -> bloquear si cuadra.
        /// soloNuevos=true: Solo procesa pagos con estado Importado/Corregido.
        /// soloNuevos=false: Procesa todos los no-aplicados (incluye NoCoincide).
        /// </summary>
        Task<ResumenConciliacion> ConciliarPagosConApiAsync(
            IEnumerable<Pago> pagos,
            string usuario,
            string equipo,
            IProgress<string> logger,
            bool soloNuevos = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Exporta y bloquea pagos conciliados en un rango de fechas.
        /// Trabaja SOLO contra la BD local, NO llama APIs del ERP.
        /// Flujo: consultar BD → bloquear conciliados → exportar Excel → marcar exportados.
        /// </summary>
        Task<ResumenExportacion> ExportarYBloquearFiduciariaAsync(
            DateTime startDate, DateTime endDate,
            string usuario, string equipo, string carpetaDestino,
            IProgress<string> logger,
            CancellationToken cancellationToken = default);
    }
}
