# Validacion de API del ERP - Version Simplificada
Write-Host "`n=== VALIDACION API ERP - GestorPagos ===`n" -ForegroundColor Cyan

# Leer configuracion
$config = Get-Content "c:\Demos\GestorPagos\GestorPagos.UI\appsettings.json" | ConvertFrom-Json
$baseUrl = $config.ErpApi.BaseUrl
$token = $config.ErpApi.Token

Write-Host "Base URL: $baseUrl" -ForegroundColor Yellow
Write-Host "Token: $(if($token){'Configurado'}else{'NO configurado'})`n" -ForegroundColor $(if($token){'Green'}else{'Red'})

# Configurar para ignorar errores SSL si es necesario
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# Test 1: Plan de Pagos
Write-Host "`n--- TEST 1: Plan de Pagos ---" -ForegroundColor White
$url1 = "$baseUrl/plan_pagos_actualizado_cliente?token=$token" + [char]38 + "cedula=0"
Write-Host "URL: $url1" -ForegroundColor Gray
try {
    $resp1 = Invoke-RestMethod -Uri $url1 -Method Get -TimeoutSec 15
    Write-Host "Estado: CONECTADO" -ForegroundColor Green
    if ($resp1 -is [array]) {
        Write-Host "Elementos recibidos: $($resp1.Count)" -ForegroundColor Green
        if ($resp1.Count -gt 0) {
            $campos = $resp1[0].PSObject.Properties.Name
            Write-Host "Campos: $($campos -join ', ')" -ForegroundColor Gray
            
            # Validar campos criticos
            $criticos = @("CREDITO","CUOTA","VENCIMIENTO","CAPITAL")
            $faltantes = $criticos | Where-Object { $campos -notcontains $_ }
            if ($faltantes.Count -eq 0) {
                Write-Host "Validacion: OK - Todos los campos presentes" -ForegroundColor Green
            } else {
                Write-Host "ADVERTENCIA: Campos faltantes: $($faltantes -join ', ')" -ForegroundColor Yellow
            }
        }
    }
    $test1OK = $true
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $test1OK = $false
}

# Test 2: Desglose Movimientos
Write-Host "`n--- TEST 2: Desglose Movimientos ---" -ForegroundColor White
$url2 = "$baseUrl/desglose_movimientos?token=$token" + [char]38 + "num_m=TEST123"
Write-Host "URL: $url2" -ForegroundColor Gray
try {
    $resp2 = Invoke-RestMethod -Uri $url2 -Method Get -TimeoutSec 15
    Write-Host "Estado: CONECTADO" -ForegroundColor Green
    
    if ($resp2.error) {
        Write-Host "Respuesta: Sin datos (esperado para TEST123)" -ForegroundColor Yellow
        Write-Host "Mensaje: $($resp2.error)" -ForegroundColor Gray
    } else {
        $campos2 = $resp2.PSObject.Properties.Name
        Write-Host "Campos: $($campos2 -join ', ')" -ForegroundColor Gray
        
        $criticos2 = @("suma_K","suma_I","suma_M","total_CC")
        $faltantes2 = $criticos2 | Where-Object { $campos2 -notcontains $_ }
        if ($faltantes2.Count -eq 0) {
            Write-Host "Validacion: OK - Todos los campos presentes" -ForegroundColor Green
        } else {
            Write-Host "ADVERTENCIA: Campos faltantes: $($faltantes2 -join ', ')" -ForegroundColor Yellow
        }
    }
    $test2OK = $true
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $test2OK = $false
}

# Test 3: Nombre Cliente
Write-Host "`n--- TEST 3: Nombre Cliente ---" -ForegroundColor White
$url3 = "$baseUrl/nombre_cliente?token=$token" + [char]38 + "cedula=0"
Write-Host "URL: $url3" -ForegroundColor Gray
try {
    $resp3 = Invoke-RestMethod -Uri $url3 -Method Get -TimeoutSec 15
    Write-Host "Estado: CONECTADO" -ForegroundColor Green
    
    if ($resp3.error) {
        Write-Host "Respuesta: Sin datos para cedula 0" -ForegroundColor Yellow
    } else {
        $campos3 = $resp3.PSObject.Properties.Name
        Write-Host "Campos: $($campos3 -join ', ')" -ForegroundColor Gray
        if ($campos3 -contains "NOMBRE") {
            Write-Host "Validacion: OK - Campo NOMBRE presente" -ForegroundColor Green
            if ($resp3.NOMBRE) {
                Write-Host "Nombre: $($resp3.NOMBRE)" -ForegroundColor Gray
            }
        } else {
            Write-Host "ADVERTENCIA: Campo NOMBRE no encontrado" -ForegroundColor Yellow
        }
    }
    $test3OK = $true
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $test3OK = $false
}

# Resumen
Write-Host "`n=== RESUMEN ===" -ForegroundColor Cyan
$total = 0
if ($test1OK) { $total++ }
if ($test2OK) { $total++ }
if ($test3OK) { $total++ }

Write-Host "Plan de Pagos: $(if($test1OK){'OK'}else{'FALLO'})" -ForegroundColor $(if($test1OK){'Green'}else{'Red'})
Write-Host "Desglose Movimientos: $(if($test2OK){'OK'}else{'FALLO'})" -ForegroundColor $(if($test2OK){'Green'}else{'Red'})
Write-Host "Nombre Cliente: $(if($test3OK){'OK'}else{'FALLO'})" -ForegroundColor $(if($test3OK){'Green'}else{'Red'})
Write-Host "`nResultado: $total/3 endpoints funcionando" -ForegroundColor $(if($total -eq 3){'Green'}elseif($total -gt 0){'Yellow'}else{'Red'})

if ($total -lt 3) {
    Write-Host "`nRECOMENDACIONES:" -ForegroundColor Yellow
    Write-Host "- Verificar conectividad de red al servidor: $baseUrl" -ForegroundColor Gray
    Write-Host "- Validar que el token no haya expirado" -ForegroundColor Gray
    Write-Host "- Confirmar que el servidor API este activo" -ForegroundColor Gray
}

Write-Host ""
