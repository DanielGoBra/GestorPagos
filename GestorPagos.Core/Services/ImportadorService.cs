using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;
using NPOI.SS.UserModel; // Added for Excel parsing
using NPOI.XSSF.UserModel;

namespace GestorPagos.Core.Services
{
    public enum TipoConvenio
    {
        Desconocido,
        Efecty,
        Bancolombia,
        PSE,
        Wompi
    }

    public class ImportadorService : IImportadorService
    {
        public async Task<ImportResult> ImportarArchivoAsync(string rutaArchivo)
        {
            var extension = Path.GetExtension(rutaArchivo).ToLower();
            if (extension == ".xlsx" || extension == ".xls")
            {
                return await ImportarArchivoExcelAsync(rutaArchivo);
            }

            var result = new ImportResult();
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

            // Detectar tipo de convenio por encabezado o nombre de archivo
            var tipoConvenio = DetectarTipoConvenio(lines[0], nombreArchivo);
            result.TipoConvenioDetectado = tipoConvenio.ToString();

            if (tipoConvenio == TipoConvenio.Desconocido)
            {
                result.Excepciones.Add(new ExcepcionPago
                {
                    Tipo_Error = "CONVENIO_DESCONOCIDO",
                    Descripcion_Error = $"No se pudo identificar el tipo de convenio del archivo. Encabezado: {lines[0]}"
                });
                return result;
            }

            // Procesar líneas (saltar encabezado)
            int consecutivoGlobal = 1;
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var pago = ParsearLinea(line, tipoConvenio, i + 1, nombreArchivo);
                    if (pago != null)
                    {
                        pago.Consecutivo = consecutivoGlobal++;
                        // Generar ID y Hash únicos basados en Cédula + Monto + FechaConsignación
                        pago.GenerarIdentificadores();
                        result.PagosValidos.Add(pago);
                    }
                }
                catch (Exception ex)
                {
                    result.Excepciones.Add(new ExcepcionPago
                    {
                        Numero_Linea = i + 1,
                        Linea_Original = line,
                        Tipo_Error = "EXCEPCION",
                        Descripcion_Error = ex.Message
                    });
                }
            }

