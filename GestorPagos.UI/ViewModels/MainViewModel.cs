using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GestorPagos.Core.Entities;
using GestorPagos.Core.Services;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Collections.Generic;
using System;
using System.Linq;
using System.IO;
using System.Text;

namespace GestorPagos.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // Ventana operativa solicitada para fiduciaria.
        private static readonly DateTime FechaOperacionDesde = new DateTime(2026, 3, 6);

        private readonly IImportadorService _importadorService;
        private readonly IExcelExportService _excelExportService;
        private readonly IPagoService _pagoService;
        private readonly IArchivoPlanoService _archivoPlanoService;
        private readonly IErpService? _erpService;
        private readonly IGestorDeProcesosService _gestorProcesosService;

        private string _convenioDetectado = "Ninguno";
        private int _cantidadErrores = 0;
        private int _cantidadGuardados = 0;
        private int _cantidadAplicados = 0;
        private int _cantidadSinCoincidencia = 0;
        private int _cantidadMultiplesCreditos = 0;
        private string _archivoActual = string.Empty;
        private string _carpetaOrigen = string.Empty;
        private List<ExcepcionPago> _excepciones = new List<ExcepcionPago>();
        private List<string> _archivosProcesados = new List<string>();

        // Usuario actual de Windows
        private readonly string _usuarioActual;
        private readonly string _equipoActual;

        // Ruta "Universal" dinámica según el usuario y el mes actual
        // Ejemplo: ...\Pagos Inklusiva\Pagos Convenio Bancolombia\12. Diciembre Bancolombia
        /// <summary>
        /// Obtiene la ruta de la carpeta mensual de Bancolombia.
        /// Busca dinámicamente la subcarpeta que empiece con el mes (ej: "3.")
        /// para tolerar variaciones: "3.marzo bancolombia", "3. Marzo Bancolombia", etc.
        /// </summary>
        private string ObtenerRutaUniversal()
        {
            var rutaConvenio = Path.Combine(ObtenerRutaBase(), "Pagos Convenio Bancolombia");
            return ObtenerRutaMensualConvenio(rutaConvenio) ?? rutaConvenio;
        }

        /// <summary>
        /// Busca dinámicamente la subcarpeta mensual dentro de una carpeta de convenio.
        /// Busca carpetas que empiecen con "{mes}." (ej: "3." para marzo)
        /// </summary>
        private string? ObtenerRutaMensualConvenio(string carpetaConvenio, int? mes = null)
        {
            if (!Directory.Exists(carpetaConvenio)) return null;

            var mesTarget = mes ?? DateTime.Now.Month;
            var prefijo = $"{mesTarget}.";

            var carpetaMes = Directory.GetDirectories(carpetaConvenio)
                .FirstOrDefault(d => Path.GetFileName(d)
                    .StartsWith(prefijo, StringComparison.OrdinalIgnoreCase));

            return carpetaMes;
        }

        /// <summary>
        /// Obtiene rutas a escanear por convenio.
        /// Para PSE/Efecty: por defecto usa carpeta mensual y solo usa raíz como fallback si no existe carpeta del mes.
        /// </summary>
        private List<(string Path, string Filter)> ObtenerTodasRutasMensuales(int? mes = null, bool incluirRaizPseEfectySiempre = false)
        {
            var rutas = new List<(string Path, string Filter)>();
            var rutaBase = ObtenerRutaBase();

            // Bancolombia: subcarpeta mensual (ej: "3. Marzo Bancolombia"), archivos .txt y .csv
            var rutaBancolombia = ObtenerRutaMensualConvenio(Path.Combine(rutaBase, "Pagos Convenio Bancolombia"), mes);
            if (rutaBancolombia != null)
            {
                rutas.Add((rutaBancolombia, "*.txt"));
                rutas.Add((rutaBancolombia, "*.csv"));
            }

            // Efecty: usar .lst en subcarpeta mensual. Si no existe, fallback a .csv en raíz.
            var rutaEfecty = ObtenerRutaMensualConvenio(Path.Combine(rutaBase, "Pagos Convenio Efecty"), mes);
            if (rutaEfecty != null)
            {
                rutas.Add((rutaEfecty, "*.lst"));
                if (incluirRaizPseEfectySiempre)
                    rutas.Add((Path.Combine(rutaBase, "Pagos Convenio Efecty"), "*.csv"));
            }
            else
            {
                rutas.Add((Path.Combine(rutaBase, "Pagos Convenio Efecty"), "*.csv"));
            }

            // PSE: usar .xlsx en subcarpeta mensual. Si no existe, fallback a .csv en raíz.
            var rutaPse = ObtenerRutaMensualConvenio(Path.Combine(rutaBase, "Pagos Convenio PSE"), mes);
            if (rutaPse != null)
            {
                rutas.Add((rutaPse, "*.xlsx"));
                if (incluirRaizPseEfectySiempre)
                    rutas.Add((Path.Combine(rutaBase, "Pagos Convenio PSE"), "*.csv"));
            }
            else
            {
                rutas.Add((Path.Combine(rutaBase, "Pagos Convenio PSE"), "*.csv"));
            }

            // Wompi: los archivos .csv quedan en la raíz del convenio
            rutas.Add((Path.Combine(rutaBase, "Pagos Convenio Wompi"), "*.csv"));

            return rutas;
        }

        private string _carpetaPagosDefecto => ObtenerRutaUniversal();

        public ObservableCollection<Pago> Pagos { get; set; } = new ObservableCollection<Pago>();

        public string ConvenioDetectado
        {
            get => _convenioDetectado;
            set { _convenioDetectado = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public int CantidadErrores
        {
            get => _cantidadErrores;
            set { _cantidadErrores = value; OnPropertyChanged(); }
        }

        public int CantidadGuardados
        {
            get => _cantidadGuardados;
            set { _cantidadGuardados = value; OnPropertyChanged(); }
        }

        public int CantidadAplicados
        {
            get => _cantidadAplicados;
            set { _cantidadAplicados = value; OnPropertyChanged(); }
        }

        public int CantidadSinCoincidencia
        {
            get => _cantidadSinCoincidencia;
            set { _cantidadSinCoincidencia = value; OnPropertyChanged(); }
        }

        public int CantidadMultiplesCreditos
        {
            get => _cantidadMultiplesCreditos;
            set { _cantidadMultiplesCreditos = value; OnPropertyChanged(); }
        }

        private Pago? _pagoSeleccionado;
        public Pago? PagoSeleccionado
        {
            get => _pagoSeleccionado;
            set { _pagoSeleccionado = value; OnPropertyChanged(); }
        }

        // ============================================================
        // LOG EN TIEMPO REAL - Panel de actividad visible para el usuario
        // ============================================================
        private string _statusMessage = "Listo";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> LogEntries { get; set; } = new ObservableCollection<string>();

        /// <summary>
        /// Agrega un mensaje al log visible en la UI y actualiza el status.
        /// Thread-safe: puede llamarse desde cualquier hilo.
        /// </summary>
        private void AgregarLog(string mensaje)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var entry = $"[{timestamp}] {mensaje}";

            System.Diagnostics.Debug.WriteLine(entry);

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    LogEntries.Insert(0, entry); // Más reciente arriba
                    if (LogEntries.Count > 50) LogEntries.RemoveAt(LogEntries.Count - 1); // Máximo 50 entradas
                    StatusMessage = mensaje;
                });
            }
        }

        public ICommand ImportCommand { get; }
        public ICommand ExportGoogleSheetsCommand { get; }
        public ICommand ExportAcumulativoCommand { get; }
        public ICommand VerErroresCommand { get; }
        public ICommand ConsultarDesgloseXeoCommand { get; }
        public ICommand ConciliarPagosCommand { get; }
        public ICommand VerHistorialCommand { get; }
        public ICommand CargarPagosPendientesCommand { get; }
        public ICommand CorregirPagoCommand { get; }
        public ICommand ExportarFiduciariaConFechasCommand { get; }

        public MainViewModel(
            IImportadorService importadorService,
            IExcelExportService excelExportService,
            IPagoService pagoService,
            IArchivoPlanoService archivoPlanoService,
            IErpService? erpService,
            IGestorDeProcesosService gestorProcesosService)
        {
            _importadorService = importadorService;
            _excelExportService = excelExportService;
            _pagoService = pagoService;
            _archivoPlanoService = archivoPlanoService;
            _erpService = erpService;
            _gestorProcesosService = gestorProcesosService;
            if (_erpService != null)
            {
                _erpService.OnLog += AgregarLog;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] ERP Service inyectado: {(_erpService != null ? "SI" : "NO")}");
#endif

            // Obtener usuario y equipo de Windows
            _usuarioActual = Environment.UserName;
            _equipoActual = Environment.MachineName;

            // Crear carpeta de pagos si no existe (Universal)
            try
            {
                if (!Directory.Exists(_carpetaPagosDefecto))
                    Directory.CreateDirectory(_carpetaPagosDefecto);
            }
            catch { }

            ImportCommand = new RelayCommand(async (param) => await ImportarArchivo(param as string ?? string.Empty));
            ExportGoogleSheetsCommand = new RelayCommand(async (param) => await ExportarParaGoogleSheets(), (param) => Pagos.Count > 0);
            ExportAcumulativoCommand = new RelayCommand(async (param) => await ExportarAcumulativoFiduciaria(), (param) => Pagos.Count > 0);
            VerErroresCommand = new RelayCommand((param) => MostrarErrores(), (param) => CantidadErrores > 0);
            ConsultarDesgloseXeoCommand = new RelayCommand(async (param) => await ConsultarDesgloseXeo(), (param) => TienePagosAplicados());
            ConciliarPagosCommand = new RelayCommand(async _ => await ConciliarPagosConApi());
            VerHistorialCommand = new RelayCommand(async (param) => await VerHistorialPagos());
            CargarPagosPendientesCommand = new RelayCommand(async (param) => await CargarPagosPendientes());
            CorregirPagoCommand = new RelayCommand(async (param) => await AbrirDialogoCorreccion(param as Pago));
            ExportarFiduciariaConFechasCommand = new RelayCommand(async (param) => await ExportarYBloquearParaFiduciaria());

            // 1. Cargar estado inicial (pagos aplicados del mes y pendientes)
            _ = CargarEstadoInicial();

            // 2. Iniciar Timer de Sincronización (cada 2 minutos)
            IniciarSincronizacion();
        }

        /// <summary>
        /// Verifica si hay pagos aplicados para consultar desglose
        /// </summary>
        private bool TienePagosAplicados()
        {
            return Pagos.Any(p => p.Estado == EstadoPago.Aplicado);
        }

        private async Task<(int Count, string Error)> ImportarArchivo(string ruta, bool silencioso = false)
        {
            if (string.IsNullOrEmpty(ruta)) return (0, string.Empty);

            try
            {
                //_archivoActual = Path.GetFileName(ruta);
                //_carpetaOrigen = Path.GetDirectoryName(ruta) ?? "";
                _archivosProcesados.Add(Path.GetFileName(ruta));

                ImportResult result;

                // Detectar si es archivo plano o CSV
                if (_archivoPlanoService.EsArchivoPlano(ruta))
                {
                    var tipoPlano = _archivoPlanoService.DetectarTipoArchivoPlano(ruta);

                    if (tipoPlano == TipoArchivoPlano.Bancolombia)
                    {
                        result = await _archivoPlanoService.ProcesarArchivoPlanoBancolombiaAsync(ruta);
                    }
                    else if (tipoPlano == TipoArchivoPlano.Efecty)
                    {
                        result = await _archivoPlanoService.ProcesarArchivoPlanoEfectyAsync(ruta);
                    }
                    else
                    {
                        // Para otros tipos de archivo plano que se implementen después
                        result = new ImportResult();
                        result.Excepciones.Add(new ExcepcionPago
                        {
                            Tipo_Error = "TIPO_NO_SOPORTADO",
                            Descripcion_Error = $"Tipo de archivo plano '{tipoPlano}' aún no implementado"
                        });
                    }
                }
                else
                {
                    // Es un archivo CSV/delimitado
                    result = await _importadorService.ImportarArchivoAsync(ruta);
                }

                ConvenioDetectado = string.IsNullOrEmpty(result.TipoConvenioDetectado) ? "Desconocido" : result.TipoConvenioDetectado;
                _excepciones = result.Excepciones;
                CantidadErrores = result.Excepciones.Count;

                // Obtener hashes de los pagos importados para verificar existencia en BD
                var hashesImportados = result.PagosValidos
                    .Where(p => !string.IsNullOrEmpty(p.HashUnico))
                    .Select(p => p.HashUnico)
                    .ToList();

                // Buscar pagos existentes en BD (para reutilizar sus IDs)
                var pagosExistentesEnBD = await _pagoService.ObtenerPagosExistentesPorHashesAsync(hashesImportados);

                int nuevos = 0;
                int omitidos = 0;
                var pagosRealmenteNuevos = new List<Pago>(); // Lista para guardar en BD

                foreach (var pago in result.PagosValidos)
                {
                    // Verificar si ya está en la colección actual (cargado desde BD)
                    if (Pagos.Any(p => p.HashUnico == pago.HashUnico))
                    {
                        omitidos++;
                        continue; // Ya está cargado - NO agregar de nuevo
                    }

                    // Si existe en BD pero NO está en la colección, significa que es de una fecha anterior
                    // NO lo agregamos para evitar perder datos guardados (Titular, CC, desglose, etc.)
                    if (pagosExistentesEnBD.ContainsKey(pago.HashUnico))
                    {
                        omitidos++;
                        continue; // Ya existe en BD - NO agregar
                    }

                    // Es un pago realmente NUEVO - agregarlo
                    nuevos++;
                    Pagos.Insert(0, pago);
                    pagosRealmenteNuevos.Add(pago); // Trackear para guardar en BD
                }

                // ============================================================
                // GUARDAR AUTOMÁTICAMENTE EN BD LOS PAGOS NUEVOS
                // ============================================================
                int guardadosEnBD = 0;
                if (pagosRealmenteNuevos.Count > 0)
                {
                    try
                    {
                        guardadosEnBD = await _pagoService.GuardarPagosAsync(pagosRealmenteNuevos, _usuarioActual, _equipoActual, _archivoActual);
                        System.Diagnostics.Debug.WriteLine($"[IMPORTAR] Guardados en BD: {guardadosEnBD} de {pagosRealmenteNuevos.Count} nuevos");
                    }
                    catch (Exception exBd)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IMPORTAR] Error guardando en BD: {exBd.Message}");
                    }
                }

                var mensaje = $"Convenio: {ConvenioDetectado}\n" +
                              $"Registros procesados: {result.PagosValidos.Count}\n" +
                              $"  - Nuevos (guardados en BD): {guardadosEnBD}\n" +
                              $"  - Ya existían (omitidos): {omitidos}\n" +
                              $"Errores de formato: {result.Excepciones.Count}\n\n" +
                              $"Usuario: {_usuarioActual}\n" +
                              $"Equipo: {_equipoActual}";

                CantidadGuardados = guardadosEnBD;

                if (!silencioso)
                {
                    if (result.Excepciones.Count > 0)
                    {
                        MessageBox.Show(mensaje, "Importación con Errores", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(mensaje, "Importación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                string errorSummary = string.Empty;
                if (result.Excepciones.Count > 0)
                {
                    var primero = result.Excepciones.First();
                    errorSummary = $"{result.Excepciones.Count} errores. Ej: {primero.Tipo_Error}: {primero.Descripcion_Error}";
                }

                return (nuevos, errorSummary);
            }
            catch (Exception ex)
            {
                // Leer encabezado para diagnóstico si es posible
                string header = "No leíble";
                try {
                     var lines = await File.ReadAllLinesAsync(ruta);
                     if (lines.Length > 0) header = lines[0];
                } catch {}

                if (!silencioso)
                    MessageBox.Show($"Error al importar archivo:\n{ex.Message}\nHeader: {header}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    System.Diagnostics.Debug.WriteLine($"Error importando archivo {ruta}: {ex.Message}");

                return (0, $"{ex.Message} [Header: {header}]");
            }
        }

        private void MostrarErrores()
        {
            if (_excepciones.Count == 0) return;

            var erroresTexto = "ERRORES DE IMPORTACIÓN:\n\n";
            foreach (var exc in _excepciones)
            {
                erroresTexto += $"Línea {exc.Numero_Linea}: {exc.Tipo_Error}\n";
                erroresTexto += $"  Detalle: {exc.Descripcion_Error}\n";
                if (!string.IsNullOrEmpty(exc.Linea_Original))
                    erroresTexto += $"  Contenido: {exc.Linea_Original}\n";
                erroresTexto += "\n";
            }

            MessageBox.Show(erroresTexto, $"Errores ({_excepciones.Count})", MessageBoxButton.OK, MessageBoxImage.Warning);
        }



        /// <summary>
        /// Exporta datos simplificados a CSV para Google Sheets (area de BI)
        /// </summary>
        private async Task ExportarParaGoogleSheets()
        {
            if (Pagos.Count == 0)
            {
                MessageBox.Show("No hay datos para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*",
                FileName = $"Pagos_BI_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Exportar para Google Sheets (BI)"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    await _excelExportService.ExportarParaGoogleSheetsAsync(new List<Pago>(Pagos), saveFileDialog.FileName);

                    MessageBox.Show($"Exportacion CSV exitosa.\n\nArchivo: {saveFileDialog.FileName}\nRegistros: {Pagos.Count}\n\nEste archivo puede importarse directamente en Google Sheets.",
                        "Exito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al exportar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Exporta pagos APLICADOS al archivo Excel acumulativo semanal para fiduciaria
        /// </summary>
        private async Task ExportarAcumulativoFiduciaria()
        {
            if (Pagos.Count == 0)
            {
                MessageBox.Show("No hay datos para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Contar pagos aplicados
            var aplicados = Pagos.Count(p => !string.IsNullOrEmpty(p.Resultado) &&
                                              p.Resultado.Equals("Aplicado", StringComparison.OrdinalIgnoreCase));

            if (aplicados == 0)
            {
                MessageBox.Show("No hay pagos con estado 'Aplicado' para agregar al archivo acumulativo.\n\nPrimero debe conciliar los pagos.",
                    "Sin pagos aplicados", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Usar carpeta de pagos por defecto
                var carpetaDestino = _carpetaPagosDefecto;

                var resultado = await _excelExportService.AgregarPagosAcumulativoAsync(
                    new List<Pago>(Pagos), carpetaDestino);

                if (resultado.NuevosRegistros > 0)
                {
                    MessageBox.Show(
                        $"Archivo acumulativo actualizado.\n\n" +
                        $"Archivo: {Path.GetFileName(resultado.Ruta)}\n" +
                        $"Nuevos registros agregados: {resultado.NuevosRegistros}\n" +
                        $"Total registros en archivo: {resultado.TotalRegistros}\n\n" +
                        $"Carpeta: {carpetaDestino}",
                        "Exito - Acumulativo Fiduciaria",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"No se agregaron nuevos registros.\n" +
                        $"Los pagos ya existian en el archivo acumulativo.\n\n" +
                        $"Total registros en archivo: {resultado.TotalRegistros}",
                        "Sin cambios", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar acumulativo:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Concilia pagos contra la API del ERP para aplicarlos automáticamente.
        /// soloNuevos=true: Solo procesa pagos Importado/Corregido (no re-consulta NoCoincide).
        /// soloNuevos=false: Re-procesa todos los no-aplicados (incluye NoCoincide por si ya se aplicaron).
        /// </summary>
        private bool _isConciliando = false;
        private async Task ConciliarPagosConApi(bool silencioso = false, bool soloNuevos = false, DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            if (_gestorProcesosService == null || _erpService == null)
            {
                AgregarLog("[CONCILIACION] Servicio no disponible.");
                return;
            }

            if (_isConciliando)
            {
                AgregarLog("[CONCILIACION] Ya hay una conciliacion en curso.");
                return;
            }

            _isConciliando = true;
            IsBusy = true;
            AgregarLog(soloNuevos
                ? "[CONCILIACION] Conciliando pagos nuevos contra API..."
                : "[CONCILIACION] Iniciando conciliacion contra API (todos los pendientes)...");
            var progress = new Progress<string>(msg => AgregarLog($"[CONCILIACION] {msg}"));

            try
            {
                var pagosObjetivo = Pagos.AsEnumerable();
                if (fechaDesde.HasValue)
                    pagosObjetivo = pagosObjetivo.Where(p => p.FechaConsignacion.Date >= fechaDesde.Value.Date);
                if (fechaHasta.HasValue)
                    pagosObjetivo = pagosObjetivo.Where(p => p.FechaConsignacion.Date <= fechaHasta.Value.Date);

                var pagosObjetivoList = pagosObjetivo.ToList();
                AgregarLog($"[CONCILIACION] Lote filtrado: {pagosObjetivoList.Count} pagos ({(fechaDesde.HasValue ? fechaDesde.Value.ToString("dd/MM/yyyy") : "-∞")} -> {(fechaHasta.HasValue ? fechaHasta.Value.ToString("dd/MM/yyyy") : "+∞")}).");

                var resumen = await _gestorProcesosService.ConciliarPagosConApiAsync(
                    pagosObjetivoList, _usuarioActual, _equipoActual, progress,
                    soloNuevos: soloNuevos);

                // Refrescar grilla
                await CargarEstadoInicial();
                ActualizarContadores();

                AgregarLog($"[CONCILIACION] Finalizada. Conciliados: {resumen.PagosConciliados}, Sin coincidencia: {resumen.PagosSinCoincidencia}");

                if (!silencioso)
                {
                    System.Windows.MessageBox.Show(
                        resumen.MensajeFinal,
                        "Conciliacion API",
                        System.Windows.MessageBoxButton.OK,
                        resumen.PagosConciliados > 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AgregarLog($"[CONCILIACION] Error: {ex.Message}");
                if (!silencioso)
                {
                    System.Windows.MessageBox.Show($"Error en conciliacion: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            finally
            {
                _isConciliando = false;
                IsBusy = false;
            }
        }

        /// <summary>
        /// Abre el diálogo de corrección de cédula y re-concilia el pago.
        /// </summary>
        private async Task AbrirDialogoCorreccion(Pago? pago)
        {
            if (pago == null) return;

            var dialog = new Views.CorrectionDialog(pago);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Guardar la cédula corregida
                    if (!string.IsNullOrWhiteSpace(pago.CedulaCorrecta))
                    {
                        await _pagoService.ActualizarCedulaCorrectaAsync(pago.Id, pago.CedulaCorrecta);
                        AgregarLog($"[CORRECCION] Cédula corregida para pago {pago.IdCorto}: {pago.CedulaCorrecta}");

                        // Re-conciliar con la API
                        await ConciliarPagosConApi(silencioso: true);
                    }
                }
                catch (Exception ex)
                {
                    AgregarLog($"[CORRECCION] Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Procesa el cambio manual de un CC en la grilla.
        /// Consulta el desglose y si hay múltiples créditos, DIVIDE el pago en varias filas.
        /// Lógica delegada al servicio GestorDeProcesosService.
        /// </summary>
        public async Task ProcesarCambioManualCC(Pago pago)
        {
            if (string.IsNullOrWhiteSpace(pago.CC) || _gestorProcesosService == null) return;

            try
            {
                IsBusy = true; // Mostrar indicador de carga

                if (string.IsNullOrWhiteSpace(pago.CedulaCorrecta))
                {
                    MessageBox.Show("El pago no tiene cedula asignada. No se puede consultar el desglose.", "Sin Cedula", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 1. Consultar posibles desgloses (Agrupados por crédito)
                var itemsAProcesar = await _gestorProcesosService.ConsultarPosibleDesgloseAsync(pago.CedulaCorrecta, pago.FechaConsignacion);

                if (itemsAProcesar.Count == 0)
                {
                    MessageBox.Show($"No se encontraron cuotas pagadas para cedula {pago.CedulaCorrecta} en fecha {pago.FechaConsignacion:dd/MM/yyyy}.", "Sin Datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. Confirmar División si es necesario (Interacción UI)
                if (itemsAProcesar.Count > 1)
                {
                    var confirm = MessageBox.Show(
                        $"El comprobante {pago.CC} aplica a {itemsAProcesar.Count} créditos distintos.\n" +
                        $"El pago se dividirá en {itemsAProcesar.Count} registros (uno por crédito).\n\n" +
                        "Desglose propuesto:\n" +
                        string.Join("\n", itemsAProcesar.Select(i => $" - Crédito {i.NumeroCredito}: {i.TotalAplicado:C0}")) + "\n\n" +
                        "¿Desea aplicar esta división?", "Dividir Pago", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes) return;
                }

                // 3. Aplicar Cambios (Lógica en Servicio)
                // Retorna los nuevos pagos creados (si hubo división). El original se modifica por referencia.
                var nuevosPagos = await _gestorProcesosService.AplicarDesgloseEnPagoAsync(
                    pago,
                    itemsAProcesar,
                    _usuarioActual,
                    _equipoActual);

                // 4. Actualizar Grid (Solo agregar los nuevos, el original se actualiza por binding o referencia)
                foreach(var np in nuevosPagos)
                {
                    Pagos.Add(np);
                }

                // Forzar refresco visual caso de que INPC no esté implementado full
                var index = Pagos.IndexOf(pago);
                if (index >= 0) Pagos[index] = pago;

                ActualizarContadores();

                // Si fue un cambio simple (1 crédito), mostrar que se aplicó
                if (itemsAProcesar.Count == 1)
                {
                    // Opcional: Feedback visual sutil (ej. cambio de color o sonido)
                    // No mostramos MessageBox para agilidad
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al procesar CC: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Consulta el desglose de pagos aplicados desde XEO usando GestorDeProcesosService
        /// </summary>
        private async Task ConsultarDesgloseXeo(bool silencioso = false, DateTime? fechaDesde = null, DateTime? fechaHasta = null, bool soloAplicados = true)
        {
            if (_erpService == null || _gestorProcesosService == null)
            {
                if (!silencioso)
                    MessageBox.Show("El servicio de XEO o Gestor de Procesos no está disponible.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

                // Consultar pagos del rango objetivo con datos mínimos para consulta.
                // Regla importante:
                // - Se incluyen no bloqueados.
                // - También se incluyen bloqueados SIN desglose para completar datos faltantes históricos.
                var pagosConCC = Pagos.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.CC) || !string.IsNullOrWhiteSpace(p.CedulaCorrecta) || !string.IsNullOrWhiteSpace(p.CedulaCliente))
                    && !p.TieneDatosAplicacion
                    && (!p.Bloqueado || !p.TieneDatosAplicacion)
                    && (!fechaDesde.HasValue || p.FechaConsignacion.Date >= fechaDesde.Value.Date)
                    && (!fechaHasta.HasValue || p.FechaConsignacion.Date <= fechaHasta.Value.Date)
                    && (!soloAplicados || p.Estado == EstadoPago.Aplicado))
                .ToList();
            var pagosBloqueados = Pagos.Count(p => p.Bloqueado);

            if (pagosConCC.Count == 0)
            {
                if (!silencioso)
                {
                    MessageBox.Show(pagosBloqueados > 0
                        ? $"No hay pagos pendientes de desglose. {pagosBloqueados} ya están bloqueados y con desglose."
                        : "No se encontraron pagos con información suficiente para consultar (CC o Cédula).",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (!silencioso)
            {
                var resultado = MessageBox.Show(
                    $"Se consultará el desglose de {pagosConCC.Count} pagos.\n" +
                    $"Esto llenará las columnas de Capital, Intereses, etc. desde el ERP.\n" +
                    $"Los pagos que cuadren perfectamente serán bloqueados.\n\n" +
                    $"¿Desea continuar?",
                    "Consultar Desglose XEO",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (resultado != MessageBoxResult.Yes) return;
            }

            _isValidating = true;
            System.Windows.Threading.DispatcherTimer? refreshTimer = null;
            try
            {
                // Refresco periódico: solo contadores y StatusMessage.
                // El RefrescarGrid completo (clear+add) es costoso con muchos pagos;
                // se hace solo cuando el servicio reporta un guardado nuevo.
                refreshTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                refreshTimer.Tick += (s, e) =>
                {
                    ActualizarContadores();
                };
                refreshTimer.Start();

                // Usar Progress<string> para reportar logs a la UI y reflejar avances en vivo.
                int actualizacionesEnVivo = 0;
                var progress = new Progress<string>(msg =>
                {
                    AgregarLog($"[SERVICE] {msg}");

                    // El servicio reporta cada guardado individual.
                    if (msg.StartsWith("Desglose guardado:", StringComparison.OrdinalIgnoreCase))
                    {
                        actualizacionesEnVivo++;
                        // Refrescar la grilla cada 10 desgloses guardados para ver progreso
                        if (actualizacionesEnVivo % 10 == 0)
                        {
                            StatusMessage = $"Desglose en progreso: {actualizacionesEnVivo} guardados...";
                            RefrescarGrid();
                            ActualizarContadores();
                        }
                    }
                });

                // LLAMADA AL SERVICIO (Lógica de negocio extraída)
                var resumen = await _gestorProcesosService.ProcesarDesgloseAsync(
                    pagosConCC,
                    _usuarioActual,
                    DateTime.UtcNow,
                    progress);

                // Refrescar la vista (necesario porque ObservableCollection no detecta cambios en propiedades anidadas si no hay INPC)
                var pagosTemp = Pagos.ToList();
                Pagos.Clear();
                foreach (var p in pagosTemp) Pagos.Add(p);

                if (!silencioso)
                {
                    MessageBox.Show(resumen.MensajeFinal, "Desglose Completo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AgregarLog($"Desglose XEO: ERROR - {ex.Message}");
                MessageBox.Show($"Error al consultar desglose:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Stop();
                }

                // Refresco final para dejar la vista consistente al terminar.
                RefrescarGrid();
                ActualizarContadores();
                _isValidating = false;
            }
        }

        private async Task SincronizarDatos()
        {
            if (_isScanning)
            {
                MessageBox.Show("El sistema está escaneando archivos. Por favor espere.", "Ocupado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsBusy = true;
                await _dbSemaphore.WaitAsync();

                await CargarEstadoInicial();

                MessageBox.Show("Sincronización manual completada.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al sincronizar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _dbSemaphore.Release();
                IsBusy = false;
            }
        }

        private async Task SincronizarDatosSilencioso()
        {
            if (_isScanning || _isValidating) return;

            try
            {
                if (await _dbSemaphore.WaitAsync(0)) // TryAcquire inmediato
                {
                    AgregarLog("Sincronizando con base de datos...");
                    try
                    {
                         await CargarEstadoInicial();
                    }
                    finally
                    {
                        _dbSemaphore.Release();
                    }
                }
            }
            catch { /* Ignorar errores en silencioso */ }
        }

        /// <summary>
        /// Muestra el historial de pagos procesados (bloqueados) del último mes
        /// </summary>
        private async Task VerHistorialPagos()
        {
            if (_pagoService == null)
            {
                MessageBox.Show("El servicio de pagos no está disponible.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Obtener pagos bloqueados del último mes
                var fechaLimite = DateTime.UtcNow.AddMonths(-1);
                // Usar BuscarPagosAsync con fecha desde hace un mes
                var pagosHistorial = await _pagoService.BuscarPagosAsync(fechaDesde: fechaLimite);
                var pagosBloqueados = pagosHistorial
                    .Where(p => p.Bloqueado && p.FechaBloqueo >= fechaLimite)
                    .OrderByDescending(p => p.FechaBloqueo)
                    .ToList();

                if (pagosBloqueados.Count == 0)
                {
                    MessageBox.Show(
                        "No hay pagos procesados (bloqueados) en el último mes.",
                        "Historial", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var resultado = MessageBox.Show(
                    $"Se encontraron {pagosBloqueados.Count} pagos procesados en el último mes.\n\n" +
                    $"Monto total: ${pagosBloqueados.Sum(p => p.Monto):N0}\n\n" +
                    $"¿Desea cargarlos en la vista actual?\n\n" +
                    $"(Esto reemplazará los pagos actuales)",
                    "Historial de Pagos",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (resultado != MessageBoxResult.Yes)
                    return;

                Pagos.Clear();
                foreach (var pago in pagosBloqueados)
                {
                    Pagos.Add(pago);
                }

                MessageBox.Show(
                   $"Se han cargado {pagosBloqueados.Count} registros del mes.",
                   "Historial Cargado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar historial:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarPagosPendientes()
        {
             // Implementacion basica para el boton
             await CargarEstadoInicial();
             MessageBox.Show("Pagos actualizados.", "Sincronización", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        // ============================================================
        // AUTOMATIZACIÓN DE ARCHIVOS (WATCHERS MULTI-CONVENIO)
        // ============================================================

        private List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        private void IniciarMonitoreo()
        {
            StopMonitoreo(); // Limpiar anteriores si existen

            try
            {
                // ============================================================
                // 1. BANCOLOMBIA (Estructura Mensual)
                // ============================================================
                // Input: Carpeta Mensual (12. Diciembre...). Filtro: *.txt, *.csv
                var rutaBancolombiaInput = ObtenerRutaUniversal();
                if (Directory.Exists(rutaBancolombiaInput))
                {
                    AgregarWatcher(rutaBancolombiaInput, "*.txt");
                    AgregarWatcher(rutaBancolombiaInput, "*.csv");
                }

                // ============================================================
                // 2. EFECTY (Estructura Unica/Plana)
                // ============================================================
                var rutaEfecty = Path.Combine(ObtenerRutaBase(), "Pagos Convenio Efecty");
                if (Directory.Exists(rutaEfecty))
                {
                    AgregarWatcher(rutaEfecty, "*.csv");
                }

                // ============================================================
                // 3. PSE (Estructura Unica/Plana)
                // ============================================================
                var rutaPse = Path.Combine(ObtenerRutaBase(), "Pagos Convenio PSE");
                if (Directory.Exists(rutaPse))
                {
                    AgregarWatcher(rutaPse, "*.csv");
                }

                // ============================================================
                // 4. WOMPI (Estructura Unica/Plana)
                // ============================================================
                var rutaWompi = Path.Combine(ObtenerRutaBase(), "Pagos Convenio Wompi");
                if (Directory.Exists(rutaWompi))
                {
                    AgregarWatcher(rutaWompi, "*.csv");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error iniciando monitoreo: {ex.Message}");
            }
        }

        private string ObtenerRutaBase()
        {
             return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Inklusiva s.a.s",
                "Sistemas Inklusiva - Documentos",
                "Documentos en RED",
                "Pagos Inklusiva");
        }

        private void StopMonitoreo()
        {
            foreach(var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }

        private void AgregarWatcher(string path, string filter)
        {
            var w = new FileSystemWatcher(path);
            w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
            w.Filter = filter;
            w.Created += OnNuevoPagoDetectado;
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }

        private async void OnNuevoPagoDetectado(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Filtro de Seguridad: Ignorar archivos que contienen "Aplicados" si se colaron por *.csv
                if (e.Name != null && e.Name.IndexOf("Aplicados", StringComparison.OrdinalIgnoreCase) >= 0) return;

                // Guard: no procesar si ya estamos ocupados
                if (IsBusy || _isConciliando) return;

                AgregarLog($"Archivo detectado: {e.Name} - Procesando...");
                // Esperar a que el archivo termine de escribirse
                await Task.Delay(2000);

                int intentos = 0;
                while (intentos < 5)
                {
                    try
                    {
                        using (File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }

                        // Usar InvokeAsync con double-await para esperar correctamente la tarea async
                        var dispatcherOp = Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            if (!IsBusy && !_isConciliando)
                            {
                                // Guardar conteo ANTES de importar
                                int pagosAntes = Pagos.Count;

                                var result = await ImportarArchivo(e.FullPath, silencioso: true);
                                System.Diagnostics.Debug.WriteLine($"Archivo auto-detectado: {e.Name} - Importados: {result.Count}");

                                // Solo informar si se importaron pagos NUEVOS
                                int pagosNuevos = Pagos.Count - pagosAntes;
                                if (pagosNuevos > 0)
                                {
                                    AgregarLog($"  + {pagosNuevos} pagos nuevos de {e.Name}. Use 'Conciliar API' para procesar.");
                                }
                            }
                        });
                        var innerTask = await dispatcherOp.Task;
                        await innerTask;

                        break;
                    }
                    catch (IOException)
                    {
                        intentos++;
                        await Task.Delay(1000 * intentos);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error procesando archivo {e.Name}: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Catch-all para async void: evitar que una excepcion no manejada crashee la app
                System.Diagnostics.Debug.WriteLine($"Error fatal en OnNuevoPagoDetectado: {ex.Message}");
            }
        }

        // ============================================================
        // SINCRONIZACIÓN Y CARGA INICIAL
        // ============================================================

        private System.Windows.Threading.DispatcherTimer? _timerSincronizacion;

        // Semáforo para controlar acceso concurrente a la BD (evita 'Command already in progress')
        private readonly System.Threading.SemaphoreSlim _dbSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private bool _isScanning = false;
        private bool _isValidating = false;

        private void IniciarSincronizacion()
        {
            _timerSincronizacion = new System.Windows.Threading.DispatcherTimer();
            _timerSincronizacion.Interval = TimeSpan.FromMinutes(2); // Sincronizar cada 2 minutos
            _timerSincronizacion.Tick += async (s, e) =>
            {
                 // Si estamos escaneando archivos, saltar este tick para no chocar
                 if (_isScanning) return;
                 await SincronizarDatosSilencioso();
            };
            _timerSincronizacion.Start();

            // Iniciar monitoreo de archivos (Watchers)
            IniciarMonitoreo();

            // Procesar archivos existentes (que ya estaban antes de abrir la app)
            // Ejecutar en segundo plano para no bloquear la UI en el arranque
            Task.Run(async () => await ProcesarArchivosExistentes());
        }

        private async Task ProcesarArchivosExistentes()
        {
            if (_isScanning) return;
            _isScanning = true;

            AgregarLog($"Iniciando escaneo de carpetas ({FechaOperacionDesde:dd/MM/yyyy} -> {DateTime.Today:dd/MM/yyyy})...");

            // Esperar un momento a que la UI cargue
            await Task.Delay(3000);

            // ============================================================
            // ESCANEAR archivos del mes actual completo
            // La protección contra duplicados está en ImportarArchivo()
            // que verifica HashUnico antes de agregar
            // ============================================================
            int pagosEnMemoria = 0;
            Application.Current.Dispatcher.Invoke(() => pagosEnMemoria = Pagos.Count);

            AgregarLog($"Cargados {pagosEnMemoria} pagos desde BD. Buscando archivos en rango operativo...");
            System.Diagnostics.Debug.WriteLine($"[SCAN] Iniciando escaneo mensual. Pagos en memoria: {pagosEnMemoria}");

            try
            {
                // Rutas a verificar: descubrimiento dinámico de carpetas mensuales
                var rutasInput = ObtenerTodasRutasMensuales(incluirRaizPseEfectySiempre: false);

                var sbDebug = new System.Text.StringBuilder();
                sbDebug.AppendLine($"--- INICIO ESCANEO MENSUAL ---");

                bool huboNuevos = false;
                int totalArchivos = 0;
                int totalNuevos = 0;

                // Fecha limite operativa solicitada
                DateTime fechaLimiteMes = FechaOperacionDesde.Date;

                // Procesar Inputs
                foreach (var (path, filter) in rutasInput)
                {
                    if (!Directory.Exists(path))
                    {
                        sbDebug.AppendLine($"[NO EXISTE] Path: {path}");
                        continue;
                    }

                    var archivos = Directory.GetFiles(path, filter);
                    var carpetaCorta = Path.GetFileName(path);
                    AgregarLog($"Escaneando {carpetaCorta} ({filter}) - {archivos.Length} archivos");
                    sbDebug.AppendLine($"[SCAN] {path} ({filter}) -> {archivos.Length} archivos.");

                    foreach (var archivo in archivos)
                    {
                        var info = new FileInfo(archivo);

                        // Escanear archivos del mes actual completo
                        bool esDelMes = info.LastWriteTime.Date >= fechaLimiteMes || info.CreationTime.Date >= fechaLimiteMes;

                        if (esDelMes)
                        {
                            // Ignorar si es un resultado (tiene "Aplicados")
                            if (info.Name.Contains("Aplicados", StringComparison.OrdinalIgnoreCase))
                            {
                                sbDebug.AppendLine($"  [SKIP] {info.Name} (Es un resultado)");
                                continue;
                            }

                            totalArchivos++;

                            // SOLO IMPORTAR - No validar per-file para no saturar API
                            var dispatcherOp = Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                int pagosAntes = Pagos.Count;

                                var res = await ImportarArchivo(archivo, silencioso: true);

                                int pagosNuevos = Pagos.Count - pagosAntes;
                                if (pagosNuevos > 0)
                                {
                                    AgregarLog($"  + {pagosNuevos} pagos nuevos de {info.Name}");
                                }

                                return (res, pagosNuevos);
                            });

                            var innerTask = await dispatcherOp.Task;
                            var (importResult, nuevos) = await innerTask;

                            if (nuevos > 0)
                            {
                                huboNuevos = true;
                                totalNuevos += nuevos;
                            }

                            sbDebug.Append($"  {info.Name} ({importResult.Count} imp)");
                            if (!string.IsNullOrEmpty(importResult.Error))
                                sbDebug.AppendLine($" [ERR: {importResult.Error}]");
                            else
                                sbDebug.AppendLine();
                        }
                    }
                }

                AgregarLog($"Escaneo: {totalArchivos} archivos procesados, {totalNuevos} pagos nuevos encontrados.");

                // Si hubo pagos nuevos importados, informar al usuario (NO conciliar automáticamente)
                if (huboNuevos)
                {
                    AgregarLog($"{totalNuevos} pagos nuevos importados. Use 'Conciliar API' para procesar.");
                }

                System.Diagnostics.Debug.WriteLine(sbDebug.ToString());
            }
            catch (Exception ex)
            {
                AgregarLog($"Error en escaneo: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
                AgregarLog($"Escaneo completado. Total pagos en sistema: {Pagos.Count}");
            }
        }

        private async Task CargarEstadoInicial()
        {
            AgregarLog("Cargando pagos desde base de datos...");
            try
            {
                // MIGRACIÓN MANUAL: Verificar esquema de BD
                await _pagoService.VerificarEsquemaAsync();

                // Cargar solo la ventana operativa para evitar procesar históricos masivos.
                var fechaDesde = DateTime.SpecifyKind(FechaOperacionDesde, DateTimeKind.Utc);
                var fechaHasta = DateTime.SpecifyKind(DateTime.Today.AddDays(1), DateTimeKind.Utc);

                var pagosActivos = await _pagoService.BuscarPagosAsync(fechaDesde: fechaDesde, fechaHasta: fechaHasta);

                // DIAGNÓSTICO: Mostrar estadísticas de carga
                var bloqueados = pagosActivos.Count(p => p.Bloqueado);
                var conDesglose = pagosActivos.Count(p => p.Capital > 0);
                var aplicados = pagosActivos.Count(p => p.Estado == EstadoPago.Aplicado);
                AgregarLog($"BD: {pagosActivos.Count} pagos cargados (Aplicados: {aplicados}, Bloqueados: {bloqueados})");
                System.Diagnostics.Debug.WriteLine($"[CARGA] Total: {pagosActivos.Count}, Bloqueados: {bloqueados}, ConDesglose: {conDesglose}, Aplicados: {aplicados}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Pagos.Clear();
                    foreach (var pago in pagosActivos)
                    {
                        Pagos.Add(pago);
                    }

                    // Actualizar contadores
                    ActualizarContadores();

                    // DIAGNÓSTICO al usuario (solo la primera vez)
                    if (bloqueados > 0 || conDesglose > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CARGA-UI] Pagos cargados desde BD - Bloqueados: {bloqueados}, Con Desglose: {conDesglose}");
                    }
                });

                System.Diagnostics.Debug.WriteLine($"[UI] Cargados {pagosActivos.Count} pagos desde {fechaDesde:dd/MM/yyyy} hasta {fechaHasta.AddDays(-1):dd/MM/yyyy}");
            }
            catch (Exception ex)
            {
                // Loguear error silenciosamente o mostrar si es crítico
                System.Diagnostics.Debug.WriteLine($"Error cargando estado inicial: {ex.Message}");
            }
        }



        private void ActualizarContadores()
        {
            CantidadGuardados = Pagos.Count;
            CantidadAplicados = Pagos.Count(p => p.Estado == EstadoPago.Aplicado);
            CantidadSinCoincidencia = Pagos.Count(p => p.Estado == EstadoPago.NoCoincide);
            CantidadErrores = Pagos.Count(p => p.Estado == EstadoPago.ConError);
            CantidadMultiplesCreditos = Pagos.Count(p => !string.IsNullOrEmpty(p.ListaCreditos));
        }


        /// <summary>
        /// Refresca el DataGrid para mostrar cambios de estado
        /// </summary>
        private void RefrescarGrid()
        {
            var pagosTemp = Pagos.ToList();
            Pagos.Clear();
            foreach (var p in pagosTemp)
            {
                Pagos.Add(p);
            }
        }

        /// <summary>
        /// Pipeline completo de procesamiento para fiduciaria:
        /// 1. Escanea archivos planos del disco (desde fechaDesde → hoy)
        /// 2. Importa y guarda en BD
        /// 3. Concilia contra API (¿ya fueron aplicados?)
        /// 4. Consulta desglose de pagos aplicados
        /// 5. Bloquea los que hicieron match exacto
        /// 6. Exporta a Excel para fiduciaria
        /// </summary>
        private async Task ExportarYBloquearParaFiduciaria()
        {
            if (_gestorProcesosService == null)
            {
                MessageBox.Show("El servicio de Gestor de Procesos no está disponible.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Rango de fechas: 6 de marzo 2026 → hoy
            // Fechas locales para filtrar archivos del disco
            var fechaDesde = FechaOperacionDesde;
            var fechaHasta = DateTime.Today;
            // Fechas UTC para consultas a PostgreSQL (Npgsql exige DateTimeKind.Utc)
            var fechaDesdeUtc = DateTime.SpecifyKind(fechaDesde, DateTimeKind.Utc);
            var fechaHastaUtc = DateTime.SpecifyKind(fechaHasta.AddDays(1), DateTimeKind.Utc);

            // Confirmar con el usuario antes de ejecutar
            var confirmacion = MessageBox.Show(
                $"PIPELINE COMPLETO para Fiduciaria\n\n" +
                $"Rango: {fechaDesde:dd/MM/yyyy} → {fechaHasta:dd/MM/yyyy}\n\n" +
                $"Este proceso ejecutará:\n" +
                $"  1. Escanear archivos planos del disco\n" +
                $"  2. Importar y guardar en BD\n" +
                $"  3. Conciliar contra API (¿aplicados?)\n" +
                $"  4. Consultar desglose de pagos aplicados\n" +
                $"  5. Bloquear pagos cuadrados\n" +
                $"  6. Exportar Excel para fiduciaria\n\n" +
                $"⚠️ Este proceso puede tardar varios minutos.\n" +
                $"¿Desea continuar?",
                "Pipeline Fiduciaria",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes) return;

            IsBusy = true;
            AgregarLog("═══════════════════════════════════════════════");
            AgregarLog("[FIDUCIARIA] ▶ Iniciando Pipeline Completo...");
            AgregarLog($"[FIDUCIARIA] Rango: {fechaDesde:dd/MM/yyyy} → {fechaHasta:dd/MM/yyyy}");

            try
            {
                // ============================================================
                // PASO 1: ESCANEAR E IMPORTAR ARCHIVOS PLANOS DEL DISCO
                // ============================================================
                AgregarLog("[FIDUCIARIA] PASO 1/6: Escaneando archivos planos...");

                // Descubrimiento dinámico de carpetas mensuales (mes actual = marzo)
                var rutasInput = ObtenerTodasRutasMensuales(incluirRaizPseEfectySiempre: false);

                int totalArchivos = 0;
                int totalNuevos = 0;

                foreach (var (path, filter) in rutasInput)
                {
                    if (!Directory.Exists(path)) continue;

                    var archivos = Directory.GetFiles(path, filter);
                    var carpetaCorta = Path.GetFileName(path);
                    AgregarLog($"  Escaneando {carpetaCorta} ({filter}) — {archivos.Length} archivos");

                    foreach (var archivo in archivos)
                    {
                        var info = new FileInfo(archivo);

                        // Filtrar por fecha del archivo: desde fechaDesde hasta hoy
                        bool enRango = (info.LastWriteTime.Date >= fechaDesde && info.LastWriteTime.Date <= fechaHasta)
                                    || (info.CreationTime.Date >= fechaDesde && info.CreationTime.Date <= fechaHasta);
                        if (!enRango) continue;

                        // Ignorar archivos de resultado
                        if (info.Name.Contains("Aplicados", StringComparison.OrdinalIgnoreCase)) continue;

                        totalArchivos++;
                        int pagosAntes = Pagos.Count;

                        var result = await ImportarArchivo(archivo, silencioso: true);

                        int pagosNuevos = Pagos.Count - pagosAntes;
                        if (pagosNuevos > 0)
                        {
                            totalNuevos += pagosNuevos;
                            AgregarLog($"    + {pagosNuevos} pagos de {info.Name}");
                        }
                    }
                }

                AgregarLog($"[FIDUCIARIA] ✓ Paso 1: {totalArchivos} archivos, {totalNuevos} pagos nuevos. Total: {Pagos.Count}");

                if (Pagos.Count == 0)
                {
                    MessageBox.Show("No se encontraron archivos planos en el rango de fechas.",
                        "Sin datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // ============================================================
                // PASO 2: CONCILIAR CONTRA API (¿ya fueron aplicados?)
                // ============================================================
                AgregarLog("[FIDUCIARIA] PASO 2/6: Conciliando contra API...");
                await ConciliarPagosConApi(silencioso: true, soloNuevos: true, fechaDesde: fechaDesde, fechaHasta: fechaHasta);
                ActualizarContadores();
                AgregarLog($"[FIDUCIARIA] ✓ Paso 2: Conciliación finalizada. Aplicados: {CantidadAplicados}");

                // ============================================================
                // PASO 3: CONSULTAR DESGLOSE DE PAGOS APLICADOS
                // ============================================================
                AgregarLog("[FIDUCIARIA] PASO 3/6: Consultando desglose...");
                await ConsultarDesgloseXeo(silencioso: true, fechaDesde: fechaDesde, fechaHasta: fechaHasta, soloAplicados: true);
                ActualizarContadores();
                AgregarLog("[FIDUCIARIA] ✓ Paso 3: Desglose completado.");

                // ============================================================
                // PASO 4-6: BLOQUEAR + EXPORTAR (orquestador)
                // ============================================================
                AgregarLog("[FIDUCIARIA] PASO 4-6: Bloqueando y exportando...");

                var progress = new Progress<string>(msg => AgregarLog($"[FIDUCIARIA] {msg}"));
                var carpetaDestino = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                var resumen = await _gestorProcesosService.ExportarYBloquearFiduciariaAsync(
                    fechaDesdeUtc, fechaHastaUtc,
                    _usuarioActual, _equipoActual, carpetaDestino,
                    progress);

                // Refrescar la grilla
                await CargarEstadoInicial();
                ActualizarContadores();

                AgregarLog($"[FIDUCIARIA] ✓ Pipeline completado. Bloqueados: {resumen.TotalBloqueados}, Exportados: {resumen.TotalExportados}");
                AgregarLog("═══════════════════════════════════════════════");

                MessageBox.Show(
                    $"PIPELINE COMPLETO\n\n" +
                    $"Archivos escaneados: {totalArchivos}\n" +
                    $"Pagos nuevos importados: {totalNuevos}\n\n" +
                    resumen.MensajeFinal,
                    "Pipeline Fiduciaria",
                    MessageBoxButton.OK,
                    resumen.TotalExportados > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (IOException ioEx)
            {
                AgregarLog($"[FIDUCIARIA] Error: {ioEx.Message}");
                MessageBox.Show(
                    $"No se pudo guardar el archivo Excel.\n\n" +
                    $"{ioEx.Message}\n\n" +
                    $"Por favor cierre el archivo en Excel y vuelva a intentar.",
                    "Archivo en uso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AgregarLog($"[FIDUCIARIA] Error: {ex.Message}");
                MessageBox.Show(
                    $"Error en pipeline fiduciaria:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
