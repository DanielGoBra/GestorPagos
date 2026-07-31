using GestorPagos.Core.Entities;
using GestorPagos.Core.Models;
using GestorPagos.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GestorPagos.Data.Services
{
    public class GestorDeProcesosService : IGestorDeProcesosService
    {
        private readonly IErpService _erpService;
        private readonly IPagoService _pagoService;
        private readonly IExcelExportService _excelExportService;

        public GestorDeProcesosService(
            IErpService erpService,
            IPagoService pagoService,
            IExcelExportService excelExportService)
        {
            _erpService = erpService;
            _pagoService = pagoService;
            _excelExportService = excelExportService;
        }

        public async Task<ResumenDesglose> ProcesarDesgloseAsync(
            IEnumerable<Pago> pagos, 
            string usuario, 
            DateTime fechaActual,
            IProgress<string> logger, 
            CancellationToken cancellationToken = default)
        {
            var resumen = new ResumenDesglose();
            var pagosList = pagos.ToList(); // Materialize to avoid multiple enumeration
            resumen.TotalConsultados = pagosList.Count;

            // 1. Agrupar por Cédula para minimizar llamadas a la API
            // La fecha ya no se usa como clave porque FECHA_PAGO del ERP puede diferir
            // varios días de la fecha de consignación bancaria.
            var gruposPorCedula = pagosList
                .Select(p => new { 
                    Pago = p, 
                    Cedula = !string.IsNullOrWhiteSpace(p.CedulaCorrecta) ? p.CedulaCorrecta : 
                             (!string.IsNullOrWhiteSpace(p.CedulaCliente) ? p.CedulaCliente : 
                             (p.CC != null && p.CC.Length > 5 && p.CC.All(char.IsDigit) ? p.CC : "")) 
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Cedula))
                .GroupBy(x => x.Cedula, x => x.Pago)
                .ToList();

            if (gruposPorCedula.Count == 0)
            {
                logger?.Report("No se encontraron pagos con cédula válida para consultar.");
                resumen.MensajeFinal = "No se encontraron pagos válidos (sin cédula).";
                return resumen;
            }

            logger?.Report($"Consultando desglose para {gruposPorCedula.Count} cédulas únicas...");

            var desglosesPorCedulaFecha = new Dictionary<string, List<DesglosePagoXEO>>();
            int exito = 0;
            int fallos = 0;

            // 2. Ejecutar consultas en paralelo (limitado) o secuencial
            // Para simplicidad y control, usamos secuencial o Task.Run limitado
            var tasks = new List<Task>();
            using (var semaphore = new SemaphoreSlim(2)) // Limite de 2 simultáneos para no saturar la API
            {
                foreach (var grupo in gruposPorCedula)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            var detalles = await _erpService.ConsultarDesglosePagosAsync(grupo.Key, DateTime.Today);
                            
                            if (detalles != null && detalles.Count > 0)
                            {
                                var clave = grupo.Key;
                                lock (desglosesPorCedulaFecha)
                                {
                                    desglosesPorCedulaFecha[clave] = detalles;
                                }
                                Interlocked.Increment(ref exito);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref fallos);
                            System.Diagnostics.Debug.WriteLine($"Error consultando {grupo.Key}: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks);
            }

            logger?.Report($"Consulta API completada. Procesando resultados ({exito} exitosos, {fallos} fallidos)...");
            logger?.Report($"[DIAG] Claves en diccionario: {desglosesPorCedulaFecha.Count}. Ejemplo: {string.Join(", ", desglosesPorCedulaFecha.Keys.Take(3))}");

            // 3. Asignar resultados y GUARDAR EN CALIENTE cada pago con desglose.
            // Esto evita perder avance si el proceso se interrumpe.
            var equipo = Environment.MachineName;
            int guardados = 0;
            int sinCedula = 0;
            int sinClave = 0;
            int sinMatch = 0;
            
            foreach (var pago in pagosList)
            {
                // Fallback para coincidir con la clave
                var cedulaReal = !string.IsNullOrWhiteSpace(pago.CedulaCorrecta) ? pago.CedulaCorrecta : 
                                 (!string.IsNullOrWhiteSpace(pago.CedulaCliente) ? pago.CedulaCliente : 
                                 (pago.CC != null && pago.CC.Length > 5 && pago.CC.All(char.IsDigit) ? pago.CC : ""));
                
                if (string.IsNullOrWhiteSpace(cedulaReal))
                {
                    sinCedula++;
                    resumen.SinDesglose++;
                    continue;
                }
                
                var clave = cedulaReal;
                if (!desglosesPorCedulaFecha.TryGetValue(clave, out var detallesDelDia) || detallesDelDia.Count == 0)
                {
                    sinClave++;
                    resumen.SinDesglose++;
                    continue;
                }
                
                // Lógica de Matching
                DesglosePagoXEO mejorMatch = null;
                
                // Prioridad 1: Coincidencia por Crédito
                if (!string.IsNullOrWhiteSpace(pago.Credito))
                {
                    var cuotasDelCredito = detallesDelDia.Where(d => d.NumeroCredito == pago.Credito).ToList();
                    if (cuotasDelCredito.Count > 0)
                    {
                         mejorMatch = CrearDesgloseAgrupado(cedulaReal, pago.Credito, cuotasDelCredito);
                    }
                }
                
                // Prioridad 2: Si no hay match por crédito, usar lógica general
                if (mejorMatch == null)
                {
                    var creditosUnicos = detallesDelDia.Select(d => d.NumeroCredito).Distinct().ToList();
                    if (creditosUnicos.Count == 1)
                    {
                        mejorMatch = CrearDesgloseAgrupado(cedulaReal, creditosUnicos[0], detallesDelDia);
                    }
                    else if (creditosUnicos.Count > 1)
                    {
                         // Múltiples créditos: se suman todos y se listan los créditos
                         mejorMatch = CrearDesgloseAgrupado(cedulaReal, string.Join("; ", creditosUnicos), detallesDelDia);
                    }
                }
                
                if (mejorMatch == null || !mejorMatch.TieneDatos)
                {
                    // FALLBACK: Si no hay desglose por plan de pagos, intentar por movimiento CC
                    if (!string.IsNullOrEmpty(pago.CC))
                    {
                        var desgloseMov = await _erpService.ConsultarDesgloseMovimientosAsync(pago.CC);
                        if (desgloseMov != null)
                        {
                            pago.Capital = desgloseMov.SumaK;
                            pago.Intereses = desgloseMov.SumaI;
                            pago.Mora = desgloseMov.SumaM;
                            pago.Cargos = desgloseMov.SumaO;
                            pago.TotalAplicado = desgloseMov.TotalCC;
                            pago.Condonacion = 0;

                            if (!string.IsNullOrEmpty(desgloseMov.ClavesCredito))
                                pago.Credito = desgloseMov.ClavesCredito;

                            pago.UsuarioDesglose = usuario;
                            pago.FechaDesglose = fechaActual;
                            resumen.DesglosesEncontrados++;

                            if (pago.TieneDatosAplicacion && pago.EstaCuadrado)
                            {
                                resumen.PagosCuadrados++;
                                pago.Bloqueado = true;
                                pago.FechaBloqueo = fechaActual;
                                pago.UsuarioBloqueo = usuario;
                                resumen.PagosBloqueados++;
                            }

                            try
                            {
                                var descripcion = pago.Bloqueado ? "Desglose (Bloqueado)" : "Desglose";
                                if (await _pagoService.ActualizarPagoAsync(pago, usuario, equipo, descripcion))
                                {
                                    guardados++;
                                    resumen.Actualizados = guardados;
                                    logger?.Report($"Desglose guardado: Pago {pago.IdCorto} | CC {pago.CC} | TotalAplicado {pago.TotalAplicado:N0}" +
                                                   (pago.Bloqueado ? " | BLOQUEADO" : ""));
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.Report($"Error guardando pago {pago.IdCorto}: {ex.Message}");
                            }
                        }
                        else
                        {
                            resumen.SinDesglose++;
                        }
                    }
                    else
                    {
                        resumen.SinDesglose++;
                    }
                    continue;
                }
                
                // Actualizar Pago
                pago.Capital = mejorMatch.Capital;
                pago.Intereses = mejorMatch.Intereses;
                pago.Cargos = mejorMatch.Cargos;
                pago.Mora = mejorMatch.Mora;
                pago.TotalAplicado = mejorMatch.TotalAplicado;
                pago.Condonacion = 0;
                
                if (string.IsNullOrEmpty(pago.Credito) && !string.IsNullOrEmpty(mejorMatch.NumeroCredito))
                {
                    pago.Credito = mejorMatch.NumeroCredito;
                }
                
                pago.UsuarioDesglose = usuario;
                pago.FechaDesglose = fechaActual;
                
                resumen.DesglosesEncontrados++;
                
                if (pago.TieneDatosAplicacion && pago.EstaCuadrado)
                {
                    resumen.PagosCuadrados++;
                    pago.Bloqueado = true;
                    pago.FechaBloqueo = fechaActual;
                    pago.UsuarioBloqueo = usuario;
                    resumen.PagosBloqueados++;
                }

                try
                {
                    var descripcion = pago.Bloqueado ? "Desglose (Bloqueado)" : "Desglose";
                    if (await _pagoService.ActualizarPagoAsync(pago, usuario, equipo, descripcion))
                    {
                        guardados++;
                        resumen.Actualizados = guardados;
                        logger?.Report($"Desglose guardado: Pago {pago.IdCorto} | CC {pago.CC} | TotalAplicado {pago.TotalAplicado:N0}" +
                                       (pago.Bloqueado ? " | BLOQUEADO" : ""));
                    }
                }
                catch (Exception ex)
                {
                    logger?.Report($"Error guardando pago {pago.IdCorto}: {ex.Message}");
                }
            }

            logger?.Report($"Guardado incremental completado. Registros guardados: {resumen.Actualizados}.");
            logger?.Report($"[DIAG] sinCedula={sinCedula}, sinClave={sinClave}, desgloseEncontrados={resumen.DesglosesEncontrados}, guardados={guardados}");
            
            // Log primeras claves buscadas vs disponibles para depuración
            if (sinClave > 0 && desglosesPorCedulaFecha.Count > 0)
            {
                var clavesEjemplo = pagosList.Take(5).Select(p => {
                    var ced = !string.IsNullOrWhiteSpace(p.CedulaCorrecta) ? p.CedulaCorrecta :
                              (!string.IsNullOrWhiteSpace(p.CedulaCliente) ? p.CedulaCliente : "?");
                    return ced;
                });
                logger?.Report($"[DIAG] Claves buscadas (primeras 5): {string.Join(", ", clavesEjemplo)}");
                logger?.Report($"[DIAG] Claves disponibles (primeras 5): {string.Join(", ", desglosesPorCedulaFecha.Keys.Take(5))}");
            }
            
            resumen.MensajeFinal = $"Consulta completada. {resumen.DesglosesEncontrados} desgloses encontrados, {resumen.PagosBloqueados} pagos bloqueados.";
            return resumen;
        }

        public async Task<ResumenValidacion> ValidarPagosAsync(
            IEnumerable<Pago> pagos,
            string usuario,
            string equipo,
            IProgress<string> logger,
            CancellationToken cancellationToken = default)
        {
            var resumen = new ResumenValidacion();
            var pagosList = pagos.ToList();
            if (pagosList.Count == 0) return resumen;

            logger?.Report("Preparando validación contra ERP...");

            // Limpiar cache para asegurar datos recientes
            _erpService.LimpiarCache();

            // Verificar conexión
            if (!await _erpService.VerificarConexionAsync())
            {
                logger?.Report("Error: No hay conexión con el ERP.");
                resumen.MensajeFinal = "No hay conexión con el ERP.";
                return resumen;
            }

            // 1. Detectar Pagos Dobles
            var cedulasConteo = pagosList.GroupBy(p => p.CedulaCliente)
                .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => g.Key)
                .ToHashSet();
            
            resumen.PagosDobles = cedulasConteo.Count;
            
            foreach(var p in pagosList)
            {
                p.EsPagoDoble = !string.IsNullOrEmpty(p.CedulaCliente) && cedulasConteo.Contains(p.CedulaCliente);
            }

            // 2. Llenar Cédula Correcta si falta
            var pagosSinCedulaCorrecta = pagosList
                .Where(p => string.IsNullOrWhiteSpace(p.CedulaCorrecta) && !string.IsNullOrWhiteSpace(p.CedulaCliente))
                .ToList();

            foreach (var p in pagosSinCedulaCorrecta)
            {
                p.CedulaCorrecta = p.CedulaCliente;
                if (!string.IsNullOrEmpty(p.CodigoPago))
                {
                    try { await _pagoService.ActualizarCedulaCorrectaAsync(p.Id, p.CedulaCorrecta); } catch {}
                }
            }

            // 3. Filtrar Pagos a Validar
            var pagosAValidar = pagosList.Where(p =>
                !p.Bloqueado &&
                p.Estado != EstadoPago.Aplicado &&
                !string.IsNullOrWhiteSpace(p.CedulaCliente)
            ).ToList();

            if (pagosAValidar.Count == 0)
            {
                resumen.MensajeFinal = "No hay pagos pendientes de validación.";
                return resumen;
            }

            // 4. Consultar en Lote
            var cedulasUnicas = pagosAValidar.Select(p => p.CedulaCliente).Distinct().ToList();
            logger?.Report($"Validando {cedulasUnicas.Count} cédulas únicas ({pagosAValidar.Count} pagos)...");
            resumen.PagosConsultados = cedulasUnicas.Count;

            var resultadosDic = await _erpService.ValidarPagosEnLoteAsync(cedulasUnicas);
            
            // 5. Procesar Resultados
            int procesados = 0;
            var pagosParaGuardar = new List<Pago>();

            foreach (var pago in pagosAValidar)
            {
                if (resultadosDic.TryGetValue(pago.CedulaCliente, out var resultado))
                {
                    // Asignar Titular
                    if (resultado.Cliente != null && !string.IsNullOrWhiteSpace(resultado.Cliente.NombreCompleto))
                    {
                        pago.Titular = resultado.Cliente.NombreCompleto;
                    }

                    switch (resultado.Estado)
                    {
                        case EstadoValidacionERP.Valido:
                            resumen.PagosValidos++;
                            resumen.ConCredito++;
                            pago.Estado = EstadoPago.Validado;
                            pago.XEO_Error = "Sin Novedad";

                            if (resultado.Cliente?.Creditos?.Any() == true)
                            {
                                var credito = resultado.Cliente.Creditos.First();
                                var cuotaProxima = credito.ProximaCuota;
                                if (cuotaProxima != null)
                                {
                                    pago.ValorCuota = cuotaProxima.ValorCuota;
                                    pago.CuotaActual = cuotaProxima.NumeroCuota;
                                    pago.DiasMora = cuotaProxima.DiasMora;
                                    pago.FechaProximaCuota = cuotaProxima.FechaVencimiento.ToString("dd/MM/yyyy");
                                    
                                    if (pago.DiasMora > 30) resumen.PagosMoraAlta++;
                                }
                                pago.CuotasPendientes = credito.CuotasPendientes?.Count;
                                pago.Credito = resultado.CreditoUnico;
                            }
                            break;

                        case EstadoValidacionERP.MultiplesCreditos:
                            resumen.MultiplesCreditos++;
                            pago.Estado = EstadoPago.ConError;
                            pago.XEO_Error = "Múltiples créditos";
                            pago.XEO_Observacion = $"{resultado.CantidadCreditos} créditos - Requiere gestión manual";
                            if (resultado.NumerosCreditos?.Any() == true)
                            {
                                pago.ListaCreditos = string.Join(", ", resultado.NumerosCreditos);
                            }
                            break;

                        case EstadoValidacionERP.ClienteNoExiste:
                            resumen.NoEncontrados++;
                            pago.XEO_Error = resultado.Mensaje;
                            break;
                            
                        case EstadoValidacionERP.SinCreditos:
                            resumen.SinCreditos++;
                            pago.XEO_Error = resultado.Mensaje;
                            break;

                        default:
                            resumen.Errores++;
                            pago.XEO_Error = resultado.Mensaje ?? "Error desconocido";
                            break;
                    }
                }
                else
                {
                    resumen.Errores++;
                    pago.XEO_Error = "No se obtuvo respuesta del ERP";
                }
                pagosParaGuardar.Add(pago);
                procesados++;
            }

            // 6. Guardar Cambios
            logger?.Report("Guardando resultados en base de datos...");
            foreach(var p in pagosParaGuardar)
            {
                try { await _pagoService.ActualizarPagoAsync(p, usuario, equipo, "Validación Auto"); } catch {}
            }

            resumen.MensajeFinal = $"Validación completada.\nValidos: {resumen.PagosValidos}\nMúltiples Créditos: {resumen.MultiplesCreditos}\nSin Créditos: {resumen.SinCreditos}\nNo Encontrados: {resumen.NoEncontrados}";
            
            return resumen;
        }
        public async Task<List<DesglosePagoXEO>> ConsultarPosibleDesgloseAsync(
            string cedula, 
            DateTime fecha, 
            CancellationToken cancellationToken = default)
        {
            // 1. Consultar cuotas PAGADAS por Cedula y Fecha
            var detalles = await _erpService.ConsultarDesglosePagosAsync(cedula, fecha);
            
            if (detalles == null || detalles.Count == 0) return new List<DesglosePagoXEO>();

            // 2. Agrupar detalles por credito
            var creditosUnicos = detalles.Select(d => d.NumeroCredito).Distinct().ToList();
            
            // 3. Preparar items a procesar (agrupados por credito)
            var itemsAProcesar = creditosUnicos.Select(credito =>
            {
                var cuotasDelCredito = detalles.Where(d => d.NumeroCredito == credito).ToList();
                return new DesglosePagoXEO
                {
                    Cedula = cedula,
                    NumeroCredito = credito,
                    Capital = cuotasDelCredito.Sum(c => c.Capital),
                    Intereses = cuotasDelCredito.Sum(c => c.Intereses),
                    Mora = cuotasDelCredito.Sum(c => c.Mora),
                    Cargos = cuotasDelCredito.Sum(c => c.Cargos),
                    TotalAplicado = cuotasDelCredito.Sum(c => c.TotalAplicado)
                };
            }).Where(item => item.TieneDatos).ToList();

            return itemsAProcesar;
        }

        public async Task<List<Pago>> AplicarDesgloseEnPagoAsync(
            Pago pagoOriginal, 
            List<DesglosePagoXEO> itemsAProcesar, 
            string usuario,
            string equipo,
            CancellationToken cancellationToken = default)
        {
            if (itemsAProcesar == null || itemsAProcesar.Count == 0) return new List<Pago>();
            
            var totalCC = itemsAProcesar.Sum(x => x.TotalAplicado);
            var nuevosPagos = new List<Pago>();

            // Ordenar items para consistencia (mayor valor primero)
            itemsAProcesar = itemsAProcesar.OrderByDescending(x => x.TotalAplicado).ToList();

            // CASO 1: Múltiples Créditos (DIVIDIR)
            if (itemsAProcesar.Count > 1)
            {
                // --- FILA 1 (Pago Original modificado) ---
                var item1 = itemsAProcesar[0];
                ActualizarPagoConDesglose(pagoOriginal, item1);
                
                // Modificar el Monto y Hash del original para reflejar solo SU parte
                pagoOriginal.Monto = item1.TotalAplicado; 
                if (pagoOriginal.HashUnico.Length > 13) pagoOriginal.HashUnico = pagoOriginal.HashUnico.Substring(0, 13);
                pagoOriginal.HashUnico += "-1";
                
                if (!string.IsNullOrEmpty(item1.NumeroCredito)) pagoOriginal.Credito = item1.NumeroCredito;
                
                // Registrar auditoria
                pagoOriginal.UsuarioDesglose = usuario;
                pagoOriginal.FechaDesglose = DateTime.UtcNow;
                pagoOriginal.Estado = EstadoPago.Aplicado;
                
                if (pagoOriginal.EstaCuadrado)
                {
                    pagoOriginal.Bloqueado = true;
                    pagoOriginal.FechaBloqueo = DateTime.UtcNow;
                    pagoOriginal.UsuarioBloqueo = usuario;
                }

                // --- FILAS ADICIONALES ---
                for (int i = 1; i < itemsAProcesar.Count; i++)
                {
                    var itemN = itemsAProcesar[i];
                    var nuevoPago = new Pago
                    {
                        Id = Guid.NewGuid(),
                        CodigoPago = pagoOriginal.CodigoPago,
                        CedulaCliente = pagoOriginal.CedulaCliente,
                        CedulaCorrecta = pagoOriginal.CedulaCorrecta,
                        FechaConsignacion = pagoOriginal.FechaConsignacion,
                        Convenio = pagoOriginal.Convenio,
                        NumeroAprobacion = pagoOriginal.NumeroAprobacion,
                        CC = pagoOriginal.CC,
                        Resultado = pagoOriginal.Resultado,
                        Estado = EstadoPago.Aplicado,
                        Monto = itemN.TotalAplicado,
                        UsuarioDesglose = usuario,
                        FechaDesglose = DateTime.UtcNow
                    };

                    var baseHash = pagoOriginal.HashUnico.Contains("-") ? pagoOriginal.HashUnico.Split('-')[0] : pagoOriginal.HashUnico;
                    nuevoPago.HashUnico = $"{baseHash}-{i + 1}";
                    
                    ActualizarPagoConDesglose(nuevoPago, itemN);
                    if (!string.IsNullOrEmpty(itemN.NumeroCredito)) nuevoPago.Credito = itemN.NumeroCredito;
                    
                    if (nuevoPago.EstaCuadrado)
                    {
                        nuevoPago.Bloqueado = true;
                        nuevoPago.FechaBloqueo = DateTime.UtcNow;
                        nuevoPago.UsuarioBloqueo = usuario;
                    }
                     
                    nuevosPagos.Add(nuevoPago);
                }
                
                // GUARDAR TODO EN BD
                if (_pagoService != null)
                {
                    await _pagoService.ActualizarPagoAsync(pagoOriginal, usuario, equipo, "División manual por múltiple crédito (Principal)");
                    await _pagoService.GuardarPagosAsync(nuevosPagos, usuario, equipo, "División manual por múltiple crédito (Parte)");
                }
            }
            else
            {
                // CASO 2: Un Solo Crédito (Simple)
                var item = itemsAProcesar.First();
                ActualizarPagoConDesglose(pagoOriginal, item);
                if (!string.IsNullOrEmpty(item.NumeroCredito)) pagoOriginal.Credito = item.NumeroCredito;
                
                pagoOriginal.UsuarioDesglose = usuario;
                pagoOriginal.FechaDesglose = DateTime.UtcNow;
                pagoOriginal.Estado = EstadoPago.Aplicado;
                
                if (pagoOriginal.EstaCuadrado)
                {
                    pagoOriginal.Bloqueado = true;
                    pagoOriginal.FechaBloqueo = DateTime.UtcNow;
                    pagoOriginal.UsuarioBloqueo = usuario;
                }

                if (_pagoService != null)
                {
                    await _pagoService.ActualizarPagoAsync(pagoOriginal, usuario, equipo, "Asignación manual de CC");
                }
            }

            return nuevosPagos;
        }

        private void ActualizarPagoConDesglose(Pago p, DesglosePagoXEO d)
        {
             p.Capital = d.Capital;
             p.Intereses = d.Intereses;
             p.Cargos = d.Cargos;
             p.Mora = d.Mora;
             p.Condonacion = 0;
             p.TotalAplicado = d.TotalAplicado;

             p.Resultado = "Aplicado";
             p.SaldoP = "0";
             p.Error = "Sin Novedad";
             p.Observacion = $"Desglose Manual CC {p.CC}";
        }

        private DesglosePagoXEO CrearDesgloseAgrupado(string cedula, string credito, List<DesglosePagoXEO> cuotas)
        {
            return new DesglosePagoXEO
            {
                Cedula = cedula,
                NumeroCredito = credito,
                Capital = cuotas.Sum(c => c.Capital),
                Intereses = cuotas.Sum(c => c.Intereses),
                Mora = cuotas.Sum(c => c.Mora),
                Cargos = cuotas.Sum(c => c.Cargos),
                TotalAplicado = cuotas.Sum(c => c.TotalAplicado)
            };
        }

        /// <summary>
        /// Concilia pagos importados contra movimientos CC del API.
        /// 1. Obtiene movimientos CC por fecha de consignacion
        /// 2. Cruza por Cedula + Monto
        /// 3. Obtiene nombres de clientes
        /// 4. Obtiene desglose (credito, capital, intereses, mora, cargos)
        /// 5. Bloquea si cuadra
        /// </summary>
        public async Task<ResumenConciliacion> ConciliarPagosConApiAsync(
            IEnumerable<Pago> pagos,
            string usuario,
            string equipo,
            IProgress<string> logger,
            bool soloNuevos = false,
            CancellationToken cancellationToken = default)
        {
            var resumen = new ResumenConciliacion();
            var pagosList = pagos
                .Where(p => !p.Bloqueado &&
                            p.Estado != EstadoPago.Aplicado &&
                            !string.IsNullOrWhiteSpace(p.CedulaCliente))
                .ToList();

            // Si soloNuevos, excluir pagos ya conciliados (NoCoincide) que no fueron corregidos
            // Esto evita re-consultar la API para pagos que ya sabemos que no tienen match
            if (soloNuevos)
            {
                var antes = pagosList.Count;
                pagosList = pagosList
                    .Where(p => p.Estado == EstadoPago.Importado ||
                                p.Estado == EstadoPago.Corregido ||
                                p.Estado == EstadoPago.Validado)
                    .ToList();
                if (antes != pagosList.Count)
                {
                    logger?.Report($"Modo soloNuevos: {pagosList.Count} pagos a procesar (se omiten {antes - pagosList.Count} ya conciliados)");
                }
            }

            resumen.TotalPagos = pagosList.Count;

            if (pagosList.Count == 0)
            {
                resumen.MensajeFinal = "No hay pagos pendientes de conciliacion.";
                return resumen;
            }

            // Verificar conexion
            if (!await _erpService.VerificarConexionAsync())
            {
                logger?.Report("Error: No hay conexion con el ERP.");
                resumen.MensajeFinal = "No hay conexion con el ERP.";
                return resumen;
            }

            // 1. Obtener fechas unicas de consignacion
            var fechasUnicas = pagosList
                .Select(p => p.FechaConsignacion.Date)
                .Distinct()
                .OrderBy(f => f)
                .ToList();

            logger?.Report($"Consultando movimientos CC para {fechasUnicas.Count} fecha(s)...");

            // 2. Obtener TODOS los movimientos CC para esas fechas
            var todosMovimientos = new List<MovimientoCC>();
            foreach (var fecha in fechasUnicas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var movs = await _erpService.ConsultarMovimientosCCPorFechaAsync(fecha);
                todosMovimientos.AddRange(movs);
            }

            logger?.Report($"Se encontraron {todosMovimientos.Count} movimientos CC en total.");

            if (todosMovimientos.Count == 0)
            {
                // Marcar todos como NoCoincide
                foreach (var p in pagosList)
                {
                    p.Estado = EstadoPago.NoCoincide;
                    p.Resultado = "Sin coincidencia";
                    p.Observacion = "No se encontraron movimientos CC para la fecha";
                }
                resumen.PagosSinCoincidencia = pagosList.Count;
                resumen.MensajeFinal = $"No se encontraron movimientos CC. {pagosList.Count} pagos sin coincidencia.";

                // Guardar
                foreach (var p in pagosList)
                {
                    try { await _pagoService.ActualizarPagoAsync(p, usuario, equipo, "Conciliacion API - Sin movimientos"); } catch { }
                }
                return resumen;
            }

            // 3. Cruzar pagos vs movimientos por Cedula + Monto
            // Usar lista mutable para "consumir" movimientos usados (evitar doble-match)
            var movimientosDisponibles = todosMovimientos.ToList();
            var pagosParaGuardar = new List<Pago>();

            // Log de cedulas disponibles en movimientos para diagnóstico
            var cedulasEnMovimientos = movimientosDisponibles.Select(m => m.Cedula.Trim()).Distinct().ToList();
            var cedulasEnPagos = pagosList.Select(p => (!string.IsNullOrWhiteSpace(p.CedulaCorrecta) ? p.CedulaCorrecta : p.CedulaCliente).Trim()).Distinct().ToList();
            logger?.Report($"Cedulas en movimientos CC: {cedulasEnMovimientos.Count} | Cedulas en pagos: {cedulasEnPagos.Count}");

            logger?.Report("Cruzando pagos con movimientos CC (Cedula + Monto)...");

            foreach (var pago in pagosList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cedula = (!string.IsNullOrWhiteSpace(pago.CedulaCorrecta) ? pago.CedulaCorrecta : pago.CedulaCliente).Trim();

                // Prioridad 1: Match exacto por Cedula + Monto
                var match = movimientosDisponibles.FirstOrDefault(m =>
                    m.Cedula.Trim() == cedula && m.Total == pago.Monto);

                // Prioridad 2: Si no hay match por cedula, intentar por solo Monto (si es único)
                if (match == null)
                {
                    var matchesPorMonto = movimientosDisponibles.Where(m => m.Total == pago.Monto).ToList();
                    if (matchesPorMonto.Count == 1)
                    {
                        match = matchesPorMonto[0];
                        logger?.Report($"  Match por Monto: Pago {cedula} ${pago.Monto:N0} → CC {match.Movimiento} (Cedula API: {match.Cedula})");
                    }
                }

                if (match != null)
                {
                    // Match encontrado
                    pago.CC = match.Movimiento;
                    pago.CedulaCorrecta = match.Cedula; // <--- Se asigna la cédula real del ERP
                    pago.Estado = EstadoPago.Aplicado;
                    pago.FechaAplicacion = match.Fecha.ToString("dd/MM/yyyy");
                    pago.Resultado = "Aplicado";
                    pago.Error = "Sin Novedad";
                    resumen.PagosConciliados++;

                    // Consumir el movimiento para no reutilizarlo
                    movimientosDisponibles.Remove(match);
                }
                else
                {
                    pago.Estado = EstadoPago.NoCoincide;
                    pago.Resultado = "Sin coincidencia";
                    pago.Observacion = $"No se encontro CC para cedula {cedula} por ${pago.Monto:N0}";
                    resumen.PagosSinCoincidencia++;
                }

                pagosParaGuardar.Add(pago);
            }

            logger?.Report($"Match: {resumen.PagosConciliados} conciliados, {resumen.PagosSinCoincidencia} sin coincidencia.");

            // 4. Obtener nombres de clientes para pagos conciliados
            var pagosConciliados = pagosParaGuardar.Where(p => p.Estado == EstadoPago.Aplicado).ToList();

            if (pagosConciliados.Count > 0)
            {
                var cedulasParaNombres = pagosConciliados
                    .Select(p => !string.IsNullOrWhiteSpace(p.CedulaCorrecta) ? p.CedulaCorrecta : p.CedulaCliente)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                logger?.Report($"Consultando nombres para {cedulasParaNombres.Count} cedula(s)...");

                var nombres = await _erpService.ObtenerNombresTercerosAsync(cedulasParaNombres);

                foreach (var pago in pagosConciliados)
                {
                    var cedula = (!string.IsNullOrWhiteSpace(pago.CedulaCorrecta) ? pago.CedulaCorrecta : pago.CedulaCliente).Trim();
                    if (nombres.TryGetValue(cedula, out var nombre))
                    {
                        pago.Titular = nombre;
                        resumen.PagosConNombre++;
                    }
                }

                // NOTA: El desglose (Capital, Intereses, Mora, Cargos) se obtiene en el Paso 3
                // (ProcesarDesgloseAsync) que es más eficiente y tiene paralelismo controlado.
                // Aquí solo se asigna CC + nombre para no saturar la API con llamadas redundantes.
            }

            // 6. Guardar todos los cambios en BD
            logger?.Report($"Guardando {pagosParaGuardar.Count} pagos en base de datos...");
            int guardados = 0;
            foreach (var pago in pagosParaGuardar)
            {
                try
                {
                    var desc = pago.Estado == EstadoPago.Aplicado ? "Conciliacion API - Aplicado" : "Conciliacion API - Sin coincidencia";
                    var resultado = await _pagoService.ActualizarPagoAsync(pago, usuario, equipo, desc);
                    if (resultado)
                    {
                        guardados++;
                    }
                    else
                    {
                        resumen.Errores++;
                        logger?.Report($"No se encontro pago {pago.IdCorto} en BD para actualizar");
                    }
                }
                catch (Exception ex)
                {
                    resumen.Errores++;
                    logger?.Report($"Error guardando pago {pago.IdCorto}: {ex.Message}");
                }
            }

            logger?.Report($"Guardados: {guardados}/{pagosParaGuardar.Count}");

            resumen.MensajeFinal = $"Conciliacion completada.\n" +
                $"Conciliados: {resumen.PagosConciliados}\n" +
                $"Sin coincidencia: {resumen.PagosSinCoincidencia}\n" +
                $"Con nombre: {resumen.PagosConNombre}\n" +
                $"Con desglose: {resumen.PagosConDesglose}\n" +
                $"Bloqueados: {resumen.PagosBloqueados}\n" +
                $"Guardados en BD: {guardados}/{pagosParaGuardar.Count}" +
                (resumen.Errores > 0 ? $"\nErrores: {resumen.Errores}" : "");

            logger?.Report(resumen.MensajeFinal.Replace("\n", " | "));
            return resumen;
        }

        /// <summary>
        /// Exporta y bloquea pagos conciliados en un rango de fechas.
        /// Trabaja SOLO contra la BD local, NO llama APIs del ERP.
        /// Flujo: consultar BD → bloquear conciliados → exportar Excel → marcar exportados.
        /// </summary>
        public async Task<ResumenExportacion> ExportarYBloquearFiduciariaAsync(
            DateTime startDate, DateTime endDate,
            string usuario, string equipo, string carpetaDestino,
            IProgress<string> logger,
            CancellationToken cancellationToken = default)
        {
            var resumen = new ResumenExportacion();

            // ============================================================
            // 1. CONSULTAR pagos en el rango de fechas (solo BD local)
            // ============================================================
            logger?.Report($"Consultando pagos del {startDate:dd/MM/yyyy} al {endDate:dd/MM/yyyy}...");
            
            var todosPagos = await _pagoService.BuscarPagosAsync(
                fechaDesde: startDate, 
                fechaHasta: endDate);

            resumen.TotalConsultados = todosPagos.Count;
            logger?.Report($"Se encontraron {todosPagos.Count} pagos en el rango.");

            if (todosPagos.Count == 0)
            {
                resumen.MensajeFinal = "No se encontraron pagos en el rango de fechas especificado.";
                return resumen;
            }

            // Contar los que ya estaban bloqueados
            resumen.TotalYaBloqueados = todosPagos.Count(p => p.Bloqueado);

            // ============================================================
            // 2. BLOQUEAR pagos conciliados que aún no están bloqueados
            // ============================================================
            cancellationToken.ThrowIfCancellationRequested();
            logger?.Report("Bloqueando pagos conciliados (Aplicado + Cuadrado)...");

            var nuevosBloqueados = await _pagoService.BloquearPagosConciliadosAsync(startDate, endDate, usuario);
            resumen.TotalBloqueados = nuevosBloqueados;
            logger?.Report($"Bloqueados: {nuevosBloqueados} pagos nuevos (ya bloqueados: {resumen.TotalYaBloqueados}).");

            // ============================================================
            // 3. RE-CONSULTAR pagos bloqueados para exportar
            //    (incluye los recién bloqueados + los ya bloqueados anteriormente)
            // ============================================================
            cancellationToken.ThrowIfCancellationRequested();
            var pagosParaExportar = await _pagoService.BuscarPagosAsync(
                fechaDesde: startDate, 
                fechaHasta: endDate);

            // Filtrar solo los bloqueados y aplicados con desglose real
            // (evita exportar históricos bloqueados con Capital/Intereses en 0).
            pagosParaExportar = pagosParaExportar
                .Where(p => p.Bloqueado && p.Estado == EstadoPago.Aplicado && p.TieneDatosAplicacion)
                .ToList();

            if (pagosParaExportar.Count == 0)
            {
                resumen.MensajeFinal = $"No hay pagos bloqueados para exportar en el rango.\n" +
                    $"Total pagos: {resumen.TotalConsultados}\n" +
                    $"Verifique que los pagos estén conciliados, cuadrados y con desglose (Capital/Intereses/Cargos/Mora).";
                return resumen;
            }

            // ============================================================
            // 4. EXPORTAR a Excel con formato fiduciaria
            // ============================================================
            cancellationToken.ThrowIfCancellationRequested();
            logger?.Report($"Exportando {pagosParaExportar.Count} pagos a Excel...");

            // Asegurar que la carpeta exista
            if (!Directory.Exists(carpetaDestino))
            {
                Directory.CreateDirectory(carpetaDestino);
            }

            var (ruta, totalRegistros) = await _excelExportService.ExportarPagosFiduciariaAsync(
                pagosParaExportar, carpetaDestino, startDate, endDate);

            resumen.RutaArchivo = ruta;
            resumen.TotalExportados = totalRegistros;

            // ============================================================
            // 5. MARCAR como exportados en BD (trazabilidad)
            // ============================================================
            cancellationToken.ThrowIfCancellationRequested();
            logger?.Report("Registrando exportación en base de datos...");

            var pagoIds = pagosParaExportar.Select(p => p.Id).ToList();
            await _pagoService.MarcarComoExportadosAsync(pagoIds, usuario, ruta);

            // ============================================================
            // 6. RESUMEN FINAL
            // ============================================================
            resumen.MensajeFinal = $"Exportación completada exitosamente.\n\n" +
                $"Rango: {startDate:dd/MM/yyyy} → {endDate:dd/MM/yyyy}\n" +
                $"Total pagos en rango: {resumen.TotalConsultados}\n" +
                $"Nuevos bloqueados: {resumen.TotalBloqueados}\n" +
                $"Ya bloqueados anteriormente: {resumen.TotalYaBloqueados}\n" +
                $"Exportados al Excel: {resumen.TotalExportados}\n\n" +
                $"Archivo: {Path.GetFileName(ruta)}";

            logger?.Report(resumen.MensajeFinal.Replace("\n", " | "));
            return resumen;
        }
    }
}
