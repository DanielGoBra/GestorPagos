# Script de validación de API del ERP
# Prueba conectividad y estructura de respuestas

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  VALIDACIÓN API ERP - GestorPagos" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Leer configuración
$configPath = "c:\Demos\GestorPagos\GestorPagos.UI\appsettings.json"
$config = Get-Content $configPath | ConvertFrom-Json

$baseUrl = $config.ErpApi.BaseUrl
$token = $config.ErpApi.Token

Write-Host "Base URL: $baseUrl" -ForegroundColor Yellow
Write-Host "Token configurado: " -NoNewline
if ($token) { Write-Host "SI" -ForegroundColor Green } else { Write-Host "NO" -ForegroundColor Red }
Write-Host ""

# Función para probar endpoint
function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [string[]]$ExpectedFields
    )
    
    Write-Host "[$Name]" -ForegroundColor Cyan
    Write-Host "  URL: $Url" -ForegroundColor Gray
    
    try {
        $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 10 -ErrorAction Stop
        Write-Host "  Estado: " -NoNewline
        Write-Host "CONECTADO" -ForegroundColor Green
        
        # Validar estructura
        if ($response -is [array] -and $response.Count -gt 0) {
            $firstItem = $response[0]
            Write-Host "  Tipo respuesta: Array con $($response.Count) elementos" -ForegroundColor Gray
            
            # Listar campos disponibles
            $campos = $firstItem.PSObject.Properties.Name
            Write-Host "  Campos encontrados ($($campos.Count)): $($campos -join ', ')" -ForegroundColor Gray
            
            # Validar campos esperados
            $faltantes = @()
            foreach ($field in $ExpectedFields) {
                if ($campos -notcontains $field) {
                    $faltantes += $field
                }
            }
            
            if ($faltantes.Count -eq 0) {
                Write-Host "  Validacion: " -NoNewline
                Write-Host "TODOS LOS CAMPOS PRESENTES [OK]" -ForegroundColor Green
            }
            else {
                Write-Host "  Validacion: " -NoNewline
                Write-Host "CAMPOS FALTANTES [ERROR]" -ForegroundColor Red
                Write-Host "    Faltantes: $($faltantes -join ', ')" -ForegroundColor Red
            }
            
            # Mostrar muestra de datos
            Write-Host "  Muestra de datos:" -ForegroundColor Gray
            $firstItem | Format-List | Out-String | ForEach-Object { 
                $_.Split("`n") | Select-Object -First 10 | ForEach-Object {
                    Write-Host "    $_" -ForegroundColor DarkGray
                }
            }
            
        } elseif ($response.PSObject.Properties.Name -contains "NOMBRE" -or $response.PSObject.Properties.Name -contains "error") {
            Write-Host "  Tipo respuesta: Objeto" -ForegroundColor Gray
            $campos = $response.PSObject.Properties.Name
            Write-Host "  Campos: $($campos -join ', ')" -ForegroundColor Gray
            
            if ($campos -contains "error") {
                Write-Host "  Resultado: " -NoNewline
                Write-Host "ERROR DEL SERVIDOR" -ForegroundColor Red
                Write-Host "    Mensaje: $($response.error)" -ForegroundColor Red
            }
            else {
                Write-Host "  Validacion: " -NoNewline
                Write-Host "OK [CORRECTO]" -ForegroundColor Green
                $response | Format-List | Out-String | ForEach-Object {
                    $_.Split("`n") | Select-Object -First 5 | ForEach-Object {
                        Write-Host "    $_" -ForegroundColor DarkGray
                    }
                }
            }
        } else {
            Write-Host "  Tipo respuesta: " -NoNewline
            Write-Host "FORMATO INESPERADO" -ForegroundColor Yellow
            Write-Host "    Contenido: $($response | ConvertTo-Json -Depth 2)" -ForegroundColor DarkGray
        }
        
        return $true
    }
    catch {
        Write-Host "  Estado: " -NoNewline
        Write-Host "ERROR" -ForegroundColor Red
        Write-Host "    Detalle: $($_.Exception.Message)" -ForegroundColor Red
        
        if ($_.Exception.Response) {
            Write-Host "    HTTP Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
        }
        
        return $false
    }
    
    Write-Host ""
}

# PRUEBA 1: Plan de Pagos (con cédula de prueba 0)
Write-Host "`n--- PRUEBA 1: Plan de Pagos ---" -ForegroundColor White
$cedulaParam = "cedula=0"
$url1 = "$baseUrl/plan_pagos_actualizado_cliente?token=$token" + '&' + $cedulaParam
$esperados1 = @("CREDITO", "CUOTA", "VENCIMIENTO", "DIAS_MORA", "CAPITAL")
$test1 = Test-Endpoint -Name "plan_pagos_actualizado_cliente" -Url $url1 -ExpectedFields $esperados1

# PRUEBA 2: Desglose de Movimientos (con número de comprobante de prueba)
Write-Host "`n--- PRUEBA 2: Desglose de Movimientos ---" -ForegroundColor White
$numParam = "num_m=TEST123"
$url2 = "$baseUrl/desglose_movimientos?token=$token" + '&' + $numParam
$esperados2 = @("suma_K", "suma_I", "suma_O", "suma_M", "total_CC")
$test2 = Test-Endpoint -Name "desglose_movimientos" -Url $url2 -ExpectedFields $esperados2

# PRUEBA 3: Nombre Cliente (con cédula de prueba)
Write-Host "`n--- PRUEBA 3: Nombre Cliente ---" -ForegroundColor White
$cedParam2 = "cedula=0"
$url3 = "$baseUrl/nombre_cliente?token=$token" + '&' + $cedParam2
$esperados3 = @("NOMBRE")
$test3 = Test-Endpoint -Name "nombre_cliente" -Url $url3 -ExpectedFields $esperados3

# RESUMEN
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  RESUMEN DE VALIDACIÓN" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$resultados = @(
    @{Nombre="Plan de Pagos"; Estado=$test1},
    @{Nombre="Desglose Movimientos"; Estado=$test2},
    @{Nombre="Nombre Cliente"; Estado=$test3}
)

foreach ($r in $resultados) {
    Write-Host "  $($r.Nombre): " -NoNewline
    if ($r.Estado) {
        Write-Host "[OK] Funciona correctamente" -ForegroundColor Green
    }
    else {
        Write-Host "[ERROR] Fallo en la conexion" -ForegroundColor Red
    }
}

$exitosos = ($resultados | Where-Object {$_.Estado}).Count
$total = $resultados.Count

Write-Host "`n  Total: $exitosos/$total endpoints funcionando correctamente" -ForegroundColor $(if ($exitosos -eq $total) {"Green"} else {"Yellow"})
Write-Host ""

# Recomendaciones
if ($exitosos -lt $total) {
    Write-Host "`n[ADVERTENCIA] RECOMENDACIONES:" -ForegroundColor Yellow
    Write-Host "  1. Verificar que el servidor este accesible" -ForegroundColor Gray
    Write-Host "  2. Validar que el token sea correcto y no haya expirado" -ForegroundColor Gray
    Write-Host "  3. Confirmar que los endpoints esten activos en el servidor" -ForegroundColor Gray
    Write-Host "  4. Revisar configuracion de firewall/red" -ForegroundColor Gray
}

Write-Host ""
