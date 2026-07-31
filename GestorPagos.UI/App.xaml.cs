using System;
using System.Windows;
using GestorPagos.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace GestorPagos.UI
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; } = null!;
        public IConfiguration Configuration { get; private set; } = null!;

        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Error no manejado: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Error crítico: {ex?.Message}\n\n{ex?.StackTrace}",
                "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                // Usar el directorio de la aplicación, no el directorio de trabajo actual
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                
                var builder = new ConfigurationBuilder()
                    .SetBasePath(appDir)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                Configuration = builder.Build();

                var serviceCollection = new ServiceCollection();
                ConfigureServices(serviceCollection);

                ServiceProvider = serviceCollection.BuildServiceProvider();

                // Asegurar que la base de datos esté actualizada con todas las columnas
                using (var scope = ServiceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    // Verificar conexión y agregar columnas faltantes
                    try
                    {
                        // Intentar crear la tabla si no existe
                        dbContext.Database.EnsureCreated();
                        
                        // Ejecutar migración manual para agregar columnas faltantes
                        var connection = dbContext.Database.GetDbConnection();
                        connection.Open();
                        
                        using var command = connection.CreateCommand();
                        command.CommandText = @"
                            DO $$ 
                            BEGIN
                                -- ============================================
                                -- MIGRACIÓN COMPLETA - TODAS LAS COLUMNAS
                                -- ============================================
                                
                                -- Identificadores
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CodigoPago') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CodigoPago"" VARCHAR(20) DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='HashUnico') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""HashUnico"" VARCHAR(16) DEFAULT '';
                                END IF;
                                
                                -- Datos principales
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Consecutivo') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Consecutivo"" INTEGER DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CedulaCorrecta') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CedulaCorrecta"" VARCHAR(20) DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CedulaCliente') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CedulaCliente"" VARCHAR(20) DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Monto') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Monto"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaConsignacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaConsignacion"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Convenio') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Convenio"" VARCHAR(50) DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='NumeroAprobacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""NumeroAprobacion"" VARCHAR(100) DEFAULT '';
                                END IF;
                                
                                -- Campos de resultado
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Resultado') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Resultado"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Error') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Error"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Asumido') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Asumido"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='SaldoP') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""SaldoP"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Observacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Observacion"" TEXT DEFAULT '';
                                END IF;
                                
                                -- ERP/CC
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CC') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CC"" VARCHAR(50);
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaAplicacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaAplicacion"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Credito') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Credito"" TEXT;
                                END IF;
                                
                                -- Desglose del pago
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Capital') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Capital"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Intereses') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Intereses"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Cargos') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Cargos"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Mora') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Mora"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Condonacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Condonacion"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='TotalAplicado') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""TotalAplicado"" DECIMAL(19,4) DEFAULT 0;
                                END IF;
                                
                                -- Info adicional crédito
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CuotaActual') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CuotaActual"" INTEGER;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='CuotasPendientes') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""CuotasPendientes"" INTEGER;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='DiasMora') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""DiasMora"" INTEGER;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='ListaCreditos') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""ListaCreditos"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaProximaCuota') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaProximaCuota"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Excedente') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Excedente"" DECIMAL(19,4);
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='EsPagoDoble') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""EsPagoDoble"" BOOLEAN DEFAULT FALSE;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='ValorCuota') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""ValorCuota"" DECIMAL(19,4);
                                END IF;
                                
                                -- Campos XEO (prediccion antes del RPA)
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='XEO_Error') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""XEO_Error"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='XEO_SaldoP') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""XEO_SaldoP"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='XEO_Observacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""XEO_Observacion"" TEXT;
                                END IF;
                                
                                -- Trazabilidad - Importacion
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaImportacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaImportacion"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='UsuarioImportacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""UsuarioImportacion"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='EquipoImportacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""EquipoImportacion"" TEXT DEFAULT '';
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='ArchivoOrigen') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""ArchivoOrigen"" TEXT DEFAULT '';
                                END IF;
                                
                                -- Trazabilidad - Modificación
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaModificacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaModificacion"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='UsuarioModificacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""UsuarioModificacion"" TEXT;
                                END IF;
                                
                                -- Trazabilidad - Exportación
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaExportacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaExportacion"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='UsuarioExportacion') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""UsuarioExportacion"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='ArchivoExportado') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""ArchivoExportado"" TEXT;
                                END IF;
                                
                                -- Auditoría - Desglose y Bloqueo
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='UsuarioDesglose') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""UsuarioDesglose"" TEXT;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaDesglose') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaDesglose"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Bloqueado') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Bloqueado"" BOOLEAN DEFAULT FALSE;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='FechaBloqueo') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""FechaBloqueo"" TIMESTAMP;
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='UsuarioBloqueo') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""UsuarioBloqueo"" TEXT;
                                END IF;
                                
                                -- Estado
                                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Pagos' AND column_name='Estado') THEN
                                    ALTER TABLE ""Pagos"" ADD COLUMN ""Estado"" INTEGER DEFAULT 1;
                                END IF;
                            END $$;
                        ";
                        command.ExecuteNonQuery();
                        
                        // Crear índice único para HashUnico si no existe
                        using var indexCommand = connection.CreateCommand();
                        indexCommand.CommandText = @"
                            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Pagos_HashUnico"" ON ""Pagos"" (""HashUnico"") WHERE ""HashUnico"" IS NOT NULL AND ""HashUnico"" <> '';
                        ";
                        indexCommand.ExecuteNonQuery();

                        // Migrar estados RPA obsoletos a los nuevos estados
                        using var migrationCommand = connection.CreateCommand();
                        migrationCommand.CommandText = @"
                            DO $$
                            BEGIN
                                -- Migrar estados RPA obsoletos
                                UPDATE ""Pagos"" SET ""Estado"" = 1 WHERE ""Estado"" = 5;
                                UPDATE ""Pagos"" SET ""Estado"" = 11 WHERE ""Estado"" = 7;
                                UPDATE ""Pagos"" SET ""Estado"" = 4 WHERE ""Estado"" = 8;
                                UPDATE ""Pagos"" SET ""Estado"" = 1 WHERE ""Estado"" = 9;
                                UPDATE ""Pagos"" SET ""Estado"" = 6 WHERE ""Estado"" = 10;
                            END $$;
                        ";
                        migrationCommand.ExecuteNonQuery();

