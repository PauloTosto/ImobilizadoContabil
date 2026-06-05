using System.Data.OleDb;

namespace Imobilizado.Dados
{
    /// <summary>Abre conexão VFPOLEDB para um diretório de DBFs (free tables).</summary>
    internal static class ConexaoVfp
    {
        public static OleDbConnection Abrir(string pastaDados)
        {
            var con = new OleDbConnection($"Provider=VFPOLEDB.1;Data Source={pastaDados}");
            con.Open();
            return con;
        }
    }
}
