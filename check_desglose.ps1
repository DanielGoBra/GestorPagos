$token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyIjoiU29wb3J0ZSJ9.K2rQZuv4x_8UxsiNzZ6JP0VBGCTFrULxLC3zS44AnDw"
$raw = (Invoke-WebRequest -Uri "http://serv-xeo.inklusiva.co:8000/Plan_de_pagos_Final?token=$token&cedula=43884195" -TimeoutSec 30).Content
$json = $raw | ConvertFrom-Json
$pagada = $json | Where-Object { $_.ESTADO -eq "PAGADA" }

Write-Host "=== CUOTA PAGADA - cedula 43884195 ==="
Write-Host "CUO_CAP_PAGO: $($pagada.CUO_CAP_PAGO)"
Write-Host "CUO_INT_PAGO: $($pagada.CUO_INT_PAGO)"
Write-Host "CAR_CUO_PAGO: $($pagada.CAR_CUO_PAGO)"
Write-Host "ABONO_MORA:   $($pagada.ABONO_MORA)"
$total = [decimal]$pagada.CUO_CAP_PAGO + [decimal]$pagada.CUO_INT_PAGO + [decimal]$pagada.CAR_CUO_PAGO + [decimal]$pagada.ABONO_MORA
Write-Host "SUMA:         $total"
Write-Host "FECHA_PAGO:   $($pagada.FECHA_PAGO)"
Write-Host ""

# Ahora veamos el pago del banco
$env:PGPASSWORD=CAMBIAR
$q = 'SELECT "CedulaCliente","Monto","FechaConsignacion"::date,"CC" FROM "Pagos" WHERE "CedulaCliente"=''43884195'' AND "FechaConsignacion">=''2026-03-06'' LIMIT 3;'
Write-Host "=== PAGO DEL BANCO ==="
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h localhost -p 5432 -U usuario_pagos -d db_conciliacion -c $q
