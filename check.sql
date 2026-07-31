SELECT "CedulaCliente","Monto","FechaConsignacion"::date,"CC" FROM "Pagos" WHERE "CedulaCliente"='1000000000' AND "FechaConsignacion">='2026-03-06' LIMIT 3;
