using System;
using System.Security.Cryptography;
using System.Text;

namespace GestorPagos.Core.Entities
{
    public class Pago
    {
        // Identificadores
        public Guid Id { get; set; }  // Ahora tiene setter para permitir asignación desde GenerarIdUnico
        public string CodigoPago { get; set; } = string.Empty;  // ID único legible: PAG-20251127-00001
        
        /// <summary>
        /// Hash único del pago basado en Cédula + Monto + FechaConsignación
        /// Permite detectar duplicados incluso si se importa el mismo archivo varias veces
        /// </summary>
        public string HashUnico { get; set; } = string.Empty;
        
        // ============================================================
        // COLUMNAS DESTINO EXCEL - Según plantilla Fiduciaria
        // ============================================================
        
        public int Consecutivo { get; set; }                         // A: CONSECUTIVO (auto-generado)
        public string CedulaCorrecta { get; set; } = string.Empty;   // B: CEDULA (la correcta, corregida por gestores)
        public string CedulaCliente { get; set; } = string.Empty;    // C: BANCO (la cédula con la que entró el pago)
        public string? Titular { get; set; }                         // NUEVO: Nombre del titular (Fiduciaria) - Nullable
        public decimal Monto { get; set; }                           // D: MONTO
        public DateTime FechaConsignacion { get; set; }              // E: FECHA CONSIG
        public string Convenio { get; set; } = string.Empty;         // F: CONVENIO (Efecty, Bancolombia-App, PSE, Wompi)
        public string NumeroAprobacion { get; set; } = string.Empty; // G: N° APROBACIÓN (Id Transacción, puede estar vacío)
        
        // ============================================================
        // Campos de resultado/procesamiento (columnas del Excel)
        // ============================================================
        public string Resultado { get; set; } = string.Empty;   // H: RESULTADO - Aplicado/No Aplicado
        public string Error { get; set; } = string.Empty;       // I: ERROR - Descripción del error si hay
        public string Asumido { get; set; } = string.Empty;     // J: ASUMIDO - Valor asumido por la empresa
        public string SaldoP { get; set; } = string.Empty;      // K: SALDO PENDIENTE
        public string Observacion { get; set; } = string.Empty; // L: OBSERVACIÓN
        
        // ============================================================
        // Consecutivo del ERP (CC) - Viene del sistema contable
        // ============================================================
        public string? CC { get; set; }             // M: CONSECUTIVO CC - Consecutivo generado por el ERP
        public string? FechaAplicacion { get; set; } // N: FECHA APLICACIÓN
        public string? Credito { get; set; }        // O: CRÉDITO - Número del crédito al que se aplicó
        
        // ============================================================
        // Desglose del pago aplicado (viene del ERP/RPA)
        // ============================================================
        public decimal Capital { get; set; }        // P: CAPITAL
        public decimal Intereses { get; set; }      // Q: INTERESES
        public decimal Cargos { get; set; }         // R: CARGOS (suma_O)
        public decimal Mora { get; set; }           // S: MORA
        public decimal Condonacion { get; set; }    // T: CONDONACION (total_CO)
        public decimal TotalAplicado { get; set; }  // U: TOTAL APLICADO
        
        // ============================================================
        // Informacion adicional del credito (nuevas columnas despues de TOTAL APLIC)
        // ============================================================
        public int? CuotaActual { get; set; }       // U: CUOTA - Numero de cuota actual
        public int? CuotasPendientes { get; set; }  // V: CUOTAS PEND - Cantidad de cuotas pendientes
        public int? DiasMora { get; set; }          // W: DIAS MORA - Dias de mora del credito
        public string? FechaProximaCuota { get; set; } // X: PROX CUOTA - Fecha de vencimiento de la proxima cuota
        // Excedente se calcula: Monto - TotalAplicado (ver propiedad calculada abajo)
        public string? ListaCreditos { get; set; }  // Z: CREDITOS - Lista de creditos si tiene mas de 1
        public bool EsPagoDoble { get; set; }       // Flag: PAGO DOBLE - Si la cedula aparece mas de una vez
        public decimal? ValorCuota { get; set; }    // Valor de la cuota segun ERP (para comparar)
        
        // ============================================================
        // Campos de prediccion XEO (se llenan al consultar XEO ANTES del RPA)
        // Estos campos NO se sobrescriben cuando se importa el resultado del RPA
        // ============================================================
        public string? XEO_Error { get; set; }       // Prediccion de error (Inferior/Sin Novedad/Superior)
        public string? XEO_SaldoP { get; set; }      // Saldo pendiente calculado
        public string? XEO_Observacion { get; set; } // Observacion con info de cuota
        
