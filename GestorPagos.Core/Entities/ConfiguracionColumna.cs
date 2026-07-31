namespace GestorPagos.Core.Entities
{
    public class ConfiguracionColumna
    {
        public int Id { get; set; }
        public string Nombre_Campo { get; set; } = string.Empty;
        public int Indice_Columna { get; set; }
        public bool Es_Requerido { get; set; }
        public string Formato_Excel { get; set; } = string.Empty;
    }
}
