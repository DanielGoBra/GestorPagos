using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;

namespace GestorPagos.Core.Services
{
    public class ExcelExportService : IExcelExportService
    {
        public async Task ExportarPagosAsync(List<Pago> pagos, string rutaSalida)
        {
            await Task.Run(() =>
            {
                string rutaPlantilla = @"C:\Users\DanielFelipeGomezBra\Inklusiva s.a.s\Sistemas Inklusiva - Documentos\Documentos en RED\Daniel Gomez\Plantilla Desarrollo Vaas.xlsm";

                IWorkbook workbook;

                if (File.Exists(rutaPlantilla))
                {
                    using (var fs = new FileStream(rutaPlantilla, FileMode.Open, FileAccess.Read))
                    {
                        workbook = new XSSFWorkbook(fs);
                    }
                }
                else
                {
                    workbook = new XSSFWorkbook();
                    workbook.CreateSheet("Convenios");
                }

                ISheet sheet = workbook.GetSheetAt(0);

                // Estilos
                ICellStyle headerStyle = workbook.CreateCellStyle();
                headerStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                headerStyle.FillPattern = FillPattern.SolidForeground;
                IFont headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerStyle.SetFont(headerFont);

                ICellStyle currencyStyle = workbook.CreateCellStyle();
                IDataFormat format = workbook.CreateDataFormat();
                currencyStyle.DataFormat = format.GetFormat("#,##0");

                ICellStyle dateStyle = workbook.CreateCellStyle();
                dateStyle.DataFormat = format.GetFormat("dd/MM/yyyy");

                // Estilo bloqueado (pago cuadrado) - verde claro
                ICellStyle lockedStyle = workbook.CreateCellStyle();
                lockedStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightGreen.Index;
                lockedStyle.FillPattern = FillPattern.SolidForeground;
                lockedStyle.IsLocked = true;
                lockedStyle.DataFormat = format.GetFormat("#,##0");

                ICellStyle lockedDateStyle = workbook.CreateCellStyle();
                lockedDateStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightGreen.Index;
                lockedDateStyle.FillPattern = FillPattern.SolidForeground;
                lockedDateStyle.IsLocked = true;
                lockedDateStyle.DataFormat = format.GetFormat("dd/MM/yyyy");

                ICellStyle lockedTextStyle = workbook.CreateCellStyle();
                lockedTextStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightGreen.Index;
                lockedTextStyle.FillPattern = FillPattern.SolidForeground;
                lockedTextStyle.IsLocked = true;

                // Estilo para mora alta (rojo)
                ICellStyle moraAltaStyle = workbook.CreateCellStyle();
                moraAltaStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Red.Index;
                moraAltaStyle.FillPattern = FillPattern.SolidForeground;
                IFont moraFont = workbook.CreateFont();
                moraFont.IsBold = true;
                moraFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
                moraAltaStyle.SetFont(moraFont);

                // Encabezado segun plantilla para vaas + nuevas columnas
                IRow headerRow = sheet.GetRow(0) ?? sheet.CreateRow(0);
                string[] headers = { "ID", "CEDULA", "BANCO", "MONTO", "FECHA CONSIG", "CONVENIO", 
                    "N APROBACION", "RESULTADO", "ERROR", "ASUMIDO", "SALDO P", 
                    "OBSERVACION", "CC", "FECHA APLICACION", "CREDITO", 
                    "CAPITAL", "INTERESES", "CARGOS", "MORA", "TOTAL APLICADO",
                    "CUOTA", "CUOTAS PEND", "DIAS MORA", "PROX CUOTA", "EXCEDENTE", "CREDITOS" };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = headerRow.GetCell(i) ?? headerRow.CreateCell(i);
                    cell.SetCellValue(headers[i]);
                    cell.CellStyle = headerStyle;
                }

                // Datos
                int rowIndex = 1;
                foreach (var pago in pagos)
                {
                    IRow row = sheet.GetRow(rowIndex) ?? sheet.CreateRow(rowIndex);
                    bool estaCuadrado = pago.EstaCuadrado;
                    bool tieneMoraAlta = pago.TieneMoraAlta;

                    // A: ID (últimos 4 dígitos del GUID)
                    SetCell(row, 0, pago.IdCorto, estaCuadrado ? lockedTextStyle : null);
                    // B: CEDULA (la correcta, corregida por gestores)
                    SetCell(row, 1, pago.CedulaCorrecta, estaCuadrado ? lockedTextStyle : null);
                    // C: BANCO (la cédula con la que entró el pago)
                    SetCell(row, 2, pago.CedulaCliente, estaCuadrado ? lockedTextStyle : null);
                    // D: MONTO
                    SetCellDecimal(row, 3, pago.Monto, estaCuadrado ? lockedStyle : currencyStyle);
                    // E: FECHA CONSIG
                    SetCellDate(row, 4, pago.FechaConsignacion, estaCuadrado ? lockedDateStyle : dateStyle);
                    // F: CONVENIO
                    SetCell(row, 5, pago.Convenio, estaCuadrado ? lockedTextStyle : null);
                    // G: N° APROBACIÓN
                    SetCell(row, 6, pago.NumeroAprobacion, estaCuadrado ? lockedTextStyle : null);
                    // H: RESULTADO
                    SetCell(row, 7, pago.Resultado, estaCuadrado ? lockedTextStyle : null);
                    // I: ERROR
                    SetCell(row, 8, pago.Error, estaCuadrado ? lockedTextStyle : null);
                    // J: ASUMIDO
                    SetCell(row, 9, pago.Asumido, estaCuadrado ? lockedTextStyle : null);
                    // K: SALDO P
                    SetCell(row, 10, pago.SaldoP, estaCuadrado ? lockedTextStyle : null);
                    // L: OBSERVACIÓN
                    SetCell(row, 11, pago.Observacion, estaCuadrado ? lockedTextStyle : null);
                    // M: CC (queda para consecutivo del ERP)
                    SetCell(row, 12, pago.CC ?? "", estaCuadrado ? lockedTextStyle : null);
                    // N: FECHA APLICACIÓN
                    SetCell(row, 13, pago.FechaAplicacion ?? "", estaCuadrado ? lockedTextStyle : null);
                    // O: CREDITO
                    SetCell(row, 14, pago.Credito ?? "", estaCuadrado ? lockedTextStyle : null);
                    // P: CAPITAL
                    SetCellDecimal(row, 15, pago.Capital, estaCuadrado ? lockedStyle : currencyStyle);
                    // Q: INTERESES
                    SetCellDecimal(row, 16, pago.Intereses, estaCuadrado ? lockedStyle : currencyStyle);
                    // R: CARGOS
                    SetCellDecimal(row, 17, pago.Cargos, estaCuadrado ? lockedStyle : currencyStyle);
                    // S: MORA (valor)
                    SetCellDecimal(row, 18, pago.Mora, estaCuadrado ? lockedStyle : currencyStyle);
                    // T: TOTAL APLICADO
                    SetCellDecimal(row, 19, pago.TotalAplicado, estaCuadrado ? lockedStyle : currencyStyle);
                    
                    // ============================================================
                    // NUEVAS COLUMNAS - Información adicional del crédito
                    // ============================================================
                    // U: CUOTA (número de cuota actual)
                    if (pago.CuotaActual.HasValue)
                        SetCellNumeric(row, 20, pago.CuotaActual.Value, null);
                    else
                        SetCell(row, 20, "", null);
                    
                    // V: CUOTAS PEND (cantidad de cuotas pendientes)
                    if (pago.CuotasPendientes.HasValue)
                        SetCellNumeric(row, 21, pago.CuotasPendientes.Value, null);
                    else
                        SetCell(row, 21, "", null);
                    
                    // W: DÍAS MORA (con estilo rojo si > 30)
                    if (pago.DiasMora.HasValue)
                        SetCellNumeric(row, 22, pago.DiasMora.Value, tieneMoraAlta ? moraAltaStyle : null);
                    else
                        SetCell(row, 22, "", null);
                    
                    // X: PROX CUOTA (proxima fecha de pago)
                    SetCell(row, 23, pago.FechaProximaCuota ?? "", null);
                    
                    // Y: EXCEDENTE (monto que excede el valor de la cuota - calculado)
                    if (pago.Excedente > 0)
                        SetCellDecimal(row, 24, pago.Excedente, null);
                    else
                        SetCell(row, 24, "", null);
                    
                    // Z: CREDITOS (lista si tiene mas de 1)
                    SetCell(row, 25, pago.ListaCreditos ?? "", null);

                    rowIndex++;
                }

                // Proteger hoja
                if (sheet is XSSFSheet xssfSheet)
                {
                    xssfSheet.ProtectSheet("Inklusiva2025");
                }

                for (int i = 0; i < headers.Length; i++)
                {
                    sheet.AutoSizeColumn(i);
                }

                using (var fs = new FileStream(rutaSalida, FileMode.Create, FileAccess.Write))
                {
                    workbook.Write(fs);
                }
            });
        }