#if DEBUG
                        System.Diagnostics.Debug.WriteLine("Migracion de BD completada exitosamente");
#endif
                    }
                    catch (Exception)
                    {
                        // Continuar aunque falle la migracion - la app puede funcionar sin guardar en BD
                    }
                }

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al iniciar la aplicación: {ex.Message}\n\n{ex.StackTrace}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            services.AddSingleton(Configuration);

            // Database - Conexión a PostgreSQL en servidor Ubuntu
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"PostgreSQL ConnectionString: {connectionString}");
#endif
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection' para PostgreSQL en appsettings.json");
            }
            
            // [FIX] Agregar timeout más largo para conexiones VPN lentas
            // y usar pooling con límites adecuados
            var finalConnectionString = connectionString;
            if (!connectionString.Contains("Timeout="))
            {
                finalConnectionString += ";Timeout=30;Command Timeout=60";
            }
            
            // [FIX] Usar AddDbContextFactory para permitir crear contextos frescos en cada operación
            // Esto evita el error "A second operation was started on this context instance"
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseNpgsql(finalConnectionString));
            
            // También registrar el contexto normal para operaciones síncronas simples
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(finalConnectionString));

            // Core Services
            services.AddTransient<GestorPagos.Core.Services.IImportadorService, GestorPagos.Core.Services.ImportadorService>();
            services.AddTransient<GestorPagos.Core.Services.IExcelExportService, GestorPagos.Core.Services.ExcelExportService>();
            services.AddTransient<GestorPagos.Core.Services.IArchivoPlanoService, GestorPagos.Core.Services.ArchivoPlanoService>();
            // [FIX] Cambiar a Transient para que cada solicitud obtenga un nuevo PagoService con su propio DbContext
            services.AddTransient<GestorPagos.Core.Services.IPagoService, GestorPagos.Data.Services.PagoService>();

            // ERP Service - Integración con API XEO
            var erpConfig = Configuration.GetSection("ErpApi");
            var erpBaseUrl = erpConfig["BaseUrl"] ?? "";
            var erpToken = erpConfig["Token"] ?? "";
            var validarContraERP = bool.TryParse(erpConfig["ValidarContraERP"], out var v) && v;
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"ERP Config - BaseUrl: {erpBaseUrl}, Token: {(string.IsNullOrEmpty(erpToken) ? "NO" : "SI")}, Validar: {validarContraERP}");
#endif
            
            // Siempre registrar el servicio ERP si hay URL y Token
            if (!string.IsNullOrEmpty(erpBaseUrl) && !string.IsNullOrEmpty(erpToken))
            {
                var erpService = new GestorPagos.Data.Services.ErpService(erpBaseUrl, erpToken);
                services.AddSingleton<GestorPagos.Core.Services.IErpService>(erpService);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"ERP Service CREADO: {erpBaseUrl}");
#endif
            }
            else
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("ERP Service NO configurado - faltan BaseUrl o Token");
#endif
            }

            // [REFAC] Servicio de Procesos de Negocio (Desglose, Validación)
            services.AddTransient<GestorPagos.Core.Services.IGestorDeProcesosService, GestorPagos.Data.Services.GestorDeProcesosService>();

            // ViewModels - Inyección manual para asegurar que IErpService se pase correctamente
            services.AddTransient<ViewModels.MainViewModel>(sp =>
            {
                var importador = sp.GetRequiredService<GestorPagos.Core.Services.IImportadorService>();
                var excel = sp.GetRequiredService<GestorPagos.Core.Services.IExcelExportService>();
                var pago = sp.GetRequiredService<GestorPagos.Core.Services.IPagoService>();
                var archivoPlano = sp.GetRequiredService<GestorPagos.Core.Services.IArchivoPlanoService>();
                var erp = sp.GetService<GestorPagos.Core.Services.IErpService>();
                var gestorProcesos = sp.GetRequiredService<GestorPagos.Core.Services.IGestorDeProcesosService>();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"MainViewModel creado - ERP Service: {(erp != null ? "SI" : "NO")}");
#endif
                return new ViewModels.MainViewModel(importador, excel, pago, archivoPlano, erp, gestorProcesos);
            });

            // UI
            services.AddTransient<MainWindow>();
        }
    }
}