        /// <summary>
        /// Suma del desglose: Capital + Intereses + Cargos + Mora (sin condonación, que no es dinero que entra)
        /// </summary>
        public decimal SumaDesglose => Capital + Intereses + Cargos + Mora;
        
        /// <summary>
        /// Indica si hay datos de aplicación (al menos TotalAplicado o algún desglose)
        /// </summary>
        public bool TieneDatosAplicacion => TotalAplicado > 0 || SumaDesglose > 0;
        
        /// <summary>
        /// Excedente: dinero sobrante que va a la próxima cuota o queda como saldo a favor
        /// </summary>
        public decimal Excedente => TotalAplicado > 0 ? Math.Max(0, Monto - TotalAplicado) : 0;
        
        /// <summary>
        /// Indica si el pago cuadra:
        /// - TotalAplicado > 0 (se aplicó algo en XEO)
        /// - TotalAplicado <= Monto (no se aplicó más de lo pagado)
        /// - SumaDesglose == TotalAplicado (el desglose K+I+O+M suma correctamente)
        /// El excedente (Monto - TotalAplicado) es normal y esperado
        /// </summary>
        public bool EstaCuadrado
        {
            get
            {
                // Si tiene datos de aplicación del desglose XEO
                if (TieneDatosAplicacion)
                {
                    // Cuadra si:
                    // 1. Se aplicó algo (TotalAplicado > 0)
                    // 2. No se aplicó más de lo que pagó (TotalAplicado <= Monto)
                    // 3. El desglose suma correctamente (SumaDesglose == TotalAplicado)
                    bool aplicacionValida = TotalAplicado > 0 && TotalAplicado <= Monto;
                    bool desgloseValido = SumaDesglose == 0 || SumaDesglose == TotalAplicado;
                    
                    return aplicacionValida && desgloseValido;
                }
                
                // Si está marcado como APLICADO pero no tiene desglose aún,
                // NO se considera cuadrado — falta obtener Capital/Intereses/Mora/Cargos
                if (Estado == EstadoPago.Aplicado)
                {
                    return false;
                }
                
                return false;
            }
        }
        
        /// <summary>
        /// Indica si el pago NO cuadra (hay datos pero no coinciden)
        /// </summary>
        public bool NoCuadra => TieneDatosAplicacion && !EstaCuadrado;
        
        /// <summary>
        /// Texto para mostrar en columna CUADRA:
        /// - SI: si cuadra (TotalAplicado <= Monto y desglose correcto)
        /// - NO: si hay problema (TotalAplicado > Monto o desglose no suma)
        /// - —: si no hay datos y no está aplicado
        /// </summary>
        public string TextoCuadra
        {
            get
            {
                if (Estado == EstadoPago.Aplicado)
                {
                    // Si está aplicado y tiene desglose, verificar si cuadra
                    if (TieneDatosAplicacion)
                    {
                        return EstaCuadrado ? "SI" : "NO";
                    }
                    // Aplicado sin desglose = asumimos que cuadra
                    return "SI*"; // Asterisco indica "pendiente verificar desglose"
                }
                
                if (TieneDatosAplicacion)
                {
                    return EstaCuadrado ? "SI" : "NO";
                }
                
                return "—";
            }
        }
        
        /// <summary>
        /// Diferencia entre Monto y Total Aplicado (positivo = faltó, negativo = sobró)
        /// </summary>
        public decimal DiferenciaCuadre => Monto - TotalAplicado;
        
        /// <summary>
        /// Diferencia entre TotalAplicado y la suma del desglose
        /// </summary>
        public decimal DiferenciaDesglose => TotalAplicado - SumaDesglose;
        
        /// <summary>
        /// Indica si tiene mora significativa (más de 30 días)
        /// </summary>
        public bool TieneMoraAlta => DiasMora.HasValue && DiasMora.Value > 30;
        
        /// <summary>
        /// Devuelve los últimos 4 dígitos del ID único para referencia rápida
        /// </summary>
        public string IdCorto => Id.ToString().Substring(Id.ToString().Length - 4).ToUpper();
        
        /// <summary>
        /// Texto legible del estado para mostrar en la UI
        /// </summary>
        public string EstadoTexto => Estado switch
        {
            EstadoPago.Importado => "NUEVO",
            EstadoPago.Validado => "VALIDADO",
            EstadoPago.ConError => "ERROR",
            EstadoPago.Corregido => "CORREGIDO",
            EstadoPago.Aplicado => "APLICADO",
            EstadoPago.NoCoincide => "SIN COINC.",
            _ => Estado.ToString().ToUpper()
        };
        