        private void SetCell(IRow row, int col, string value, ICellStyle? style)
        {
            var cell = row.GetCell(col) ?? row.CreateCell(col);
            cell.SetCellValue(value);
            if (style != null) cell.CellStyle = style;
        }

        private void SetCell(IRow row, int col, int value, ICellStyle? style)
        {
            var cell = row.GetCell(col) ?? row.CreateCell(col);
            cell.SetCellValue(value);
            if (style != null) cell.CellStyle = style;
        }

        private void SetCellDecimal(IRow row, int col, decimal value, ICellStyle? style)
        {
            var cell = row.GetCell(col) ?? row.CreateCell(col);
            cell.SetCellValue((double)value);
            if (style != null) cell.CellStyle = style;
        }

        private void SetCellNumeric(IRow row, int col, int value, ICellStyle? style)
        {
            var cell = row.GetCell(col) ?? row.CreateCell(col);
            cell.SetCellValue(value);
            if (style != null) cell.CellStyle = style;
        }

        private void SetCellDate(IRow row, int col, System.DateTime value, ICellStyle style)
        {
            var cell = row.GetCell(col) ?? row.CreateCell(col);
            cell.SetCellValue(value);
            cell.CellStyle = style;
        }
        
        /// <summary>
        /// Exporta datos simplificados a CSV para Google Sheets (area de BI)
        /// Incluye info para notificar al cliente sobre su pago y cuotas restantes
        /// </summary>
        public async Task ExportarParaGoogleSheetsAsync(List<Pago> pagos, string rutaSalida)
        {
            await Task.Run(() =>
            {
                var sb = new System.Text.StringBuilder();
                
                // Encabezados CSV
                sb.AppendLine("CEDULA,MONTO,RESULTADO,ERROR,SALDO_PENDIENTE,CREDITO,CUOTA_PAGADA,CUOTAS_PENDIENTES,PROXIMA_CUOTA,DIAS_MORA");
                
                foreach (var pago in pagos)
                {
                    // Escapar campos que puedan contener comas
                    var cedula = EscaparCsv(pago.CedulaCorrecta);
                    var monto = pago.Monto.ToString("F0");
                    var resultado = EscaparCsv(pago.Resultado);
                    var error = EscaparCsv(pago.Error);
                    var saldoP = EscaparCsv(pago.SaldoP);
                    var credito = EscaparCsv(pago.Credito ?? "");
                    var cuotaPagada = pago.CuotaActual?.ToString() ?? "";
                    var cuotasPendientes = pago.CuotasPendientes?.ToString() ?? "";
                    var proxCuota = EscaparCsv(pago.FechaProximaCuota ?? "");
                    var diasMora = pago.DiasMora?.ToString() ?? "";
                    
                    sb.AppendLine($"{cedula},{monto},{resultado},{error},{saldoP},{credito},{cuotaPagada},{cuotasPendientes},{proxCuota},{diasMora}");
                }
                
                // Guardar con BOM para que Excel/Google Sheets reconozca UTF-8
                File.WriteAllText(rutaSalida, sb.ToString(), System.Text.Encoding.UTF8);
            });
        }
        
