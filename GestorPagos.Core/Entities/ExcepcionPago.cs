using System;

namespace GestorPagos.Core.Entities
{
    public class ExcepcionPago
    {
        public Guid Id_Excepcion { get; set; }
        public int Numero_Linea { get; set; }
        public string Linea_Original { get; set; } = string.Empty;
        public string Tipo_Error { get; set; } = string.Empty; // 'FORMATO_INVALIDO', 'SIN_CEDULA', 'MONTO_INVALIDO', 'EXCEPCION'
        public string Descripcion_Error { get; set; } = string.Empty;
        public string Estado_Resolucion { get; set; } = "PENDIENTE"; // 'PENDIENTE', 'CORREGIDO', 'IGNORADO'
        public string Usuario_Asignado { get; set; } = string.Empty;
        public DateTime Fecha_Registro { get; set; }

        public ExcepcionPago()
        {
            Id_Excepcion = Guid.NewGuid();
            Fecha_Registro = DateTime.Now;
        }
    }
}
