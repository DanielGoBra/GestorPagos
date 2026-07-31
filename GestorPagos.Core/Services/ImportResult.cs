using System.Collections.Generic;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    public class ImportResult
    {
        public List<Pago> PagosValidos { get; set; } = new List<Pago>();
        public List<ExcepcionPago> Excepciones { get; set; } = new List<ExcepcionPago>();
        public string TipoConvenioDetectado { get; set; } = string.Empty;
        public int TotalLineasProcesadas => PagosValidos.Count + Excepciones.Count;
    }
}
