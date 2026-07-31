using System.Threading.Tasks;

namespace GestorPagos.Core.Services
{
    public interface IImportadorService
    {
        Task<ImportResult> ImportarArchivoAsync(string rutaArchivo);
    }
}
