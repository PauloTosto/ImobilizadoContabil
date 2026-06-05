using System;
using System.IO;

namespace Imobilizado.App
{
    /// <summary>Persistência mínima da última pasta de dados usada (em LocalApplicationData).</summary>
    internal static class ConfigApp
    {
        private static string Pasta =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImobilizadoContabil");

        private static string Arquivo => Path.Combine(Pasta, "config.txt");

        public static string CarregarPastaDados()
        {
            try { return File.Exists(Arquivo) ? File.ReadAllText(Arquivo).Trim() : ""; }
            catch { return ""; }
        }

        public static void SalvarPastaDados(string caminho)
        {
            try
            {
                Directory.CreateDirectory(Pasta);
                File.WriteAllText(Arquivo, caminho ?? "");
            }
            catch { /* não-crítico */ }
        }
    }
}