            return result;
        }

        private TipoConvenio DetectarTipoConvenio(string encabezado, string nombreArchivo)
        {
            var headerLower = encabezado.ToLower();
            var fileNameLower = nombreArchivo.ToLower();

            // Detectar por nombre de archivo primero
            if (fileNameLower.Contains("efecty")) return TipoConvenio.Efecty;
            if (fileNameLower.Contains("bancolombia")) return TipoConvenio.Bancolombia;
            if (fileNameLower.Contains("pse")) return TipoConvenio.PSE;
            if (fileNameLower.Contains("wompi")) return TipoConvenio.Wompi;

            // Detectar por estructura del encabezado
            var columnas = encabezado.Split(';').Select(c => c.Trim().ToLower()).ToList();

            // Bancolombia tiene "Resultado" como 5ta columna y NO tiene "Convenio"
            if (columnas.Contains("resultado") && !columnas.Contains("convenio"))
                return TipoConvenio.Bancolombia;

            // Efecty, PSE, Wompi tienen "Convenio" en el encabezado
            if (columnas.Contains("convenio"))
            {
                // Se detectará por el contenido de la columna Convenio en cada fila
                // Por ahora retornamos un genérico que procesará igual
                return TipoConvenio.Efecty; // Formato genérico con Convenio
            }

            return TipoConvenio.Desconocido;
        }

        private Pago? ParsearLinea(string linea, TipoConvenio tipo, int numeroLinea, string nombreArchivo)
        {
            // Detectar delimitador: Si tiene mas ';' que ',' usaremos ';' sino ','
            char delimiter = ';';
            if (linea.Count(c => c == ',') > linea.Count(c => c == ';'))
            {
                delimiter = ',';
            }

            var parts = linea.Split(delimiter);

            return tipo switch
            {
                TipoConvenio.Bancolombia => ParsearBancolombia(parts, nombreArchivo),
                TipoConvenio.Efecty or TipoConvenio.PSE or TipoConvenio.Wompi => ParsearConvenioGenerico(parts, nombreArchivo),
                _ => null
            };
        }

        /// <summary>
        /// Formato Bancolombia: Nro.;Cedula;Monto;Fecha;Resultado;Observacion;...;NumAprobacion;...
        /// Ejemplo: 5;10240689;600000;20251126;Aplicado;Sin Novedad;0;0;Tiene dos Creditos;7137813055;
        /// </summary>
        private Pago ParsearBancolombia(string[] parts, string nombreArchivo)
        {
            if (parts.Length < 5) return null!;

            var cedula = parts[1].Trim();
            var montoStr = parts[2].Trim();
            var fechaStr = parts[3].Trim();
            var resultado = parts.Length > 4 ? parts[4].Trim() : "";
            var observacion = parts.Length > 5 ? parts[5].Trim() : "";
            
            // El número de aprobación está en la posición 9 (índice 9) si existe
            var numAprobacion = "";
            if (parts.Length > 9 && !string.IsNullOrWhiteSpace(parts[9]))
            {
                numAprobacion = parts[9].Trim();
            }

            // Parsear monto
            if (!decimal.TryParse(montoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                return null!;

            // Parsear fecha (formato: YYYYMMDD)
            DateTime fechaPago = DateTime.Now;
            if (fechaStr.Length == 8)
            {
                DateTime.TryParseExact(fechaStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fechaPago);
            }

            return new Pago
            {
                CedulaCliente = cedula,
                Monto = monto,
                FechaConsignacion = fechaPago,
                Convenio = "Bancolombia",  // Puede ser Bancolombia-App o Bancolombia Corresponsal
                NumeroAprobacion = numAprobacion, // Puede estar vacío
                Resultado = resultado,
                Observacion = observacion,
                ArchivoOrigen = nombreArchivo,
                Estado = EstadoPago.Importado
            };
        }

        /// <summary>
        /// Formato genérico (Efecty, PSE, Wompi): Nro.;Cedula;Monto;Fecha;[Convenio];[IdTransaccion]
        /// Si no tiene columna Convenio, se usa el tipo detectado.
        /// </summary>
        private Pago ParsearConvenioGenerico(string[] parts, string nombreArchivo)
        {
            // Mínimo 4 columnas: Nro;Cedula;Monto;Fecha
            if (parts.Length < 4) 
                throw new FormatException($"Columnas insuficientes. Se esperan 4, se encontraron {parts.Length}.");

            var cedula = parts[1].Trim();
            var montoStr = parts[2].Trim();
            var fechaStr = parts[3].Trim();
            
            // Si hay columna 5 (índice 4), es Convenio, sino inferir del nombre
            var convenio = "";
            if (parts.Length > 4)
            {
                convenio = parts[4].Trim();
            }
            
            // Si la columna convenio está vacía o no existe, usar el tipo detectado del archivo
            if (string.IsNullOrEmpty(convenio))
            {
                var lowerName = nombreArchivo.ToLower();
                if (lowerName.Contains("efecty")) convenio = "Efecty";
                else if (lowerName.Contains("pse")) convenio = "PSE";
                else if (lowerName.Contains("wompi")) convenio = "Wompi";
                else convenio = "Desconocido";
            }

            var idTransaccion = parts.Length > 5 ? parts[5].Trim() : "";

            // Parsear monto
            if (!decimal.TryParse(montoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                throw new FormatException($"Monto inválido: '{montoStr}'");

            // Parsear fecha (múltiples formatos)
            DateTime fechaPago = ParsearFecha(fechaStr);

            return new Pago
            {
                CedulaCliente = cedula,
                Monto = monto,
                FechaConsignacion = fechaPago,
                Convenio = convenio, 
                NumeroAprobacion = idTransaccion,
                ArchivoOrigen = nombreArchivo,
                Estado = EstadoPago.Importado
            };
        }

        private DateTime ParsearFecha(string fechaStr)
        {
            DateTime fecha = DateTime.Now;

            // Intentar varios formatos (normalizados en yyyy-MM-dd, dd/MM/yyyy, mm/dd/yyyy, etc)
            string[] formatos = new[]
            {
                "yyyy/MM/dd", 
                "dd/MM/yyyy", 
                "yyyyMMdd", 
                "yyyy-MM-dd", 
                "dd-MM-yyyy",
                "M/d/yyyy",
                "MM/dd/yyyy", 
                "d/M/yyyy"
            };

            foreach (var formato in formatos)
            {
                if (DateTime.TryParseExact(fechaStr, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
                    return fecha;
            }
            
            // Intento final genérico
            if (DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
                return fecha;

            return DateTime.Now;
        }

        private Task<ImportResult> ImportarArchivoExcelAsync(string rutaArchivo)
        {
            var result = new ImportResult();
            result.TipoConvenioDetectado = "PSE (Excel .xlsx)";
            var nombreArchivo = Path.GetFileName(rutaArchivo);

            try
            {
                // Leer archivo Excel usando NPOI (funciona bien sincrónicamente con streams)
                using var stream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var isXlsx = rutaArchivo.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
                IWorkbook workbook = WorkbookFactory.Create(stream);

                var sheet = workbook.GetSheetAt(0); // Tomar la primera hoja
                if (sheet == null || sheet.PhysicalNumberOfRows <= 1)
                {
                    result.Excepciones.Add(new ExcepcionPago { Tipo_Error = "ARCHIVO_VACIO", Descripcion_Error = "El archivo Excel no contiene datos o solo tiene encabezados" });
                    return Task.FromResult(result);
                }

                // Asegurarse de que PSE sea el convenio para todo el archivo
                string convenioDetectado = "PSE";

                int consecutivoGlobal = 1;
                // Empezamos desde 1 para saltar la fila 0 (encabezados)
                for (int i = 1; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    if (row == null || row.Cells.All(d => d.CellType == CellType.Blank)) continue;

                    try
                    {
                        // Estructura PSE:
                        // 0: Modalidad de Pago
                        // 1: Estado (Paga)
                        // 2: Valor Factura -> Monto
                        // 3: CUS
                        // 4: Fecha Pago (05/03/2026 01:17:48) -> FechaConsignacion
                        // 5: ID_Pago -> NumeroAprobacion
                        // 6: Descripción del Pago
                        // 7: Cédula o Nit -> CedulaCliente

                        string ObtenerValorCelda(IRow f, int col)
                        {
                            var c = f.GetCell(col);
                            if (c == null) return "";
                            if (c.CellType == CellType.Numeric)
                            {
                                if (DateUtil.IsCellDateFormatted(c)) 
                                {
                                    if (c.DateCellValue is DateTime dt)
                                        return dt.ToString("yyyy-MM-dd HH:mm:ss");
                                    return System.Convert.ToDateTime(c.DateCellValue).ToString("yyyy-MM-dd HH:mm:ss");
                                }
                                return c.NumericCellValue.ToString(CultureInfo.InvariantCulture);
                            }
                            return c.ToString()?.Trim() ?? "";
                        }

                        string estadoStr = ObtenerValorCelda(row, 1);
                        // Opcional: solo procesar las "Paga" si existen otras.
                        // if (!estadoStr.Equals("Paga", StringComparison.OrdinalIgnoreCase)) continue;

                        string montoStr = ObtenerValorCelda(row, 2);
                        string fechaStr = ObtenerValorCelda(row, 4);
                        string idPagoStr = ObtenerValorCelda(row, 5);
                        string cedulaStr = ObtenerValorCelda(row, 7);

                        if (string.IsNullOrWhiteSpace(montoStr) || string.IsNullOrWhiteSpace(cedulaStr))
                            continue;

                        // Parsear monto
                        if (!decimal.TryParse(montoStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal monto))
                            throw new FormatException($"Monto inválido: '{montoStr}'");

                        // Parsear fecha
                        DateTime fechaPago = DateTime.UtcNow;
                        if (DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fp))
                        {
                            fechaPago = fp.ToUniversalTime();
                        }

                        // Crear el pago
                        var pago = new Pago
                        {
                            Consecutivo = consecutivoGlobal++,
                            CedulaCliente = cedulaStr,
                            Monto = monto,
                            FechaConsignacion = DateTime.SpecifyKind(fechaPago, DateTimeKind.Utc),
                            Convenio = convenioDetectado,
                            NumeroAprobacion = idPagoStr,
                            ArchivoOrigen = nombreArchivo,
                            Estado = EstadoPago.Importado
                        };

                        pago.GenerarIdentificadores();
                        result.PagosValidos.Add(pago);
                    }
                    catch (Exception ex)
                    {
                        result.Excepciones.Add(new ExcepcionPago { Numero_Linea = i + 1, Linea_Original = $"Fila excel {i}", Tipo_Error = "ERROR_PARSEO_EXCEL", Descripcion_Error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                result.Excepciones.Add(new ExcepcionPago { Tipo_Error = "ERROR_APERTURA_EXCEL", Descripcion_Error = $"No se pudo abrir el archivo Excel NPOI: {ex.Message}" });
            }

            return Task.FromResult(result);
        }
    }
}
