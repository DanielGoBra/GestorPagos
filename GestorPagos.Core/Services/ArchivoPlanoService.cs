using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;

namespace GestorPagos.Core.Services
{
    /// <summary>
    /// Servicio para procesar archivos planos (posición fija) de las diferentes plataformas de pago.
    /// Estos son los archivos "crudos" que llegan directamente de Bancolombia, Efecty, etc.
    /// </summary>
    public class ArchivoPlanoService : IArchivoPlanoService
    {
        /// <summary>
        /// Detecta si un archivo es plano (posición fija) o CSV/delimitado
        /// </summary>
        public bool EsArchivoPlano(string rutaArchivo)
        {
            var extension = Path.GetExtension(rutaArchivo).ToLower();
            
            // Los archivos .lst de Efecty son archivos planos de longitud/posiciones fijas
            if (extension == ".lst") return true;

            // Si es .txt, verificamos el contenido
            if (extension == ".txt")
            {
                var primeraLinea = File.ReadLines(rutaArchivo).FirstOrDefault() ?? "";
                
                // Si NO contiene delimitadores comunes, es archivo plano
                if (!primeraLinea.Contains(";") && !primeraLinea.Contains(",") && !primeraLinea.Contains("\t"))
                {
                    return true;
                }
                
                // Si la línea es muy larga (>100 chars) y tiene pocos delimitadores, probablemente es plano
                if (primeraLinea.Length > 100)
                {
                    int countDelimiters = primeraLinea.Count(c => c == ';' || c == ',' || c == '\t');
                    if (countDelimiters < 5)
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Detecta el tipo de archivo plano basándose en el contenido
        /// </summary>
        public TipoArchivoPlano DetectarTipoArchivoPlano(string rutaArchivo)
        {
            var nombreArchivo = Path.GetFileName(rutaArchivo).ToLower();
            
            // Detectar por nombre
            if (nombreArchivo.Contains("bancolombia")) return TipoArchivoPlano.Bancolombia;
            if (nombreArchivo.Contains("efecty")) return TipoArchivoPlano.Efecty;
            if (nombreArchivo.Contains("pse")) return TipoArchivoPlano.PSE;
            if (nombreArchivo.Contains("wompi")) return TipoArchivoPlano.Wompi;
            
            // Si no se detecta por nombre, intentar por estructura
            var lineas = File.ReadLines(rutaArchivo);
            var primeraLinea = lineas.FirstOrDefault();
            if (primeraLinea == null) return TipoArchivoPlano.Desconocido;
            
            // Bancolombia tiene líneas de más de 180 caracteres típicamente
            if (primeraLinea.Length >= 180)
            {
                return TipoArchivoPlano.Bancolombia;
            }
            
            return TipoArchivoPlano.Desconocido;
        }

        /// <summary>
        /// Procesa un archivo plano de Bancolombia y extrae los pagos
        /// Formato de posiciones (basado en el script AutoIt):
        /// - Posición 70-77 (8 chars): Monto
        /// - Posición 81-92 (12 chars): Cédula
        /// - Posición 175-182 (8 chars): Fecha (YYYYMMDD)
        /// - Posición 61-62 (2 chars): Tipo (67=App, otro=Corresponsal)
        /// </summary>
        public async Task<ImportResult> ProcesarArchivoPlanoBancolombiaAsync(string rutaArchivo)
        {
            var result = new ImportResult();
            result.TipoConvenioDetectado = "Bancolombia (Archivo Plano)";
            
            var lines = await File.ReadAllLinesAsync(rutaArchivo);
            var nombreArchivo = Path.GetFileName(rutaArchivo);
            
            if (lines.Length == 0)
            {
                result.Excepciones.Add(new ExcepcionPago
                {
                    Tipo_Error = "ARCHIVO_VACIO",
                    Descripcion_Error = "El archivo no contiene datos"
                });
                return result;
            }

            int consecutivo = 1;
            
            // Saltar la primera línea (cabecera del archivo plano)
            for (int i = 1; i < lines.Length; i++)
            {
                var linea = lines[i];
                
                // Verificar que la línea tenga suficiente longitud
                if (linea.Length < 183)
                {
                    // Línea muy corta, podría ser final del archivo o línea vacía
                    if (!string.IsNullOrWhiteSpace(linea))
                    {
                        result.Excepciones.Add(new ExcepcionPago
                        {
                            Numero_Linea = i + 1,
                            Linea_Original = linea.Length > 50 ? linea.Substring(0, 50) + "..." : linea,
                            Tipo_Error = "LINEA_CORTA",
                            Descripcion_Error = $"La línea tiene {linea.Length} caracteres, se esperaban al menos 183"
                        });
                    }
                    continue;
                }

                try
                {
                    // Extraer campos según posiciones (índice base 0, el script AutoIt usa base 1)
                    // StringMid($sLine, 70, 8) = substring desde posición 69 (0-based), 8 caracteres
                    var montoStr = linea.Substring(69, 8).Trim();           // Posición 70-77
                    var cedulaStr = linea.Substring(80, 12).Trim();         // Posición 81-92
                    var fechaStr = linea.Substring(174, 8).Trim();          // Posición 175-182
                    var tipoStr = linea.Substring(60, 2).Trim();            // Posición 61-62

                    // Determinar tipo de convenio
                    string tipoConvenio = tipoStr == "67" ? "Bancolombia - App" : "Bancolombia - Corresponsal";

                    // Parsear monto
                    if (!decimal.TryParse(montoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                    {
                        result.Excepciones.Add(new ExcepcionPago
                        {
                            Numero_Linea = i + 1,
                            Linea_Original = linea.Length > 50 ? linea.Substring(0, 50) + "..." : linea,
                            Tipo_Error = "MONTO_INVALIDO",
                            Descripcion_Error = $"No se pudo parsear el monto: '{montoStr}'"
                        });
                        continue;
                    }

                    // Parsear fecha (formato YYYYMMDD) - Convertir a UTC para PostgreSQL
                    DateTime fechaPago = DateTime.UtcNow;
                    if (fechaStr.Length == 8)
                    {
                        if (DateTime.TryParseExact(fechaStr, "yyyyMMdd", CultureInfo.InvariantCulture, 
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime fechaParseada))
                        {
                            fechaPago = fechaParseada;
                        }
                        else
                        {
                            result.Excepciones.Add(new ExcepcionPago
                            {
                                Numero_Linea = i + 1,
                                Linea_Original = linea.Length > 50 ? linea.Substring(0, 50) + "..." : linea,
                                Tipo_Error = "FECHA_INVALIDA",
                                Descripcion_Error = $"No se pudo parsear la fecha: '{fechaStr}'"
                            });
                            continue;
                        }
                    }

                    // Crear el pago
                    var pago = new Pago
                    {
                        Consecutivo = consecutivo++,
                        CedulaCliente = cedulaStr,
                        Monto = monto,
                        FechaConsignacion = DateTime.SpecifyKind(fechaPago, DateTimeKind.Utc),
                        Convenio = tipoConvenio,
                        NumeroAprobacion = "", // El archivo plano no tiene número de aprobación
                        ArchivoOrigen = nombreArchivo,
                        Estado = EstadoPago.Importado
                    };
                    
                    // Generar ID y Hash únicos basados en Cédula + Monto + FechaConsignación
                    pago.GenerarIdentificadores();

                    result.PagosValidos.Add(pago);
                }
                catch (Exception ex)
                {
                    result.Excepciones.Add(new ExcepcionPago
                    {
                        Numero_Linea = i + 1,
                        Linea_Original = linea.Length > 50 ? linea.Substring(0, 50) + "..." : linea,
                        Tipo_Error = "ERROR_PARSEO",
                        Descripcion_Error = ex.Message
                    });
                }
            }

            // Ordenar por cédula (como hace el script AutoIt)
            result.PagosValidos = result.PagosValidos
                .OrderBy(p => long.TryParse(p.CedulaCliente, out long cedula) ? cedula : long.MaxValue)
                .ToList();

            // Reasignar consecutivos después de ordenar
            for (int i = 0; i < result.PagosValidos.Count; i++)
            {
                result.PagosValidos[i].Consecutivo = i + 1;
            }

            return result;
        }

        /// <summary>
        /// Procesa un archivo plano de Efecty (.lst) y extrae los pagos
        /// Formato esperado por línea (separado por espacios):
        /// FACTURA (con ceros), VALOR ($X,XXX.00), VALOR ADICIONAL, CAJERO, TRANSAC., FECHA DE PAGO
        /// </summary>
        public async Task<ImportResult> ProcesarArchivoPlanoEfectyAsync(string rutaArchivo)
        {
            var result = new ImportResult();
            result.TipoConvenioDetectado = "Efecty (Archivo Plano .lst)";
            
            var lines = await File.ReadAllLinesAsync(rutaArchivo);
            var nombreArchivo = Path.GetFileName(rutaArchivo);
            
            if (lines.Length == 0)
            {
                result.Excepciones.Add(new ExcepcionPago { Tipo_Error = "ARCHIVO_VACIO", Descripcion_Error = "El archivo no contiene datos" });
                return result;
            }

            int consecutivo = 1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var linea = lines[i].Trim();
                
                // Saltar líneas vacías o de cabecera/totales
                if (string.IsNullOrWhiteSpace(linea) || linea.StartsWith("REPORTE") || linea.StartsWith("ENTIDAD") || linea.StartsWith("FACTURA") || linea.StartsWith("TOTALES") || linea.StartsWith("Total") || linea.StartsWith("Valor") || linea.StartsWith("GRAN") || linea.StartsWith("Operaciones:"))
                    continue;

                try
                {
                    // La línea tiene varias partes separadas por espacios en blanco múltiples
                    var parts = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    
                    // Esperamos al menos 6 columnas: 0:Factura, 1:Valor, 2:Adicional, 3:Cajero, 4:Transac, 5:Fecha
                    if (parts.Length < 6)
                        continue;

                    var facturaStr = parts[0];
                    var montoStr = parts[1];
                    var transaccionStr = parts[4];
                    var fechaStr = parts[5];

                    // Formatear cédula (quitar ceros a la izquierda)
                    string cedula = facturaStr.TrimStart('0');
                    if (string.IsNullOrEmpty(cedula)) cedula = "0"; // por si era todo ceros

                    // Limpiar y parsear monto
                    montoStr = montoStr.Replace("$", "").Replace(",", "").Trim();
                    if (!decimal.TryParse(montoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                    {
                        result.Excepciones.Add(new ExcepcionPago { Numero_Linea = i + 1, Linea_Original = linea, Tipo_Error = "MONTO_INVALIDO", Descripcion_Error = $"No se pudo parsear el monto: '{parts[1]}'" });
                        continue;
                    }

                    // Parsear fecha (formato YYYY-MM-DD)
                    DateTime fechaPago = DateTime.UtcNow;
                    if (DateTime.TryParseExact(fechaStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime fechaParseada))
                    {
                        fechaPago = fechaParseada;
                    }
                    else
                    {
                        result.Excepciones.Add(new ExcepcionPago { Numero_Linea = i + 1, Linea_Original = linea, Tipo_Error = "FECHA_INVALIDA", Descripcion_Error = $"No se pudo parsear la fecha: '{fechaStr}'" });
                        continue;
                    }

                    // Crear el pago
                    var pago = new Pago
                    {
                        Consecutivo = consecutivo++,
                        CedulaCliente = cedula,
                        Monto = monto,
                        FechaConsignacion = DateTime.SpecifyKind(fechaPago, DateTimeKind.Utc),
                        Convenio = "Efecty",
                        NumeroAprobacion = transaccionStr, // El No. de transacción
                        ArchivoOrigen = nombreArchivo,
                        Estado = EstadoPago.Importado
                    };
                    
                    pago.GenerarIdentificadores();
                    result.PagosValidos.Add(pago);
                }
                catch (Exception ex)
                {
                    result.Excepciones.Add(new ExcepcionPago { Numero_Linea = i + 1, Linea_Original = linea, Tipo_Error = "ERROR_PARSEO", Descripcion_Error = ex.Message });
                }
            }

            return result;
        }

        /// <summary>
        /// Genera un archivo CSV con los pagos que tienen múltiples créditos para revisión manual
        /// </summary>
        public async Task<string> GenerarReporteMultiplesCreditosAsync(
            List<Pago> pagosMultiples, 
            string carpetaDestino, 
            string nombreConvenio)
        {
            var fecha = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rutaArchivo = System.IO.Path.Combine(carpetaDestino, $"REVISAR_MultiplesCreditos_{nombreConvenio}_{fecha}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("CEDULA,MONTO,CONVENIO,FECHA_CONSIGNACION,CANTIDAD_CREDITOS,CREDITOS,MENSAJE");

            foreach (var pago in pagosMultiples)
            {
                var creditosStr = pago.ListaCreditos ?? "";
                var cantidad = string.IsNullOrWhiteSpace(creditosStr) ? 0 : creditosStr.Split(',').Length;
                
                var mensaje = $"El cliente {pago.CedulaCliente} tiene múltiples créditos activos. Consultar con jefe de negocio.";
                
                sb.AppendLine($"{pago.CedulaCliente},{pago.Monto},{pago.Convenio},{pago.FechaConsignacion:yyyy-MM-dd},{cantidad},\"{creditosStr}\",\"{mensaje}\"");
            }

            // Asegurar directorio
            var dir = System.IO.Path.GetDirectoryName(rutaArchivo);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) 
                System.IO.Directory.CreateDirectory(dir);

            await System.IO.File.WriteAllTextAsync(rutaArchivo, sb.ToString(), Encoding.UTF8);
            return rutaArchivo;
        }

    }

    public enum TipoArchivoPlano
    {
        Desconocido,
        Bancolombia,
        Efecty,
        PSE,
        Wompi
    }
}
