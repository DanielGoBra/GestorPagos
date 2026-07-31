using System.Collections.Generic;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    /// <summary>
    /// Servicio para integración con el ERP (API XEO)
    /// </summary>
    public interface IErpService
    {
        /// <summary>
        /// Evento para notificar logs de actividad
        /// </summary>
        event Action<string> OnLog;

        /// <summary>
        /// Consulta los créditos activos de un cliente por su cédula
        /// </summary>
        /// <param name="cedula">Número de cédula del cliente</param>
        /// <returns>Información del cliente con sus créditos</returns>
        Task<ClienteERP?> ConsultarClientePorCedulaAsync(string cedula);
        
        /// <summary>
        /// Valida un pago contra el ERP
        /// </summary>
        /// <param name="cedula">Cédula del cliente</param>
        /// <returns>Resultado de la validación</returns>
        Task<ResultadoValidacionERP> ValidarPagoAsync(string cedula);
        
        /// <summary>
        /// Valida múltiples pagos contra el ERP (en lote)
        /// </summary>
        /// <param name="cedulas">Lista de cédulas a validar</param>
        /// <returns>Diccionario con resultados por cédula</returns>
        Task<Dictionary<string, ResultadoValidacionERP>> ValidarPagosEnLoteAsync(IEnumerable<string> cedulas);
        
        /// <summary>
        /// Consulta el desglose de pagos aplicados para un cliente en una fecha específica
        /// </summary>
        /// <param name="cedula">Cédula del cliente</param>
        /// <param name="fecha">Fecha de aplicación del pago</param>
        /// <returns>Lista de desgloses de pagos aplicados ese día</returns>
        Task<List<DesglosePagoXEO>> ConsultarDesglosePagosAsync(string cedula, DateTime fecha);
        
        /// <summary>
        /// Consulta el desglose de pagos aplicados para múltiples clientes en una fecha
        /// </summary>
        /// <param name="cedulas">Lista de cédulas</param>
        /// <param name="fecha">Fecha de aplicación</param>
        /// <returns>Diccionario con desgloses por cédula</returns>
        Task<Dictionary<string, List<DesglosePagoXEO>>> ConsultarDesglosePagosEnLoteAsync(IEnumerable<string> cedulas, DateTime fecha);
        
        /// <summary>
        /// Consulta el desglose de movimientos por número de comprobante (CC)
        /// Usa el endpoint /desglose_movimientos
        /// </summary>
        /// <param name="numeroComprobante">Número de comprobante CC del ERP</param>
        /// <returns>Desglose con Capital (K), Intereses (I), Otros (O), Mora (M), Total CC, Total CO</returns>
        Task<DesgloseMovimientoXEO?> ConsultarDesgloseMovimientosAsync(string numeroComprobante);
        
        /// <summary>
        /// Consulta el desglose de movimientos para múltiples comprobantes
        /// </summary>
        /// <param name="comprobantes">Lista de números de comprobante</param>
        /// <returns>Diccionario con desgloses por comprobante</returns>
        Task<Dictionary<string, DesgloseMovimientoXEO?>> ConsultarDesgloseMovimientosEnLoteAsync(IEnumerable<string> comprobantes);
        
        /// <summary>
        /// Verifica si la conexión con el ERP está disponible
        /// </summary>
        Task<bool> VerificarConexionAsync();
        
        /// <summary>
        /// Limpia el cache para forzar consultas frescas
        /// </summary>
        void LimpiarCache();
        
        /// <summary>
        /// Obtiene los nombres de terceros (deudores) por cédula consultando el endpoint de desembolsos
        /// </summary>
        /// <param name="cedulas">Lista de cédulas a consultar</param>
        /// <param name="fechaDesembolso">Fecha para buscar en reserva_desembolsos_por_fecha (opcional, usa fecha actual si null)</param>
        /// <returns>Diccionario cédula -> nombre</returns>
        Task<Dictionary<string, string>> ObtenerNombresTercerosAsync(IEnumerable<string> cedulas, DateTime? fechaDesembolso = null);

        /// <summary>
        /// Consulta los movimientos CC aplicados en el ERP desde una fecha dada.
        /// Endpoint: /movimientos_cc_por_fecha
        /// </summary>
        /// <param name="fecha">Fecha desde la cual consultar movimientos</param>
        /// <returns>Lista de movimientos CC con cedula, monto y numero de comprobante</returns>
        Task<List<MovimientoCC>> ConsultarMovimientosCCPorFechaAsync(DateTime fecha);
    }
}
