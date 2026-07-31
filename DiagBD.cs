// Script de diagnóstico para consultar la BD
using System;
using Npgsql;

class Program
{
    static void Main()
    {
        var connStr = "Host=localhost;Database=db_conciliacion;Username=usuario;Password=CAMBIAR";
        
        using var conn = new NpgsqlConnection(connStr);
        conn.Open();
        
        Console.WriteLine("=== DIAGNÓSTICO BD PAGOS ===\n");
        
        // Total pagos
        using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Pagos\"", conn))
        {
            Console.WriteLine($"Total pagos en BD: {cmd.ExecuteScalar()}");
        }
        
        // Pagos bloqueados
        using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Pagos\" WHERE \"Bloqueado\" = true", conn))
        {
            Console.WriteLine($"Pagos BLOQUEADOS: {cmd.ExecuteScalar()}");
        }
        
        // Pagos por estado
        using (var cmd = new NpgsqlCommand(@"
            SELECT ""Estado"", COUNT(*) 
            FROM ""Pagos"" 
            GROUP BY ""Estado"" 
            ORDER BY COUNT(*) DESC", conn))
        {
            Console.WriteLine("\nPagos por ESTADO:");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  - Estado {reader[0]}: {reader[1]}");
            }
        }
        
        // Pagos por fecha
        using (var cmd = new NpgsqlCommand(@"
            SELECT DATE(""FechaConsignacion""), COUNT(*) 
            FROM ""Pagos"" 
            GROUP BY DATE(""FechaConsignacion"") 
            ORDER BY DATE(""FechaConsignacion"")", conn))
        {
            Console.WriteLine("\nPagos por FECHA CONSIGNACIÓN:");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  - {reader[0]:yyyy-MM-dd}: {reader[1]} pagos");
            }
        }
        
        // Pagos con desglose (Capital > 0)
        using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Pagos\" WHERE \"Capital\" > 0", conn))
        {
            Console.WriteLine($"\nPagos CON DESGLOSE (Capital > 0): {cmd.ExecuteScalar()}");
        }
        
        // Pagos con CC
        using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Pagos\" WHERE \"CC\" IS NOT NULL AND \"CC\" != ''", conn))
        {
            Console.WriteLine($"Pagos CON CC: {cmd.ExecuteScalar()}");
        }
        
        // Muestra de pagos con CC pero sin desglose
        using (var cmd = new NpgsqlCommand(@"
            SELECT ""CC"", ""Capital"", ""TotalAplicado"", ""Bloqueado""
            FROM ""Pagos"" 
            WHERE ""CC"" IS NOT NULL AND ""CC"" != '' 
            LIMIT 5", conn))
        {
            Console.WriteLine("\nMuestra pagos CON CC:");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  CC={reader[0]}, Capital={reader[1]}, TotalAplic={reader[2]}, Bloq={reader[3]}");
            }
        }
        
        Console.WriteLine("\n=== FIN DIAGNÓSTICO ===");
    }
}
