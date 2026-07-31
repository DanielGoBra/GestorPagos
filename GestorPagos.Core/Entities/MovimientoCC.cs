namespace GestorPagos.Core.Entities
{
    /// <summary>
    /// Representa un movimiento de Comprobante Contable (CC) obtenido del ERP.
    /// Fuente: endpoint /movimientos_cc_por_fecha
    /// </summary>
    public class MovimientoCC
    {
        /// <summary>
        /// Numero del movimiento/comprobante (CC)
        /// </summary>
        public string Movimiento { get; set; } = string.Empty;

        /// <summary>
        /// Fecha del movimiento
        /// </summary>
        public DateTime Fecha { get; set; }

        /// <summary>
        /// Valor total del movimiento
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// Cedula del cliente asociado al movimiento
        /// </summary>
        public string Cedula { get; set; } = string.Empty;
    }
}
