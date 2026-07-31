using System;

namespace GestorPagos.Core.Entities
{
    /// <summary>
    /// Registra cada cambio realizado a un pago para auditoría completa
    /// </summary>
    public class HistorialPago
    {
        public Guid Id { get; private set; }
        public Guid PagoId { get; set; }
        public string CodigoPago { get; set; } = string.Empty;
        
        // Qué se hizo
        public TipoAccion Accion { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        
        // Valores antes/después (para cambios)
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
        public string? CampoModificado { get; set; }
        
        // Quién y cuándo
        public DateTime FechaAccion { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Equipo { get; set; } = string.Empty;
        public string DireccionIP { get; set; } = string.Empty;

        public HistorialPago()
        {
            Id = Guid.NewGuid();
            FechaAccion = DateTime.UtcNow;  // UTC para PostgreSQL
        }

        public static HistorialPago CrearRegistro(Guid pagoId, string codigoPago, TipoAccion accion, string descripcion, string usuario, string equipo)
        {
            return new HistorialPago
            {
                PagoId = pagoId,
                CodigoPago = codigoPago,
                Accion = accion,
                Descripcion = descripcion,
                Usuario = usuario,
                Equipo = equipo,
                FechaAccion = DateTime.UtcNow  // Asegurar UTC
            };
        }
    }

    public enum TipoAccion
    {
        Importado = 1,
        Modificado = 2,
        Corregido = 3,
        Exportado = 4,
        Eliminado = 5,
        Validado = 6,
        ErrorDetectado = 7
    }
}
