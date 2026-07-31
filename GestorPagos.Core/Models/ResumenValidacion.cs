using System;

namespace GestorPagos.Core.Models
{
    public class ResumenValidacion
    {
         public int PagosConsultados { get; set; }
         public int PagosValidos { get; set; }
         public int ConCredito { get; set; }
         public int MultiplesCreditos { get; set; }
         public int SinCreditos { get; set; }
         public int NoEncontrados { get; set; }
         public int Errores { get; set; }
         public int PagosDobles { get; set; }
         public int PagosMoraAlta { get; set; }
         public int PagosSinCedula { get; set; }
         public string MensajeFinal { get; set; } = string.Empty;
    }
}