        private string EscaparCsv(string valor)
        {
            if (string.IsNullOrEmpty(valor)) return "";
            
            // Si contiene coma, comillas o salto de linea, envolver en comillas
            if (valor.Contains(",") || valor.Contains("\"") || valor.Contains("\n"))
            {
                // Escapar comillas dobles duplicandolas
                valor = valor.Replace("\"", "\"\"");
                return $"\"{valor}\"";
            }
            return valor;
        }
        
        /// <summary>
        /// Agrega pagos APLICADOS a un archivo Excel acumulativo semanal (para reporte fiduciaria)
        /// El archivo se crea si no existe, o se agregan registros si ya existe
        /// Solo incluye pagos con Resultado = "Aplicado"
        /// </summary>
        public async Task<(string Ruta, int NuevosRegistros, int TotalRegistros)> AgregarPagosAcumulativoAsync(List<Pago> pagos, string carpetaDestino)
        {
            return await Task.Run(() =>
            {
                // Filtrar solo pagos aplicados
                var pagosAplicados = pagos
                    .Where(p => !string.IsNullOrEmpty(p.Resultado) && 
                               p.Resultado.Equals("Aplicado", System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (pagosAplicados.Count == 0)
                {
                    return ("", 0, 0);
                }
                
                // Calcular semana ISO (lunes a domingo)
                var hoy = System.DateTime.Now;
                var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
                var calendario = cultura.Calendar;
                var diaSemana = (int)hoy.DayOfWeek;
                var inicioSemana = hoy.AddDays(-(diaSemana == 0 ? 6 : diaSemana - 1)); // Lunes
                var nombreArchivo = $"Pagos_Acumulado_Semana_{inicioSemana:yyyyMMdd}.xlsx";
                var rutaCompleta = Path.Combine(carpetaDestino, nombreArchivo);
                
                IWorkbook workbook;
                ISheet sheet;
                int rowIndex;
                HashSet<string> hashesExistentes = new HashSet<string>();
                
                // Si el archivo existe, abrirlo y leer hashes existentes
                if (File.Exists(rutaCompleta))
                {
                    using (var fs = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read))
                    {
                        workbook = new XSSFWorkbook(fs);
                    }
                    sheet = workbook.GetSheetAt(0);
                    rowIndex = sheet.LastRowNum + 1;
                    
                    // Leer hashes existentes para evitar duplicados (columna 0 = ID/Hash)
                    for (int i = 1; i <= sheet.LastRowNum; i++)
                    {
                        var row = sheet.GetRow(i);
                        if (row != null)
                        {
                            var cellHash = row.GetCell(0);
                            if (cellHash != null)
                            {
                                hashesExistentes.Add(cellHash.StringCellValue ?? "");
                            }
                        }
                    }
                }
                else
                {
                    // Crear nuevo archivo
                    workbook = new XSSFWorkbook();
                    sheet = workbook.CreateSheet("Pagos Aplicados");
                    rowIndex = 0;
                    
                    // Crear estilos
                    ICellStyle headerStyle = workbook.CreateCellStyle();
                    headerStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.DarkBlue.Index;
                    headerStyle.FillPattern = FillPattern.SolidForeground;
                    IFont headerFont = workbook.CreateFont();
                    headerFont.IsBold = true;
                    headerFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
                    headerStyle.SetFont(headerFont);
                    
                    // Encabezados para fiduciaria
                    IRow headerRow = sheet.CreateRow(rowIndex++);
                    string[] headers = { 
                        "ID_UNICO", "CEDULA", "TITULAR", "CEDULA_PAGADOR", "MONTO", "FECHA_CONSIG", "CONVENIO", 
                        "RESULTADO", "CC", "FECHA_APLICACION", "CREDITO",
                        "CAPITAL", "INTERESES", "CARGOS", "MORA", "TOTAL_APLICADO"
                    };
                    
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = headerRow.CreateCell(i);
                        cell.SetCellValue(headers[i]);
                        cell.CellStyle = headerStyle;
                    }
                }
                
                // Estilos para datos
                IDataFormat format = workbook.CreateDataFormat();
                ICellStyle currencyStyle = workbook.CreateCellStyle();
                currencyStyle.DataFormat = format.GetFormat("#,##0");
                
                ICellStyle dateStyle = workbook.CreateCellStyle();
                dateStyle.DataFormat = format.GetFormat("dd/MM/yyyy");
                
                int nuevosRegistros = 0;
                
                // Agregar pagos aplicados (evitando duplicados por HashUnico)
                foreach (var pago in pagosAplicados)
                {
                    // Verificar si ya existe
                    if (hashesExistentes.Contains(pago.HashUnico))
                    {
                        continue; // Ya está en el archivo
                    }
                    
                    IRow row = sheet.CreateRow(rowIndex++);
                    
                    // ID único (hash para evitar duplicados)
                    row.CreateCell(0).SetCellValue(pago.HashUnico);
                    // Cédula (La correcta)
                    row.CreateCell(1).SetCellValue(pago.CedulaCorrecta);
                    // Titular (NUEVO)
                    row.CreateCell(2).SetCellValue(pago.Titular ?? "");
                    // Cedula Pagador (Original del banco) (NUEVO)
                    row.CreateCell(3).SetCellValue(pago.CedulaCliente);

                    // Monto
                    var cellMonto = row.CreateCell(4);
                    cellMonto.SetCellValue((double)pago.Monto);
                    cellMonto.CellStyle = currencyStyle;
                    // Fecha consignación
                    var cellFecha = row.CreateCell(5);
                    cellFecha.SetCellValue(pago.FechaConsignacion);
                    cellFecha.CellStyle = dateStyle;
                    // Convenio
                    row.CreateCell(6).SetCellValue(pago.Convenio);
                    // Resultado
                    row.CreateCell(7).SetCellValue(pago.Resultado);
                    // CC
                    row.CreateCell(8).SetCellValue(pago.CC ?? "");
                    // Fecha aplicación
                    row.CreateCell(9).SetCellValue(pago.FechaAplicacion ?? "");
                    // Crédito
                    row.CreateCell(10).SetCellValue(pago.Credito ?? "");
                    // Capital
                    var cellCapital = row.CreateCell(11);
                    cellCapital.SetCellValue((double)pago.Capital);
                    cellCapital.CellStyle = currencyStyle;
                    // Intereses
                    var cellIntereses = row.CreateCell(12);
                    cellIntereses.SetCellValue((double)pago.Intereses);
                    cellIntereses.CellStyle = currencyStyle;
                    // Cargos
                    var cellCargos = row.CreateCell(13);
                    cellCargos.SetCellValue((double)pago.Cargos);
                    cellCargos.CellStyle = currencyStyle;
                    // Mora
                    var cellMora = row.CreateCell(14);
                    cellMora.SetCellValue((double)pago.Mora);
                    cellMora.CellStyle = currencyStyle;
                    // Total aplicado
                    var cellTotal = row.CreateCell(15);
                    cellTotal.SetCellValue((double)pago.TotalAplicado);
                    cellTotal.CellStyle = currencyStyle;
                    
                    hashesExistentes.Add(pago.HashUnico);
                    nuevosRegistros++;
                }
                
                // Auto-ajustar columnas (solo si es archivo nuevo)
                if (nuevosRegistros > 0)
                {
                    for (int i = 0; i < 19; i++)
                    {
                        sheet.AutoSizeColumn(i);
                    }
                }
                
                // Guardar
                using (var fs = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write))
                {
                    workbook.Write(fs);
                }
                
                return (rutaCompleta, nuevosRegistros, hashesExistentes.Count);
            });
        }
        
        /// <summary>
        /// Exporta pagos bloqueados/conciliados a un archivo Excel con formato estricto de fiduciaria.
        /// Maneja IOException si el archivo está abierto por el usuario.
        /// </summary>
        public async Task<(string Ruta, int TotalRegistros)> ExportarPagosFiduciariaAsync(
            List<Pago> pagos, string carpetaDestino, DateTime startDate, DateTime endDate)
        {
            return await Task.Run(() =>
            {
                if (pagos == null || pagos.Count == 0)
                    return ("", 0);

                // Nombre del archivo con rango de fechas
                var nombreArchivo = $"Pagos_Fiduciaria_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
                var rutaCompleta = Path.Combine(carpetaDestino, nombreArchivo);

                IWorkbook workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Pagos Conciliados");

                // ============================================================
                // ESTILOS
                // ============================================================
                ICellStyle headerStyle = workbook.CreateCellStyle();
                headerStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.DarkBlue.Index;
                headerStyle.FillPattern = FillPattern.SolidForeground;
                IFont headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
                headerStyle.SetFont(headerFont);

                IDataFormat format = workbook.CreateDataFormat();
                
                ICellStyle currencyStyle = workbook.CreateCellStyle();
                currencyStyle.DataFormat = format.GetFormat("#,##0");

                ICellStyle dateStyle = workbook.CreateCellStyle();
                dateStyle.DataFormat = format.GetFormat("dd/MM/yyyy");

                // Estilo conciliado (verde claro)
                ICellStyle conciliadoStyle = workbook.CreateCellStyle();
                conciliadoStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightGreen.Index;
                conciliadoStyle.FillPattern = FillPattern.SolidForeground;
                conciliadoStyle.DataFormat = format.GetFormat("#,##0");

                // ============================================================
                // ENCABEZADOS - Columnas estrictas fiduciaria
                // ============================================================
                int rowIndex = 0;
                IRow headerRow = sheet.CreateRow(rowIndex++);
                string[] headers = {
                    "CONSECUTIVO",       // A
                    "CEDULA",            // B (CedulaCorrecta)
                    "BANCO",             // C (CedulaCliente)
                    "TITULAR",           // D (Nombre del cliente)
                    "MONTO",             // E
                    "FECHA CONSIG",      // F
                    "CONVENIO",          // G
                    "N APROBACION",      // H (ID de pago)
                    "RESULTADO",         // I
                    "CC",                // J (Consecutivo ERP)
                    "FECHA APLICACION",  // K
                    "CREDITO",           // L
                    "CAPITAL",           // M
                    "INTERESES",         // N
                    "CARGOS",            // O
                    "MORA",              // P
                    "TOTAL APLICADO"     // Q
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = headerRow.CreateCell(i);
                    cell.SetCellValue(headers[i]);
                    cell.CellStyle = headerStyle;
                }

                // ============================================================
                // DATOS - Mapeo estricto de propiedades del Pago
                // ============================================================
                int consecutivo = 1;
                foreach (var pago in pagos.OrderBy(p => p.FechaConsignacion).ThenBy(p => p.CedulaCorrecta))
                {
                    IRow row = sheet.CreateRow(rowIndex++);

                    // A: CONSECUTIVO (auto-generado)
                    row.CreateCell(0).SetCellValue(consecutivo++);

                    // B: CEDULA (la correcta, corregida por gestores)
                    row.CreateCell(1).SetCellValue(pago.CedulaCorrecta);

                    // C: BANCO (la cédula original del banco)
                    row.CreateCell(2).SetCellValue(pago.CedulaCliente);

                    // D: TITULAR (nombre del cliente desde ERP)
                    row.CreateCell(3).SetCellValue(pago.Titular ?? "");

                    // E: MONTO
                    var cellMonto = row.CreateCell(4);
                    cellMonto.SetCellValue((double)pago.Monto);
                    cellMonto.CellStyle = conciliadoStyle;

                    // F: FECHA CONSIG
                    var cellFecha = row.CreateCell(5);
                    cellFecha.SetCellValue(pago.FechaConsignacion);
                    cellFecha.CellStyle = dateStyle;

                    // G: CONVENIO
                    row.CreateCell(6).SetCellValue(pago.Convenio);

                    // H: N APROBACION (ID de pago / NumeroAprobacion)
                    row.CreateCell(7).SetCellValue(pago.NumeroAprobacion);

                    // I: RESULTADO
                    row.CreateCell(8).SetCellValue(pago.Resultado);

                    // J: CC (Consecutivo ERP)
                    row.CreateCell(9).SetCellValue(pago.CC ?? "");

                    // K: FECHA APLICACION
                    row.CreateCell(10).SetCellValue(pago.FechaAplicacion ?? "");

                    // L: CREDITO
                    row.CreateCell(11).SetCellValue(pago.Credito ?? "");

                    // M: CAPITAL
                    var cellCapital = row.CreateCell(12);
                    cellCapital.SetCellValue((double)pago.Capital);
                    cellCapital.CellStyle = currencyStyle;

                    // N: INTERESES
                    var cellIntereses = row.CreateCell(13);
                    cellIntereses.SetCellValue((double)pago.Intereses);
                    cellIntereses.CellStyle = currencyStyle;

                    // O: CARGOS
                    var cellCargos = row.CreateCell(14);
                    cellCargos.SetCellValue((double)pago.Cargos);
                    cellCargos.CellStyle = currencyStyle;

                    // P: MORA
                    var cellMora = row.CreateCell(15);
                    cellMora.SetCellValue((double)pago.Mora);
                    cellMora.CellStyle = currencyStyle;

                    // Q: TOTAL APLICADO
                    var cellTotal = row.CreateCell(16);
                    cellTotal.SetCellValue((double)pago.TotalAplicado);
                    cellTotal.CellStyle = conciliadoStyle;
                }

                // Auto-ajustar columnas
                for (int i = 0; i < headers.Length; i++)
                {
                    sheet.AutoSizeColumn(i);
                }

                // Proteger hoja para evitar ediciones accidentales
                if (sheet is XSSFSheet xssfSheet)
                {
                    xssfSheet.ProtectSheet("Inklusiva2026");
                }

                // ============================================================
                // GUARDAR - Con manejo de IOException
                // ============================================================
                try
                {
                    using (var fs = new FileStream(rutaCompleta, FileMode.Create, FileAccess.Write))
                    {
                        workbook.Write(fs);
                    }
                }
                catch (IOException)
                {
                    throw new IOException(
                        $"No se pudo guardar el archivo Excel.\n\n" +
                        $"Archivo: {nombreArchivo}\n\n" +
                        $"Por favor cierre el archivo en Excel y vuelva a intentar.");
                }

                return (rutaCompleta, pagos.Count);
            });
        }
    }
}
