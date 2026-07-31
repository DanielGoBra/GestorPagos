using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;
using GestorPagos.Core.Services;

namespace GestorPagos.Data.Services
{
    /// <summary>
    /// Implementación del servicio de integración con el ERP XEO
    /// Usa el endpoint /plan_pagos_actualizado_cliente para consultar créditos por cédula
    /// </summary>
    public class ErpService : IErpService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _token;
        private bool _disposed;
        private string? _planPagosEndpoint;
        private string? _desgloseMovimientosEndpoint;
        private bool _desgloseMovimientosNoDisponible;
        
        public event Action<string>? OnLog;
        
        // Archivo de log para diagnóstico
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
            "GestorPagos_XEO_Log.txt");

        // Cache simple para evitar consultas repetidas
        private readonly Dictionary<string, (ClienteERP Cliente, DateTime Expira)> _cache = new();
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        
        /// <summary>
        /// Escribe mensaje en archivo de log en el escritorio y dispara evento
        /// </summary>
        private void Log(string mensaje)
        {
            try
            {
                // Enmascarar token en el mensaje para no exponerlo en logs
                var mensajeSafe = !string.IsNullOrEmpty(_token)
                    ? mensaje.Replace(_token, "***")
                    : mensaje;
                var linea = $"[{DateTime.Now:HH:mm:ss}] {mensajeSafe}";
                File.AppendAllText(_logPath, linea + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(linea);
                OnLog?.Invoke(mensajeSafe);
            }
            catch { /* Ignorar errores de logging */ }
        }

        public ErpService(string baseUrl, string token)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _token = token;
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            Log($"=== ErpService iniciado. Log: {_logPath} ===");
        }

        private async Task<string> ResolverEndpointPlanPagosAsync()
        {
            if (!string.IsNullOrWhiteSpace(_planPagosEndpoint))
                return _planPagosEndpoint;

            // Orden de preferencia: endpoint real actual y luego alias legado
            var candidatos = new[] { "Plan_de_pagos_Final", "plan_pagos_actualizado_cliente" };

            foreach (var endpoint in candidatos)
            {
                try
                {
                    var url = $"{_baseUrl}/{endpoint}?token={_token}&cedula=0";
                    var resp = await _httpClient.GetAsync(url);
                    if ((int)resp.StatusCode < 500 && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        _planPagosEndpoint = endpoint;
                        Log($"[API] Endpoint plan de pagos resuelto: /{endpoint}");
                        return _planPagosEndpoint;
                    }
                }
                catch
                {
                    // Intentar siguiente candidato
                }
            }

            // Fallback seguro
            _planPagosEndpoint = "Plan_de_pagos_Final";
            Log($"[API] No se pudo verificar endpoint de plan de pagos. Usando fallback: /{_planPagosEndpoint}");
            return _planPagosEndpoint;
        }

        private async Task<string?> ResolverEndpointDesgloseMovimientosAsync()
        {
            if (_desgloseMovimientosNoDisponible)
                return null;

            if (!string.IsNullOrWhiteSpace(_desgloseMovimientosEndpoint))
                return _desgloseMovimientosEndpoint;

            // El endpoint puede variar por despliegue; probar variantes conocidas
            var candidatos = new[] { "desglose_movimientos", "desglose_movimiento", "desglose_movimientos_cc" };

            foreach (var endpoint in candidatos)
            {
                try
                {
                    var url = $"{_baseUrl}/{endpoint}?token={_token}&num_m=0";
                    var resp = await _httpClient.GetAsync(url);

                    // Si no es 404, el endpoint existe (aunque retorne error de negocio)
                    if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        _desgloseMovimientosEndpoint = endpoint;
                        Log($"[API] Endpoint desglose movimientos resuelto: /{endpoint}");
                        return _desgloseMovimientosEndpoint;
                    }
                }
                catch
                {
                    // Intentar siguiente candidato
                }
            }

            _desgloseMovimientosNoDisponible = true;
            Log("[API] Endpoint de desglose por CC no disponible en el servidor. Se omitira fallback por movimiento.");
            return null;
        }

        /// <summary>
        /// Ejecuta una operación HTTP con reintentos y backoff exponencial.
        /// </summary>
        private async Task<T?> ConReintentosAsync<T>(Func<Task<T>> operacion, string contexto, int maxReintentos = 2) where T : class
        {
            for (int intento = 0; intento <= maxReintentos; intento++)
            {
                try
                {
                    return await operacion();
                }
                catch (HttpRequestException ex) when (intento < maxReintentos)
                {
                    var espera = (int)Math.Pow(2, intento) * 1000; // 1s, 2s, 4s
                    Log($"[RETRY] {contexto} - Intento {intento + 1}/{maxReintentos} fallido ({ex.Message}). Reintentando en {espera}ms...");
                    await Task.Delay(espera);
                }
                catch (TaskCanceledException) when (intento < maxReintentos)
                {
                    var espera = (int)Math.Pow(2, intento) * 1000;
                    Log($"[RETRY] {contexto} - Timeout intento {intento + 1}/{maxReintentos}. Reintentando en {espera}ms...");
                    await Task.Delay(espera);
                }
            }
            return null;
        }
        
        /// <summary>
        /// Limpia el cache para forzar consultas frescas
        /// </summary>
        public void LimpiarCache()
        {
            _cache.Clear();
        }

        public async Task<ClienteERP?> ConsultarClientePorCedulaAsync(string cedula)
        {
            if (string.IsNullOrWhiteSpace(cedula))
                return null;

            cedula = cedula.Trim();

            // Verificar cache
            if (_cache.TryGetValue(cedula, out var cached) && cached.Expira > DateTime.Now)
            {
                return cached.Cliente;
            }

            try
            {
                // Usar el endpoint existente: /plan_pagos_actualizado_cliente
                var endpoint = await ResolverEndpointPlanPagosAsync();
                var url = $"{_baseUrl}/{endpoint}?token={_token}&cedula={cedula}";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var cliente = ParsearRespuestaPlanPagos(json, cedula);
                    
                    // Guardar en cache
                    _cache[cedula] = (cliente, DateTime.Now.Add(_cacheDuration));
                    return cliente;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Token de API inválido o expirado");
                    return null;
                }
                else
                {
                    Console.WriteLine($"Error del ERP: {response.StatusCode}");
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error de conexión con ERP: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Timeout al consultar ERP");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inesperado consultando ERP: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parsea la respuesta del endpoint /plan_pagos_actualizado_cliente
        /// La respuesta es un array donde cada elemento es una cuota de un crédito
        /// </summary>
        private ClienteERP ParsearRespuestaPlanPagos(string json, string cedula)
        {
            var cliente = new ClienteERP
            {
                Cedula = cedula
            };

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // La respuesta es un array de cuotas
                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Agrupar por crédito (campo CREDITO)
                    var creditosDict = new Dictionary<string, CreditoERP>();
                    
                    // Solo log resumido para no inflar el archivo (74MB+ con detalle por campo)
                    System.Diagnostics.Debug.WriteLine($"[XEO] Respuesta para {cedula}: {root.GetArrayLength()} elementos");

                    if (root.GetArrayLength() > 0)
                    {
                        var primerElemento = root[0];
                        string nombre = "";
                        
                        // Intentar varios nombres de campo comunes en XEO (incluyendo variantes con/sin espacios)
                        var camposNombre = new[] { 
                            "NOMBRE", "NOMBRE_TERCERO", "TERCERO", "CLIENTE", "DEUDOR", "TITULAR",
                            "NOMBRES", "NOM_TERCERO", "NOMB_TERCERO", "NOMBRE_CLIENTE", 
                            "RAZON_SOCIAL", "RAZON SOCIAL", "NOMBRE COMPLETO", "NOMBRE_COMPLETO",
                            "NOM", "NOMB", "NOMBRE DEUDOR", "NOMBRE_DEUDOR"
                        };
                        
                        foreach (var campoNombre in camposNombre)
                        {
                            if (primerElemento.TryGetProperty(campoNombre, out var nomEl))
                            {
                                var valorNombre = nomEl.GetString();
                                if (!string.IsNullOrWhiteSpace(valorNombre))
                                {
                                    nombre = valorNombre;
                                    Log($"[XEO] ✓ NOMBRE encontrado en campo '{campoNombre}': '{nombre}'");
                                    break;
                                }
                            }
                        }
                        
                        // Si aún no encontramos nombre, buscar cualquier campo que contenga "NOMBRE" o "TERCERO"
                        if (string.IsNullOrWhiteSpace(nombre))
                        {
                            Log($"[XEO] No se encontró en campos conocidos, buscando por contenido...");
                            foreach (var prop in primerElemento.EnumerateObject())
                            {
                                if ((prop.Name.Contains("NOMBRE", StringComparison.OrdinalIgnoreCase) || 
                                     prop.Name.Contains("TERCERO", StringComparison.OrdinalIgnoreCase) ||
                                     prop.Name.Contains("CLIENTE", StringComparison.OrdinalIgnoreCase)) &&
                                    prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    var valorProp = prop.Value.GetString();
                                    if (!string.IsNullOrWhiteSpace(valorProp) && valorProp.Length > 3)
                                    {
                                        nombre = valorProp;
                                        Log($"[XEO] ✓ NOMBRE encontrado por búsqueda flexible en '{prop.Name}': '{nombre}'");
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (!string.IsNullOrWhiteSpace(nombre))
                        {
                            cliente.NombreCompleto = nombre.Trim();
                            Log($"[XEO] ✓ NOMBRE FINAL para {cedula}: '{cliente.NombreCompleto}'");
                        }
                        else
                        {
                            Log($"[XEO] ✗ NO SE ENCONTRÓ NOMBRE para {cedula}");
                        }
                    }
                    else
                    {
                        Log($"[XEO] ✗ Respuesta vacía (0 elementos) para cédula {cedula}");
                    }

                    foreach (var cuotaJson in root.EnumerateArray())
                    {
                        string? numeroCredito = null;
                        if (cuotaJson.TryGetProperty("CREDITO", out var creditoElement))
                            numeroCredito = creditoElement.GetString();

                        if (string.IsNullOrEmpty(numeroCredito))
                            continue;

                        // Crear crédito si no existe
                        if (!creditosDict.ContainsKey(numeroCredito))
                        {
                            var credito = new CreditoERP
                            {
                                NumeroCredito = numeroCredito,
                                CuotasPendientes = new List<CuotaERP>()
                            };

                            // Parsear partes del número de crédito: "111 CR 23 1434"
                            var partes = numeroCredito.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (partes.Length >= 4)
                            {
                                credito.TipoCredito = partes[1];      // CR
                                credito.LineaCredito = partes[2];     // 23
                                credito.NumeroSecuencial = partes[3]; // 1434
                            }

                            // Valor de desembolso
                            if (cuotaJson.TryGetProperty("V. DESEMBOLSO", out var valorElement))
                                credito.ValorDesembolso = ObtenerDecimal(valorElement);

                            // Fecha de desembolso
                            if (cuotaJson.TryGetProperty("F. DESEMBOLSO", out var fechaElement))
                            {
                                var fechaStr = fechaElement.GetString();
                                if (DateTime.TryParse(fechaStr, out var fecha))
                                    credito.FechaDesembolso = fecha;
                            }

                            // Capital (saldo)
                            if (cuotaJson.TryGetProperty("CAPITAL", out var capitalElement))
                                credito.SaldoActual = ObtenerDecimal(capitalElement);

                            // Estado
                            credito.Estado = "A"; // Activo (si está en la consulta es porque está vigente)

                            creditosDict[numeroCredito] = credito;
                        }

                        // Parsear cuota
                        var cuota = new CuotaERP();
                        
                        if (cuotaJson.TryGetProperty("CUOTA", out var numCuotaEl))
                        {
                            if (int.TryParse(numCuotaEl.GetString() ?? numCuotaEl.ToString(), out var nc))
                                cuota.NumeroCuota = nc;
                        }
                        
                        if (cuotaJson.TryGetProperty("VENCIMIENTO", out var vencEl))
                        {
                            if (DateTime.TryParse(vencEl.GetString(), out var venc))
                                cuota.FechaVencimiento = venc;
                        }
                        
                        if (cuotaJson.TryGetProperty("DIAS_MORA", out var moraEl))
                        {
                            if (int.TryParse(moraEl.GetString() ?? moraEl.ToString(), out var dm))
                                cuota.DiasMora = dm;
                        }
                        
                        // [LOG] Debug de valores clave para Cuotas Pendientes
                        if (cuota.NumeroCuota > 0)
                        {
                            //Log($"[XEO-DEBUG] Cuota {cuota.NumeroCuota} - Venc: {cuota.FechaVencimiento:dd/MM/yyyy} - Mora: {cuota.DiasMora}");
                        }
                        
                        if (cuotaJson.TryGetProperty("CAPITAL", out var capEl))
                            cuota.Capital = ObtenerDecimal(capEl);
                        
                        if (TryGetPropertyAny(cuotaJson, out var cuotaCapEl, "CUOTA CAPITAL", "CUOTA_CAPITAL"))
                            cuota.CuotaCapital = ObtenerDecimal(cuotaCapEl);
                        
                        if (TryGetPropertyAny(cuotaJson, out var intEl, "CUOTA INTERES", "CUOTA_INTERES"))
                            cuota.CuotaInteres = ObtenerDecimal(intEl);
                        
                        if (cuotaJson.TryGetProperty("MORA", out var moraValEl))
                            cuota.Mora = ObtenerDecimal(moraValEl);
                        
                        if (TryGetPropertyAny(cuotaJson, out var cargosEl, "CARGOS", "CARGOS_CUOTA"))
                            cuota.Cargos = ObtenerDecimal(cargosEl);
                        
                        if (TryGetPropertyAny(cuotaJson, out var valorCuotaEl, "VALOR CUOTA", "VALOR_CUOTA"))
                            cuota.ValorCuota = ObtenerDecimal(valorCuotaEl);
                        
                        if (cuotaJson.TryGetProperty("ESTADO", out var estadoEl))
                            cuota.Estado = estadoEl.GetString() ?? "";
                            
                        // [LOG] Debug de ESTADO para ver por qué no la toma como pendiente
                        if (root.GetArrayLength() < 50) // Solo loguear si no son demasiadas (evitar spam extremo)
                        {
                           Log($"[XEO-DEBUG] Cuota {cuota.NumeroCuota} ({numeroCredito}): ESTADO='{cuota.Estado}', Pendiente? {cuota.EstaPendiente}");
                        }
                        
                        cuota.FechaAbono = ObtenerFechaAny(cuotaJson, "F. ABONO", "FECHA_PAGO");

                        // Solo agregar cuotas pendientes
                        if (cuota.EstaPendiente)
                        {
                            creditosDict[numeroCredito].CuotasPendientes.Add(cuota);
                        }
                        // Agregar TODAS las cuotas pagadas (para buscar por fecha de consignación después)
                        else if (cuota.EstaPagada && cuota.FechaAbono.HasValue)
                        {
                            creditosDict[numeroCredito].CuotasPagadasHoy.Add(cuota);
                        }
                    }

                    cliente.Creditos = creditosDict.Values.ToList();
                    
                    // [LOG] Resumen de créditos parseados
                    foreach(var cred in cliente.Creditos)
                    {
                        Log($"[XEO-RESUMEN] Crédito {cred.NumeroCredito}: {cred.CuotasPendientes.Count} cuotas pendientes. Prox Cuota: {cred.ProximaCuota?.NumeroCuota} (Venc: {cred.ProximaCuota?.FechaVencimiento:dd/MM/yyyy})");
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parseando JSON del ERP: {ex.Message}");
            }

            return cliente;
        }

        /// <summary>
        /// Helper para obtener un decimal de un JsonElement
        /// </summary>
        private decimal ObtenerDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetDecimal();
            if (element.ValueKind == JsonValueKind.String && 
                decimal.TryParse(element.GetString(), out var val))
                return val;
            return 0;
        }

        private bool TryGetPropertyAny(JsonElement element, out JsonElement value, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out value))
                    return true;
            }

            value = default;
            return false;
        }

        private DateTime? ObtenerFechaAny(JsonElement element, params string[] propertyNames)
        {
            if (!TryGetPropertyAny(element, out var fechaEl, propertyNames))
                return null;

            var fechaStr = fechaEl.GetString() ?? fechaEl.ToString();
            if (DateTime.TryParse(fechaStr, out var fecha))
                return fecha;

            return null;
        }

        public async Task<ResultadoValidacionERP> ValidarPagoAsync(string cedula)
        {
            var resultado = new ResultadoValidacionERP
            {
                Cedula = cedula
            };

            try
            {
                var cliente = await ConsultarClientePorCedulaAsync(cedula);

                if (cliente == null)
                {
                    // Si el servicio no está disponible
                    resultado.Estado = EstadoValidacionERP.ErrorConexion;
                    resultado.Mensaje = "No se pudo conectar con el ERP para validar.";
                    resultado.ClienteExiste = false;
                    return resultado;
                }

                resultado.Cliente = cliente;
                resultado.CantidadCreditos = cliente.CantidadCreditos;

                if (cliente.CantidadCreditos == 0)
                {
                    // La API retornó vacío = cliente no existe o sin créditos activos
                    resultado.Estado = EstadoValidacionERP.ClienteNoExiste;
                    resultado.ClienteExiste = false;
                    resultado.Mensaje = $"La cédula {cedula} no tiene créditos activos en el ERP.";
                }
                else if (cliente.CantidadCreditos == 1)
                {
                    resultado.Estado = EstadoValidacionERP.Valido;
                    resultado.ClienteExiste = true;
                    resultado.CreditoUnico = cliente.Creditos[0].NumeroCredito;
                    resultado.NumerosCreditos.Add(resultado.CreditoUnico);
                    resultado.Mensaje = $"Cliente valido - Credito: {resultado.CreditoUnico}";
                }
                else
                {
                    resultado.Estado = EstadoValidacionERP.MultiplesCreditos;
                    resultado.ClienteExiste = true;
                    resultado.NumerosCreditos = cliente.Creditos.Select(c => c.NumeroCredito).ToList();
                    resultado.Mensaje = $"El cliente tiene {cliente.CantidadCreditos} creditos activos: " +
                                       string.Join(", ", resultado.NumerosCreditos) +
                                       " - Requiere intervencion manual.";
                }
            }
            catch (Exception ex)
            {
                resultado.Estado = EstadoValidacionERP.ErrorConsulta;
                resultado.Mensaje = $"Error al validar: {ex.Message}";
            }

            return resultado;
        }

        public async Task<Dictionary<string, ResultadoValidacionERP>> ValidarPagosEnLoteAsync(IEnumerable<string> cedulas)
        {
            var resultados = new Dictionary<string, ResultadoValidacionERP>();
            var cedulasUnicas = cedulas.Distinct().ToList();

            // Procesar en paralelo con límite de concurrencia para no saturar la API
            var semaphore = new System.Threading.SemaphoreSlim(3); // Máximo 3 consultas simultáneas
            var tasks = cedulasUnicas.Select(async cedula =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var resultado = await ValidarPagoAsync(cedula);
                    lock (resultados)
                    {
                        resultados[cedula] = resultado;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return resultados;
        }

        public async Task<bool> VerificarConexionAsync()
        {
            try
            {
                // Hacer una consulta con una cédula de prueba para verificar conectividad
                var endpoint = await ResolverEndpointPlanPagosAsync();
                var url = $"{_baseUrl}/{endpoint}?token={_token}&cedula=0";
                var response = await _httpClient.GetAsync(url);
                
                // Si responde (aunque sea vacío o error 404) significa que hay conexión
                return response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable &&
                       response.StatusCode != System.Net.HttpStatusCode.GatewayTimeout;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Consulta el desglose de pagos aplicados para un cliente.
        /// Devuelve TODAS las cuotas con estado PAGADA (sin filtrar por fecha, 
        /// porque FECHA_PAGO del ERP puede diferir varios días de la consignación bancaria).
        /// </summary>
        public async Task<List<DesglosePagoXEO>> ConsultarDesglosePagosAsync(string cedula, DateTime fecha)
        {
            var desgloses = new List<DesglosePagoXEO>();
            
            if (string.IsNullOrWhiteSpace(cedula))
                return desgloses;

            cedula = cedula.Trim();

            try
            {
                var endpoint = await ResolverEndpointPlanPagosAsync();
                var url = $"{_baseUrl}/{endpoint}?token={_token}&cedula={cedula}";
                System.Diagnostics.Debug.WriteLine($"[DESGLOSE] Consultando {cedula}");
                
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[DESGLOSE] Error HTTP {(int)response.StatusCode} para {cedula}");
                    return desgloses;
                }

                var json = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                {
                    Log($"[DESGLOSE] Respuesta no es array JSON para {cedula}");
                    return desgloses;
                }

                foreach (var cuotaJson in root.EnumerateArray())
                {
                    string? estado = null;
                    if (cuotaJson.TryGetProperty("ESTADO", out var estadoEl))
                        estado = estadoEl.GetString();
                    
                    // Incluir cuotas PAGADAS y también PENDIENTES con pago parcial
                    bool esPagada = estado?.ToUpper() == "PAGADA";
                    bool tienePagoParcial = false;
                    if (!esPagada)
                    {
                        // Verificar si tiene algún abono parcial (cuota pendiente pero con pago)
                        if (TryGetPropertyAny(cuotaJson, out var capPagoCheck, "CUO_CAP_PAGO"))
                            tienePagoParcial = ObtenerDecimal(capPagoCheck) > 0;
                        if (!tienePagoParcial && TryGetPropertyAny(cuotaJson, out var intPagoCheck, "CUO_INT_PAGO"))
                            tienePagoParcial = ObtenerDecimal(intPagoCheck) > 0;
                        if (!tienePagoParcial && TryGetPropertyAny(cuotaJson, out var carPagoCheck, "CAR_CUO_PAGO"))
                            tienePagoParcial = ObtenerDecimal(carPagoCheck) > 0;
                        if (!tienePagoParcial && TryGetPropertyAny(cuotaJson, out var moraPagoCheck, "ABONO_MORA"))
                            tienePagoParcial = ObtenerDecimal(moraPagoCheck) > 0;
                    }
                    
                    if (!esPagada && !tienePagoParcial)
                        continue;

                    var fechaAbono = ObtenerFechaAny(cuotaJson, "F. ABONO", "FECHA_PAGO");
                    
                    var desglose = new DesglosePagoXEO
                    {
                        Cedula = cedula,
                        FechaAbono = fechaAbono ?? DateTime.MinValue
                    };
                    
                    if (cuotaJson.TryGetProperty("CREDITO", out var creditoEl))
                        desglose.NumeroCredito = creditoEl.GetString() ?? "";
                    
                    if (cuotaJson.TryGetProperty("CUOTA", out var cuotaEl))
                    {
                        if (int.TryParse(cuotaEl.GetString() ?? cuotaEl.ToString(), out var nc))
                            desglose.NumeroCuota = nc;
                    }
                    
                    // Preferir campos de pago real (CUO_CAP_PAGO) sobre los planificados (CUOTA_CAPITAL)
                    if (TryGetPropertyAny(cuotaJson, out var capPagoEl, "CUO_CAP_PAGO"))
                    {
                        var capPago = ObtenerDecimal(capPagoEl);
                        if (capPago > 0) desglose.Capital = capPago;
                    }
                    if (desglose.Capital == 0 && TryGetPropertyAny(cuotaJson, out var capEl, "CUOTA CAPITAL", "CUOTA_CAPITAL"))
                        desglose.Capital = ObtenerDecimal(capEl);
                    
                    if (TryGetPropertyAny(cuotaJson, out var intPagoEl, "CUO_INT_PAGO"))
                    {
                        var intPago = ObtenerDecimal(intPagoEl);
                        if (intPago > 0) desglose.Intereses = intPago;
                    }
                    if (desglose.Intereses == 0 && TryGetPropertyAny(cuotaJson, out var intEl, "CUOTA INTERES", "CUOTA_INTERES"))
                        desglose.Intereses = ObtenerDecimal(intEl);
                    
                    if (TryGetPropertyAny(cuotaJson, out var moraPagoEl, "ABONO_MORA"))
                    {
                        var moraPago = ObtenerDecimal(moraPagoEl);
                        if (moraPago > 0) desglose.Mora = moraPago;
                    }
                    if (desglose.Mora == 0 && cuotaJson.TryGetProperty("MORA", out var moraEl))
                        desglose.Mora = ObtenerDecimal(moraEl);
                    
                    if (TryGetPropertyAny(cuotaJson, out var carPagoEl, "CAR_CUO_PAGO"))
                    {
                        var carPago = ObtenerDecimal(carPagoEl);
                        if (carPago > 0) desglose.Cargos = carPago;
                    }
                    if (desglose.Cargos == 0 && TryGetPropertyAny(cuotaJson, out var cargosEl, "CARGOS", "CARGOS_CUOTA"))
                        desglose.Cargos = ObtenerDecimal(cargosEl);
                    
                    desglose.TotalAplicado = desglose.Capital + desglose.Intereses + desglose.Mora + desglose.Cargos;
                    
                    if (TryGetPropertyAny(cuotaJson, out var valorCuotaEl, "VALOR CUOTA", "VALOR_CUOTA"))
                    {
                        var valorCuota = ObtenerDecimal(valorCuotaEl);
                        if (valorCuota > desglose.TotalAplicado)
                            desglose.TotalAplicado = valorCuota;
                    }
                    
                    desgloses.Add(desglose);
                }
                
                if (desgloses.Count > 0)
                    Log($"[DESGLOSE] ✓ {cedula}: {desgloses.Count} cuotas PAGADAS");
            }
            catch (TaskCanceledException) when (!string.IsNullOrWhiteSpace(cedula))
            {
                try
                {
                    await Task.Delay(2000);
                    var endpoint2 = await ResolverEndpointPlanPagosAsync();
                    var url2 = $"{_baseUrl}/{endpoint2}?token={_token}&cedula={cedula}";
                    var response2 = await _httpClient.GetAsync(url2);
                    if (response2.IsSuccessStatusCode)
                    {
                        var json2 = await response2.Content.ReadAsStringAsync();
                        using var doc2 = JsonDocument.Parse(json2);
                        foreach (var c in doc2.RootElement.EnumerateArray())
                        {
                            string? est = null;
                            if (c.TryGetProperty("ESTADO", out var eEl)) est = eEl.GetString();
                            bool esPag = est?.ToUpper() == "PAGADA";
                            bool parcial = false;
                            if (!esPag)
                            {
                                if (TryGetPropertyAny(c, out var chk, "CUO_CAP_PAGO")) parcial = ObtenerDecimal(chk) > 0;
                                if (!parcial && TryGetPropertyAny(c, out var chk2, "CUO_INT_PAGO")) parcial = ObtenerDecimal(chk2) > 0;
                                if (!parcial && TryGetPropertyAny(c, out var chk3, "CAR_CUO_PAGO")) parcial = ObtenerDecimal(chk3) > 0;
                                if (!parcial && TryGetPropertyAny(c, out var chk4, "ABONO_MORA")) parcial = ObtenerDecimal(chk4) > 0;
                            }
                            if (!esPag && !parcial) continue;
                            var fa = ObtenerFechaAny(c, "F. ABONO", "FECHA_PAGO");
                            var d = new DesglosePagoXEO { Cedula = cedula, FechaAbono = fa ?? DateTime.MinValue };
                            if (c.TryGetProperty("CREDITO", out var crEl)) d.NumeroCredito = crEl.GetString() ?? "";
                            if (TryGetPropertyAny(c, out var ck, "CUO_CAP_PAGO", "CUOTA CAPITAL", "CUOTA_CAPITAL")) d.Capital = ObtenerDecimal(ck);
                            if (TryGetPropertyAny(c, out var ci, "CUO_INT_PAGO", "CUOTA INTERES", "CUOTA_INTERES")) d.Intereses = ObtenerDecimal(ci);
                            if (TryGetPropertyAny(c, out var cm, "ABONO_MORA", "MORA")) d.Mora = ObtenerDecimal(cm);
                            if (TryGetPropertyAny(c, out var cc, "CAR_CUO_PAGO", "CARGOS", "CARGOS_CUOTA")) d.Cargos = ObtenerDecimal(cc);
                            d.TotalAplicado = d.Capital + d.Intereses + d.Mora + d.Cargos;
                            if (TryGetPropertyAny(c, out var cv, "VALOR CUOTA", "VALOR_CUOTA"))
                            { var vc = ObtenerDecimal(cv); if (vc > d.TotalAplicado) d.TotalAplicado = vc; }
                            desgloses.Add(d);
                        }
                        if (desgloses.Count > 0)
                            Log($"[DESGLOSE] ✓ {cedula}: {desgloses.Count} cuotas (retry OK)");
                    }
                }
                catch
                {
                    Log($"[DESGLOSE] ✗ {cedula}: timeout (2 intentos)");
                }
            }
            catch (Exception ex)
            {
                Log($"[DESGLOSE] EXCEPCION {cedula}: {ex.Message}");
            }

            return desgloses;
        }
        
        /// <summary>
        /// Consulta el desglose de pagos aplicados para múltiples clientes
        /// </summary>
        public async Task<Dictionary<string, List<DesglosePagoXEO>>> ConsultarDesglosePagosEnLoteAsync(IEnumerable<string> cedulas, DateTime fecha)
        {
            var resultados = new Dictionary<string, List<DesglosePagoXEO>>();
            var cedulasUnicas = cedulas.Distinct().ToList();

            // Procesar en paralelo con límite de concurrencia
            var semaphore = new System.Threading.SemaphoreSlim(5); // 5 consultas simultáneas
            var tasks = cedulasUnicas.Select(async cedula =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var desgloses = await ConsultarDesglosePagosAsync(cedula, fecha);
                    lock (resultados)
                    {
                        resultados[cedula] = desgloses;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return resultados;
        }
        
        /// <summary>
        /// Consulta el desglose de movimientos por número de comprobante (CC)
        /// Usa el endpoint /desglose_movimientos
        /// </summary>
        public async Task<DesgloseMovimientoXEO?> ConsultarDesgloseMovimientosAsync(string numeroComprobante)
        {
            if (string.IsNullOrWhiteSpace(numeroComprobante))
                return null;

            numeroComprobante = numeroComprobante.Trim();

            try
            {
                var endpoint = await ResolverEndpointDesgloseMovimientosAsync();
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    // Evitar ruido de 404 y saturación cuando la ruta no existe en el servidor.
                    return null;
                }

                var url = $"{_baseUrl}/{endpoint}?token={_token}&num_m={numeroComprobante}";
                Log($"[DESGLOSE] Consultando: {url}");
                
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    Log($"[DESGLOSE] Error HTTP: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                Log($"[DESGLOSE] Respuesta CC={numeroComprobante}: {json}");
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Verificar si hay error
                if (root.TryGetProperty("error", out var errorEl))
                {
                    Log($"[DESGLOSE] Sin datos para comprobante {numeroComprobante}: {errorEl.GetString()}");
                    return null;
                }

                var desglose = new DesgloseMovimientoXEO
                {
                    NumeroComprobante = numeroComprobante
                };

                if (root.TryGetProperty("fecha_primera", out var fechaEl))
                {
                    var fechaStr = fechaEl.GetString();
                    if (!string.IsNullOrEmpty(fechaStr) && DateTime.TryParse(fechaStr, out var fecha))
                        desglose.FechaPrimera = fecha;
                }

                if (root.TryGetProperty("claves_credito", out var clavesEl))
                    desglose.ClavesCredito = clavesEl.GetString() ?? "";

                if (root.TryGetProperty("suma_K", out var sumaKEl))
                    desglose.SumaK = ObtenerDecimal(sumaKEl);

                if (root.TryGetProperty("suma_I", out var sumaIEl))
                    desglose.SumaI = ObtenerDecimal(sumaIEl);

                if (root.TryGetProperty("suma_O", out var sumaOEl))
                    desglose.SumaO = ObtenerDecimal(sumaOEl);

                if (root.TryGetProperty("suma_M", out var sumaMEl))
                    desglose.SumaM = ObtenerDecimal(sumaMEl);

                if (root.TryGetProperty("total_CC", out var totalCCEl))
                    desglose.TotalCC = ObtenerDecimal(totalCCEl);

                if (root.TryGetProperty("total_CO", out var totalCOEl))
                    desglose.TotalCO = ObtenerDecimal(totalCOEl);
                
                Log($"[DESGLOSE] Parseado: K={desglose.SumaK}, I={desglose.SumaI}, M={desglose.SumaM}, Total={desglose.TotalCC}");

                return desglose;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error consultando desglose movimientos: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Consulta el desglose de movimientos para múltiples comprobantes
        /// </summary>
        public async Task<Dictionary<string, DesgloseMovimientoXEO?>> ConsultarDesgloseMovimientosEnLoteAsync(IEnumerable<string> comprobantes)
        {
            var resultados = new Dictionary<string, DesgloseMovimientoXEO?>();
            var comprobantesUnicos = comprobantes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();

            // Procesar en paralelo con límite de concurrencia
            var semaphore = new System.Threading.SemaphoreSlim(5); // 5 consultas simultáneas
            var tasks = comprobantesUnicos.Select(async numM =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var desglose = await ConsultarDesgloseMovimientosAsync(numM);
                    lock (resultados)
                    {
                        resultados[numM] = desglose;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return resultados;
        }
        
        /// <summary>
        /// Obtiene los nombres de terceros (deudores) consultando el endpoint /nombre_cliente
        /// que consulta directamente la tabla de clientes por cédula
        /// </summary>
        public async Task<Dictionary<string, string>> ObtenerNombresTercerosAsync(IEnumerable<string> cedulas, DateTime? fechaDesembolso = null)
        {
            var nombres = new Dictionary<string, string>();
            var cedulasList = cedulas.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            
            if (cedulasList.Count == 0)
                return nombres;
            
            Log($"[XEO] ========== CONSULTANDO NOMBRES ==========");
            Log($"[XEO] Cédulas a consultar: {cedulasList.Count}");

            int encontrados = 0;
            int errores = 0;
            int sinNombre = 0;

            // Consultar en paralelo con límite de concurrencia
            var semaphore = new System.Threading.SemaphoreSlim(5);
            var lockObj = new object();
            var tasks = cedulasList.Select(async cedula =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var url = $"{_baseUrl}/nombre_cliente?token={_token}&cedula={cedula}";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        // Buscar el campo NOMBRE en la respuesta
                        if (root.TryGetProperty("NOMBRE", out var nombreEl) &&
                            nombreEl.ValueKind == JsonValueKind.String)
                        {
                            var nombre = nombreEl.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(nombre))
                            {
                                lock (lockObj) { nombres[cedula] = nombre; encontrados++; }
                            }
                            else
                            {
                                lock (lockObj) { sinNombre++; }
                            }
                        }
                        else if (root.TryGetProperty("error", out var errorEl))
                        {
                            lock (lockObj) { errores++; }
                            Log($"[XEO] Error del servidor para {cedula}: {errorEl.GetString()}");
                        }
                        else
                        {
                            lock (lockObj) { sinNombre++; }
                        }
                    }
                    else
                    {
                        lock (lockObj) { errores++; }
                        Log($"[XEO] HTTP {(int)response.StatusCode} para nombre de {cedula}");
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj) { errores++; }
                    Log($"[XEO] Error consultando nombre para {cedula}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            
            Log($"[XEO] ========== RESULTADO NOMBRES ==========");
            Log($"[XEO] Encontrados: {encontrados}, Sin nombre en BD: {sinNombre}, Errores: {errores}");
            Log($"[XEO] Total consultados: {cedulasList.Count}");
            
            return nombres;
        }

        /// <summary>
        /// Consulta los movimientos CC aplicados en el ERP desde una fecha dada.
        /// Endpoint: /movimientos_cc_por_fecha?token=X&fecha=YYYY-MM-DD
        /// Retorna: [{MOVIMIENTO, FECHA, TOTAL, CEDULA}, ...]
        /// </summary>
        public async Task<List<MovimientoCC>> ConsultarMovimientosCCPorFechaAsync(DateTime fecha)
        {
            var fechaStr = fecha.ToString("yyyy-MM-dd");

            var resultado = await ConReintentosAsync(async () =>
            {
                var movimientos = new List<MovimientoCC>();
                var url = $"{_baseUrl}/movimientos_cc_por_fecha?token={_token}&fecha={fechaStr}";
                Log($"[CC] Consultando movimientos CC desde {fechaStr}: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[CC] Error HTTP {(int)response.StatusCode} al consultar movimientos CC");
                    return movimientos;
                }

                var json = await response.Content.ReadAsStringAsync();
                Log($"[CC] Respuesta (len={json.Length}): {json.Substring(0, Math.Min(json.Length, 300))}...");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                {
                    Log($"[CC] Respuesta no es array JSON");
                    return movimientos;
                }

                foreach (var item in root.EnumerateArray())
                {
                    var mov = new MovimientoCC();

                    if (item.TryGetProperty("MOVIMIENTO", out var movEl))
                        mov.Movimiento = movEl.ToString();

                    if (item.TryGetProperty("FECHA", out var fechaEl))
                    {
                        var fechaMovStr = fechaEl.GetString() ?? fechaEl.ToString();
                        if (DateTime.TryParse(fechaMovStr, out var fechaMov))
                            mov.Fecha = fechaMov;
                    }

                    if (item.TryGetProperty("TOTAL", out var totalEl))
                        mov.Total = ObtenerDecimal(totalEl);

                    if (item.TryGetProperty("CEDULA", out var cedulaEl))
                        mov.Cedula = cedulaEl.ToString().Trim();

                    if (!string.IsNullOrEmpty(mov.Movimiento) && !string.IsNullOrEmpty(mov.Cedula))
                        movimientos.Add(mov);
                }

                Log($"[CC] Total movimientos CC encontrados desde {fechaStr}: {movimientos.Count}");
                return movimientos;
            }, $"Movimientos CC {fechaStr}");

            return resultado ?? new List<MovimientoCC>();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
