using System;
using System.Collections.Generic;
using System.Linq;

namespace GestorPagos.Core.Entities
{
    /// <summary>
    /// Información del cliente obtenida del ERP
    /// </summary>
    public class ClienteERP
    {
        public string Cedula { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public string Celular { get; set; } = string.Empty;
        public string CodigoMunicipio { get; set; } = string.Empty;
        
        /// <summary>
        /// Lista de créditos activos del cliente
        /// </summary>
        public List<CreditoERP> Creditos { get; set; } = new();
        
        /// <summary>
        /// Indica si el cliente existe en el ERP
        /// </summary>
        public bool Existe => !string.IsNullOrEmpty(Cedula);
        
        /// <summary>
        /// Cantidad de créditos activos
        /// </summary>
        public int CantidadCreditos => Creditos?.Count ?? 0;
        
        /// <summary>
        /// Indica si tiene múltiples créditos (requiere intervención manual)
        /// </summary>
        public bool TieneMultiplesCreditos => CantidadCreditos > 1;
        
        /// <summary>
        /// Indica si puede procesarse automáticamente (1 solo crédito)
        /// </summary>
        public bool PuedeProcesamientoAutomatico => CantidadCreditos == 1;
    }

    /// <summary>
    /// Información de un crédito del ERP
    /// Formato típico: 111-CR-23x.1434
    /// </summary>
    public class CreditoERP
    {
        /// <summary>
        /// Número completo del crédito (ej: CR-23-1434 o 111-CR-23x.1434)
        /// </summary>
        public string NumeroCredito { get; set; } = string.Empty;
        
        /// <summary>
        /// Tipo de crédito (ej: CR)
        /// </summary>
        public string TipoCredito { get; set; } = string.Empty;
        
        /// <summary>
        /// Línea de crédito
        /// </summary>
        public string LineaCredito { get; set; } = string.Empty;
        
        /// <summary>
        /// Número secuencial del crédito
        /// </summary>
        public string NumeroSecuencial { get; set; } = string.Empty;
        
        /// <summary>
        /// Valor original del desembolso
        /// </summary>
        public decimal ValorDesembolso { get; set; }
        
        /// <summary>
        /// Saldo actual del crédito
        /// </summary>
        public decimal SaldoActual { get; set; }
        
        /// <summary>
        /// Fecha del desembolso
        /// </summary>
        public DateTime FechaDesembolso { get; set; }
        
        /// <summary>
        /// Plazo del crédito en meses
        /// </summary>
        public int Plazo { get; set; }
        
        /// <summary>
        /// Tasa de interés E.A.
        /// </summary>
        public decimal TasaEA { get; set; }
        
        /// <summary>
        /// Estado del crédito (Activo, Mora, etc.)
        /// </summary>
        public string Estado { get; set; } = string.Empty;
        
        /// <summary>
        /// Código del fondo (EMP319, EMP023, EMP231)
        /// </summary>
        public string Fondo { get; set; } = string.Empty;
        
        /// <summary>
        /// Indica si el crédito está activo
        /// </summary>
        public bool EstaActivo => Estado?.ToUpper() == "A" || Estado?.ToUpper() == "ACTIVO";
        
        /// <summary>
        /// Lista de cuotas pendientes del crédito
        /// </summary>
        public List<CuotaERP> CuotasPendientes { get; set; } = new();
        
        /// <summary>
        /// Lista de cuotas pagadas HOY (para detectar pagos ya aplicados)
        /// </summary>
        public List<CuotaERP> CuotasPagadasHoy { get; set; } = new();
        
        /// <summary>
        /// Indica si tiene pagos aplicados hoy
        /// </summary>
        public bool TienePagosHoy => CuotasPagadasHoy?.Count > 0;
        
        /// <summary>
        /// Total a pagar (suma de cuotas pendientes)
        /// </summary>
        public decimal TotalAPagar => CuotasPendientes?.Sum(c => c.ValorCuota) ?? 0;
        
        /// <summary>
        /// Próxima cuota pendiente
        /// </summary>
        public CuotaERP? ProximaCuota => CuotasPendientes?.OrderBy(c => c.FechaVencimiento).FirstOrDefault();
    }