        // ============================================================
        // Trazabilidad - Importación
        // ============================================================
        public DateTime FechaImportacion { get; set; }
        public string UsuarioImportacion { get; set; } = string.Empty;
        public string EquipoImportacion { get; set; } = string.Empty;
        public string ArchivoOrigen { get; set; } = string.Empty;
        
        // Trazabilidad - Modificación
        public DateTime? FechaModificacion { get; set; }
        public string? UsuarioModificacion { get; set; }
        
        // Trazabilidad - Exportación
        public DateTime? FechaExportacion { get; set; }
        public string? UsuarioExportacion { get; set; }
        public string? ArchivoExportado { get; set; }
        
        // ============================================================
        // Auditoría - Procesamiento y Bloqueo
        // ============================================================
        
        /// <summary>
        /// Usuario que consultó el desglose (confirma que el pago cuadra)
        /// </summary>
        public string? UsuarioDesglose { get; set; }
        
        /// <summary>
        /// Fecha/hora en que se consultó el desglose
        /// </summary>
        public DateTime? FechaDesglose { get; set; }
        
        /// <summary>
        /// Indica si el pago está bloqueado (ya cuadró y no se puede modificar)
        /// </summary>
        public bool Bloqueado { get; set; }
        
        /// <summary>
        /// Fecha/hora en que se bloqueó el pago
        /// </summary>
        public DateTime? FechaBloqueo { get; set; }
        
        /// <summary>
        /// Usuario que bloqueó el pago (normalmente quien consultó el desglose que cuadró)
        /// </summary>
        public string? UsuarioBloqueo { get; set; }
        
        // Estado del pago
        public EstadoPago Estado { get; set; } = EstadoPago.Importado;

        public Pago()
        {
            Id = Guid.NewGuid();
            // PostgreSQL requiere fechas en UTC
            FechaImportacion = DateTime.UtcNow;
            FechaConsignacion = DateTime.UtcNow;
        }

        /// <summary>
        /// Genera un GUID determinístico basado en Cédula + Monto + FechaConsignación.
        /// El mismo pago siempre generará el mismo ID.
        /// </summary>
        public static Guid GenerarIdUnico(string cedula, decimal monto, DateTime fechaConsignacion)
        {
            // Crear string único con los datos del pago
            // Formato: CEDULA|MONTO|FECHA (fecha solo día, sin hora)
            var datos = $"{cedula?.Trim()}|{monto:F2}|{fechaConsignacion:yyyyMMdd}";
            
            // Generar hash MD5 (16 bytes = tamaño exacto de un GUID)
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(datos));
                return new Guid(hash);
            }
        }

        /// <summary>
        /// Genera el hash único del pago para detectar duplicados
        /// </summary>
        public static string GenerarHashUnico(string cedula, decimal monto, DateTime fechaConsignacion)
        {
            var datos = $"{cedula?.Trim()}|{monto:F2}|{fechaConsignacion:yyyyMMdd}";
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(datos));
                // Retornar los primeros 16 caracteres del hash en hexadecimal
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        /// <summary>
        /// Inicializa el ID y Hash basado en los datos del pago.
        /// Llamar después de asignar Cédula, Monto y FechaConsignación.
        /// </summary>
        public void GenerarIdentificadores()
        {
            Id = GenerarIdUnico(CedulaCliente, Monto, FechaConsignacion);
            HashUnico = GenerarHashUnico(CedulaCliente, Monto, FechaConsignacion);
        }

        /// <summary>
        /// Convierte DateTime local a UTC para PostgreSQL
        /// </summary>
        public static DateTime ToUtc(DateTime fecha)
        {
            if (fecha.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
            return fecha.ToUniversalTime();
        }

        /// <summary>
        /// Genera el código único del pago basado en fecha y secuencia
        /// Formato: PAG-YYYYMMDD-XXXXX (ej: PAG-20251127-00001)
        /// </summary>
        public static string GenerarCodigoPago(DateTime fecha, int secuencia)
        {
            return $"PAG-{fecha:yyyyMMdd}-{secuencia:D5}";
        }
    }

    public enum EstadoPago
    {
        // Estados iniciales
        Importado = 1,          // Recién importado del archivo plano
        Validado = 2,           // Validado contra el ERP

        // Estados de error
        ConError = 3,           // Error en la importación o validación
        Corregido = 4,          // Corregido después de un error

        // Estados de conciliación API
        Aplicado = 6,           // Confirmado: Aplicado en el sistema (match CC por Cédula+Monto)

        // Estado de no coincidencia
        NoCoincide = 11         // No se encontró movimiento CC correspondiente
    }
}
