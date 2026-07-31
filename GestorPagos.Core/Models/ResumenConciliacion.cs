namespace GestorPagos.Core.Models
{
    /// <summary>
    /// Resumen del proceso de conciliacion de pagos contra la API.
    /// </summary>
    public class ResumenConciliacion
    {
        public int TotalPagos { get; set; }
        public int PagosConciliados { get; set; }
        public int PagosSinCoincidencia { get; set; }
        public int PagosConNombre { get; set; }
        public int PagosConDesglose { get; set; }
        public int PagosBloqueados { get; set; }
        public int Errores { get; set; }
        public string MensajeFinal { get; set; } = string.Empty;
    }
}