    /// <summary>
    /// Información de una cuota del crédito
    /// </summary>
    public class CuotaERP
    {
        public int NumeroCuota { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public int DiasMora { get; set; }
        public decimal Capital { get; set; }
        public decimal CuotaCapital { get; set; }
        public decimal CuotaInteres { get; set; }
        public decimal Mora { get; set; }
        public decimal Cargos { get; set; }
        public decimal ValorCuota { get; set; }  // Total de la cuota
        public string Estado { get; set; } = string.Empty;  // PAGADA, PENDIENTE
        public DateTime? FechaAbono { get; set; }
        
        /// <summary>
        /// Indica si la cuota está pendiente
        /// </summary>
        public bool EstaPendiente => Estado?.ToUpper() != "PAGADA";
        
        /// <summary>
        /// Indica si la cuota está pagada
        /// </summary>
        public bool EstaPagada => Estado?.ToUpper() == "PAGADA";
        
        /// <summary>
        /// Total aplicado = Capital + Interes + Mora + Cargos
        /// </summary>
        public decimal TotalAplicado => CuotaCapital + CuotaInteres + Mora + Cargos;
    }
    
    /// <summary>
    /// Resultado del desglose de pagos aplicados consultados desde XEO
    /// </summary>
    public class DesglosePagoXEO
    {
        public string Cedula { get; set; } = string.Empty;
        public string NumeroCredito { get; set; } = string.Empty;
        public int NumeroCuota { get; set; }
        public DateTime FechaAbono { get; set; }
        public decimal Capital { get; set; }
        public decimal Intereses { get; set; }
        public decimal Mora { get; set; }
        public decimal Cargos { get; set; }
        public decimal TotalAplicado { get; set; }
        
        /// <summary>
        /// Indica si se encontró información de pago
        /// </summary>
        public bool TieneDatos => TotalAplicado > 0;
    }
    
    /// <summary>
    /// Resultado del desglose de movimientos por número de comprobante (CC)
    /// Viene del endpoint /desglose_movimientos
    /// </summary>
    public class DesgloseMovimientoXEO
    {
        public string NumeroComprobante { get; set; } = string.Empty;
        public DateTime? FechaPrimera { get; set; }
        public string ClavesCredito { get; set; } = string.Empty;
        public decimal SumaK { get; set; }      // Capital
        public decimal SumaI { get; set; }      // Intereses
        public decimal SumaO { get; set; }      // Otros/Condonaciones
        public decimal SumaM { get; set; }      // Mora
        public decimal TotalCC { get; set; }    // Total tipo CC
        public decimal TotalCO { get; set; }    // Total tipo CO (Condonaciones)
        
        /// <summary>
        /// Total aplicado = K + I + O + M
        /// </summary>
        public decimal TotalAplicado => SumaK + SumaI + SumaO + SumaM;
        
        /// <summary>
        /// Indica si se encontró información
        /// </summary>
        public bool TieneDatos => TotalAplicado > 0 || TotalCC > 0;
    }

    /// <summary>
    /// Resultado de la validación de un pago contra el ERP
    /// </summary>
    public class ResultadoValidacionERP
    {
        public string Cedula { get; set; } = string.Empty;
        public bool ClienteExiste { get; set; }
        public int CantidadCreditos { get; set; }
        public string? CreditoUnico { get; set; }  // Si solo tiene 1, aquí está el número
        public List<string> NumerosCreditos { get; set; } = new();
        public EstadoValidacionERP Estado { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public ClienteERP? Cliente { get; set; }
    }

    /// <summary>
    /// Estado de la validación contra el ERP
    /// </summary>
    public enum EstadoValidacionERP
    {
        Valido = 1,              // 1 crédito activo, listo para RPA
        MultiplesCreditos = 2,   // 2+ créditos, requiere intervención
        SinCreditos = 3,         // Cliente existe pero sin créditos activos
        ClienteNoExiste = 4,     // Cédula no encontrada en ERP
        ErrorConexion = 5,       // Error al conectar con la API
        ErrorConsulta = 6        // Error en la consulta
    }
}
