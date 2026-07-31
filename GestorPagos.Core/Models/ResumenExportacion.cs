namespace GestorPagos.Core.Models
{
    /// <summary>
    /// Resumen del proceso de exportación y bloqueo para fiduciaria.
    /// </summary>
    public class ResumenExportacion
    {
        public int TotalConsultados { get; set; }
        public int TotalBloqueados { get; set; }
        public int TotalYaBloqueados { get; set; }
        public int TotalExportados { get; set; }
        public string RutaArchivo { get; set; } = string.Empty;
        public string MensajeFinal { get; set; } = string.Empty;
    }
}
