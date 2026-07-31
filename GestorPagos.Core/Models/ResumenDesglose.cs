namespace GestorPagos.Core.Models
{
    public class ResumenDesglose
    {
        public int TotalConsultados { get; set; }
        public int DesglosesEncontrados { get; set; }
        public int SinDesglose { get; set; }
        public int PagosCuadrados { get; set; }
        public int PagosBloqueados { get; set; }
        public int Actualizados { get; set; }
        public string MensajeFinal { get; set; } = string.Empty;
    }
}
