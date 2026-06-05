using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Abre o FrmGrupoComposto com um grupo real e captura a janela (PrintWindow) para um PNG,
    /// para inspeção visual do layout (ex.: o botão Salvar aparece?). Aponta para CÓPIA.
    /// Uso: --capturacomposto &lt;pasta&gt; &lt;movid&gt; &lt;pngpath&gt;
    /// </summary>
    internal static class CapturaComposto
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        public static void Rodar(string pasta, decimal movId, string png)
        {
            var g = new MovfinGravador(pasta);
            var plano = Contabil.Core.PlanoContas.Carregar(System.IO.Path.Combine(pasta, "placon.DBF"));
            var amostra = g.LerPeriodo("20200101", "20301231", null).FirstOrDefault(l => l.MovId == movId && l.OutroId == 0);
            if (amostra == null) { Console.WriteLine($"Mestre {movId} não achado."); return; }
            var grupo = g.LerGrupo(movId, amostra.Data);

            var f = new FrmGrupoComposto(grupo, plano);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();

            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var gr = Graphics.FromImage(bmp))
                {
                    var hdc = gr.GetHdc();
                    PrintWindow(f.Handle, hdc, 0);
                    gr.ReleaseHdc(hdc);
                }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"Capturado em {png} ({f.Width}x{f.Height})");
            f.Close();
        }
    }
}
