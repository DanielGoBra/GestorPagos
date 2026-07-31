using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GestorPagos.Core.Entities;
using GestorPagos.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GestorPagos.Data.Services
{
    public class PagoService : IPagoService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        
        // Semáforo para prevenir operaciones concurrentes en la BD
        private static readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

        public PagoService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }
        
        // Helper para crear un nuevo contexto para cada operación
        private AppDbContext CreateContext() => _contextFactory.CreateDbContext();

        public async Task<int> GuardarPagosAsync(List<Pago> pagos, string usuario, string equipo, string archivoOrigen)
        {
            if (pagos == null || pagos.Count == 0) return 0;

            using var context = CreateContext();
            
            int secuenciaActual = await ObtenerSiguienteSecuenciaAsync();
            var fechaHoy = DateTime.UtcNow;  // UTC para PostgreSQL
            int guardados = 0;

            // Obtener todos los hashes de los pagos a importar
            var hashesNuevos = pagos.Select(p => p.HashUnico).Where(h => !string.IsNullOrEmpty(h)).ToList();
            
            // Buscar pagos existentes por hash (una sola consulta a BD)
            var pagosExistentes = await ObtenerPagosExistentesPorHashesAsync(hashesNuevos);

            // Set para control de duplicados LOCALES (dentro del mismo archivo)
            var hashesProcesadosEnLote = new HashSet<string>();

            foreach (var pago in pagos)
            {
                // Verificar si ya existe usando el HashUnico (BD)
                if (pagosExistentes.ContainsKey(pago.HashUnico))
                {
                    continue; // Saltar duplicados - el pago ya existe en BD
                }

                // Verificar si ya lo procesamos en este mismo lote (Duplicado en archivo)
                if (hashesProcesadosEnLote.Contains(pago.HashUnico))
                {
                    continue; // Saltar duplicados internos del archivo
                }
                hashesProcesadosEnLote.Add(pago.HashUnico);

                // Generar código único
                pago.CodigoPago = Pago.GenerarCodigoPago(fechaHoy, secuenciaActual++);
                
                // Agregar trazabilidad - Asegurar UTC
                pago.UsuarioImportacion = usuario;
                pago.EquipoImportacion = equipo;
                pago.ArchivoOrigen = archivoOrigen;
                pago.FechaImportacion = fechaHoy;
                pago.Estado = EstadoPago.Importado;
                
                // Asegurar que FechaConsignacion sea UTC
                if (pago.FechaConsignacion.Kind == DateTimeKind.Unspecified)
                {
                    pago.FechaConsignacion = DateTime.SpecifyKind(pago.FechaConsignacion, DateTimeKind.Utc);
                }

                context.Pagos.Add(pago);

                // Registrar en historial
                var historial = HistorialPago.CrearRegistro(
                    pago.Id,
                    pago.CodigoPago,
                    TipoAccion.Importado,
                    $"Pago importado desde archivo: {archivoOrigen}",
                    usuario,
                    equipo
                );
                context.HistorialPagos.Add(historial);

                guardados++;
            }

            try
            {
                await context.SaveChangesAsync();
                return guardados;
            }
            catch (Exception ex)
            {
                // Si falla el guardado, limpiar el tracker para no dejar entidades "Added" sucias
                // que contaminen futuros SaveChanges (como el de ActualizarPago).
                context.ChangeTracker.Clear();
                throw new Exception($"Error guardando pagos: {ex.Message}", ex);
            }
        }

        public async Task<int> ObtenerSiguienteSecuenciaAsync()
        {
            using var context = CreateContext();
            var fechaHoy = DateTime.Now.Date;
            var prefijo = $"PAG-{fechaHoy:yyyyMMdd}-";

            var ultimoCodigo = await context.Pagos
                .Where(p => p.CodigoPago.StartsWith(prefijo))
                .OrderByDescending(p => p.CodigoPago)
                .Select(p => p.CodigoPago)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(ultimoCodigo))
            {
                return 1;
            }

            // Extraer el número de secuencia del código
            var partes = ultimoCodigo.Split('-');
            if (partes.Length == 3 && int.TryParse(partes[2], out int secuencia))
            {
                return secuencia + 1;
            }

            return 1;
        }

        public async Task<List<Pago>> BuscarPagosAsync(string? cedula = null, string? convenio = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            using var context = CreateContext();
            var query = context.Pagos.AsQueryable();

            if (!string.IsNullOrEmpty(cedula))
                query = query.Where(p => p.CedulaCliente.Contains(cedula));

            if (!string.IsNullOrEmpty(convenio))
                query = query.Where(p => p.Convenio == convenio);

            if (fechaDesde.HasValue)
                query = query.Where(p => p.FechaConsignacion >= fechaDesde.Value);

            if (fechaHasta.HasValue)
                query = query.Where(p => p.FechaConsignacion <= fechaHasta.Value);

            return await query.OrderByDescending(p => p.FechaImportacion).ToListAsync();
        }

        public async Task<List<HistorialPago>> ObtenerHistorialAsync(Guid pagoId)
        {
            using var context = CreateContext();
            return await context.HistorialPagos
                .Where(h => h.PagoId == pagoId)
                .OrderByDescending(h => h.FechaAccion)
                .ToListAsync();
        }

        public async Task<bool> ActualizarPagoAsync(Pago pago, string usuario, string equipo, string descripcionCambio)
        {
            using var context = CreateContext();
            var pagoExistente = await context.Pagos.FindAsync(pago.Id);
            if (pagoExistente == null) return false;

            // Registrar el cambio en historial
            var historial = HistorialPago.CrearRegistro(
                pago.Id,
                pago.CodigoPago,
                TipoAccion.Modificado,
                descripcionCambio,
                usuario,
                equipo
            );
            context.HistorialPagos.Add(historial);

            // Actualizar campos de trazabilidad - UTC para PostgreSQL
            pagoExistente.FechaModificacion = DateTime.UtcNow;
            pagoExistente.UsuarioModificacion = usuario;
            
            // Actualizar campos del resultado
            pagoExistente.Resultado = pago.Resultado;
            pagoExistente.Observacion = pago.Observacion;
            pagoExistente.Estado = pago.Estado;
            pagoExistente.Error = pago.Error;
            
            // Actualizar datos obtenidos del ERP
            pagoExistente.Titular = pago.Titular;
            pagoExistente.CedulaCorrecta = pago.CedulaCorrecta;
            
            // Actualizar predicciones XEO
            pagoExistente.XEO_Error = pago.XEO_Error;
            pagoExistente.XEO_Observacion = pago.XEO_Observacion;
            pagoExistente.XEO_SaldoP = pago.XEO_SaldoP;
            
            // Actualizar campos de DESGLOSE (desde XEO)
            pagoExistente.Capital = pago.Capital;
            pagoExistente.Intereses = pago.Intereses;
            pagoExistente.Cargos = pago.Cargos;
            pagoExistente.Mora = pago.Mora;
            pagoExistente.TotalAplicado = pago.TotalAplicado;
            pagoExistente.Condonacion = pago.Condonacion;
            
            // Actualizar campos de auditoría de desglose
            pagoExistente.UsuarioDesglose = pago.UsuarioDesglose;
            pagoExistente.FechaDesglose = pago.FechaDesglose;
            
            // Actualizar bloqueo si corresponde
            pagoExistente.Bloqueado = pago.Bloqueado;
            pagoExistente.FechaBloqueo = pago.FechaBloqueo;
            pagoExistente.UsuarioBloqueo = pago.UsuarioBloqueo;
            
            // Actualizar crédito
            pagoExistente.Credito = pago.Credito;
            pagoExistente.CC = pago.CC;
            pagoExistente.FechaAplicacion = pago.FechaAplicacion;

            // Actualizar campos de validación XEO (cuotas, mora, etc.)
            pagoExistente.CuotaActual = pago.CuotaActual;
            pagoExistente.CuotasPendientes = pago.CuotasPendientes;
            pagoExistente.DiasMora = pago.DiasMora;
            pagoExistente.ValorCuota = pago.ValorCuota;
            pagoExistente.FechaProximaCuota = pago.FechaProximaCuota;
            pagoExistente.ListaCreditos = pago.ListaCreditos;
            pagoExistente.EsPagoDoble = pago.EsPagoDoble;
            pagoExistente.SaldoP = pago.SaldoP;
            pagoExistente.Asumido = pago.Asumido;

            await context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> ActualizarTitularAsync(Guid id, string titular)
        {
            using var context = CreateContext();
            // Ejecutar UPDATE directo para saltar validaciones de EF Core o bloqueos lógicos
            // Usamos parámetros {0} y {1} para prevenir inyección SQL
            int rows = await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Pagos\" SET \"Titular\" = {0} WHERE \"Id\" = {1}", 
                titular ?? (object)DBNull.Value, id);
                
            return rows > 0;
        }

        /// <summary>
        /// Actualiza solo la cédula correcta (para pagos importados antes de esta lógica)
        /// </summary>
        public async Task<bool> ActualizarCedulaCorrectaAsync(Guid id, string cedulaCorrecta)
        {
            using var context = CreateContext();
            int rows = await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Pagos\" SET \"CedulaCorrecta\" = {0} WHERE \"Id\" = {1}", 
                cedulaCorrecta ?? (object)DBNull.Value, id);
                
            return rows > 0;
        }

        public async Task<int> MarcarComoExportadosAsync(List<Guid> pagoIds, string usuario, string archivoExportado)
        {
            using var context = CreateContext();
            var pagos = await context.Pagos
                .Where(p => pagoIds.Contains(p.Id))
                .ToListAsync();

            foreach (var pago in pagos)
            {
                pago.FechaExportacion = DateTime.UtcNow;
                pago.UsuarioExportacion = usuario;
                pago.ArchivoExportado = archivoExportado;

                // Registrar en historial
                var historial = HistorialPago.CrearRegistro(
                    pago.Id,
                    pago.CodigoPago,
                    TipoAccion.Exportado,
                    $"Pago exportado: {archivoExportado}",
                    usuario,
                    Environment.MachineName
                );
                context.HistorialPagos.Add(historial);
            }

            await context.SaveChangesAsync();
            return pagos.Count;
        }

        public async Task<bool> ExistePagoAsync(string cedula, decimal monto, DateTime fechaConsignacion, string convenio)
        {
            using var context = CreateContext();
            // Usar HashUnico para detección más eficiente
            var hashUnico = Pago.GenerarHashUnico(cedula, monto, fechaConsignacion);
            return await context.Pagos.AnyAsync(p => p.HashUnico == hashUnico);
        }

        /// <summary>
        /// Obtiene un pago existente por su HashUnico (Cédula + Monto + FechaConsignación)
        /// </summary>
        public async Task<Pago?> ObtenerPorHashAsync(string hashUnico)
        {
            using var context = CreateContext();
            return await context.Pagos.FirstOrDefaultAsync(p => p.HashUnico == hashUnico);
        }

        /// <summary>
        /// Obtiene pagos existentes por sus hashes únicos (para importación masiva)
        /// </summary>
        public async Task<Dictionary<string, Pago>> ObtenerPagosExistentesPorHashesAsync(IEnumerable<string> hashes)
        {
            using var context = CreateContext();
            var hashList = hashes.ToList();
            var pagosExistentes = await context.Pagos
                .Where(p => hashList.Contains(p.HashUnico))
                .ToDictionaryAsync(p => p.HashUnico, p => p);
            return pagosExistentes;
        }

        public async Task VerificarEsquemaAsync()
        {
            try
            {
                using var context = CreateContext();
                // MIGRACIÓN MANUAL: Agregar columna Titular si no existe
                // PostgreSQL syntax
                await context.Database.ExecuteSqlRawAsync(
                    @"DO $$ 
                      BEGIN 
                        BEGIN
                            ALTER TABLE ""Pagos"" ADD COLUMN ""Titular"" text;
                        EXCEPTION
                            WHEN duplicate_column THEN RAISE NOTICE 'column Titular already exists in Pagos.';
                        END;
                        
                        -- Asegurar que permita nulos por compatibilidad inicial
                        ALTER TABLE ""Pagos"" ALTER COLUMN ""Titular"" DROP NOT NULL;
                      END $$;");
            }
            catch (Exception ex)
            {
                // Ignorar error si ya existe o falla (no bloquear app)
                System.Diagnostics.Debug.WriteLine($"Error verificación esquema: {ex.Message}");
            }
        }

        /// <summary>
        /// Bloquea en masa los pagos conciliados (Aplicado + EstaCuadrado) dentro de un rango de fechas.
        /// NO llama APIs externas, solo trabaja contra la BD local.
        /// </summary>
        public async Task<int> BloquearPagosConciliadosAsync(DateTime startDate, DateTime endDate, string usuario)
        {
            using var context = CreateContext();

            // Consultar pagos aplicados NO bloqueados en el rango de fechas
            var pagosCandidate = await context.Pagos
                .Where(p => p.Estado == EstadoPago.Aplicado
                         && !p.Bloqueado
                         && p.FechaConsignacion >= startDate
                         && p.FechaConsignacion <= endDate)
                .ToListAsync();

            // Filtrar en memoria por EstaCuadrado (propiedad calculada, no persistida en BD)
            var pagosParaBloquear = pagosCandidate.Where(p => p.EstaCuadrado).ToList();

            if (pagosParaBloquear.Count == 0) return 0;

            var fechaBloqueo = DateTime.UtcNow;
            var equipo = Environment.MachineName;

            foreach (var pago in pagosParaBloquear)
            {
                pago.Bloqueado = true;
                pago.FechaBloqueo = fechaBloqueo;
                pago.UsuarioBloqueo = usuario;

                // Registrar en historial
                var historial = HistorialPago.CrearRegistro(
                    pago.Id,
                    pago.CodigoPago,
                    TipoAccion.Modificado,
                    $"Bloqueo masivo fiduciaria ({startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy})",
                    usuario,
                    equipo
                );
                context.HistorialPagos.Add(historial);
            }

            await context.SaveChangesAsync();
            return pagosParaBloquear.Count;
        }
    }
}
