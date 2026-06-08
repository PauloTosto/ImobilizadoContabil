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

        /// <summary>Captura o FrmLancamentoComposto (incluir) com principal + 1 contrapartida, p/ ver a diferença.</summary>
        public static void RodarLancComposto(string png)
        {
            var f = new FrmLancamentoComposto(null, null, null);
            var _h = f.Handle;
            T Campo<T>(string nome) => (T)typeof(FrmLancamentoComposto).GetField(nome, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
            Campo<TextBox>("txtPrincipal").Text = "CTA_PG # FORNECEDOR";
            Campo<TextBox>("txtPrincipalValor").Text = "1.000,00";
            var dg = Campo<DataGridView>("dgvLinhas");
            dg.Rows.Add("INSUMO # ADUBO", "600,00", "Compra adubo NF 123");
            typeof(FrmLancamentoComposto).GetMethod("AtualizaTotal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var gr = Graphics.FromImage(bmp)) { var hdc = gr.GetHdc(); PrintWindow(f.Handle, hdc, 0); gr.ReleaseHdc(hdc); }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
        }

        /// <summary>Captura o FrmImportaRelaciona com uma planilha PESQUISA carregada.</summary>
        public static void RodarImportaRelaciona(string pasta, string xlsx, string png)
        {
            var f = new FrmImportaRelaciona();
            var _h = f.Handle;
            ((TextBox)typeof(FrmImportaRelaciona).GetField("txtPasta", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f)).Text = pasta;
            f.CarregarArquivo(xlsx);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var gr = Graphics.FromImage(bmp)) { var hdc = gr.GetHdc(); PrintWindow(f.Handle, hdc, 0); gr.ReleaseHdc(hdc); }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
        }

        /// <summary>Captura o FrmPrincipal (menu) para conferência visual.</summary>
        public static void RodarPrincipal(string png)
        {
            var f = new FrmPrincipal();
            var _h = f.Handle;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var gr = Graphics.FromImage(bmp)) { var hdc = gr.GetHdc(); PrintWindow(f.Handle, hdc, 0); gr.ReleaseHdc(hdc); }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
        }

        /// <summary>Captura a tela FrmLancamentos carregada com um período, para conferir o filtro de contabilidade.</summary>
        public static void RodarLanc(string pasta, string d1, string d2, string png)
        {
            var f = new FrmLancamentos();
            var _h = f.Handle;
            T Campo<T>(string nome) => (T)typeof(FrmLancamentos).GetField(nome, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
            Campo<TextBox>("txtPasta").Text = pasta;
            Campo<DateTimePicker>("dtDe").Value = new DateTime(int.Parse(d1.Substring(0, 4)), int.Parse(d1.Substring(4, 2)), int.Parse(d1.Substring(6, 2)));
            Campo<DateTimePicker>("dtAte").Value = new DateTime(int.Parse(d2.Substring(0, 4)), int.Parse(d2.Substring(4, 2)), int.Parse(d2.Substring(6, 2)));
            if (Environment.GetEnvironmentVariable("CAP_ABERTOS") == "1") Campo<CheckBox>("chkAbertos").Checked = true;
            if (Environment.GetEnvironmentVariable("CAP_TRANSF") == "1") Campo<CheckBox>("chkTransf").Checked = true;
            typeof(FrmLancamentos).GetMethod("Carregar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
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
            Console.WriteLine($"Capturado em {png}");
            f.Close();
        }

        /// <summary>Captura o FrmExportaAlterData já com o lote preparado, p/ conferência visual.</summary>
        public static void RodarExportaAlterData(string pasta, string d1, string d2, string png)
        {
            var f = new FrmExportaAlterData();
            var _h = f.Handle;
            T Campo<T>(string nome) => (T)typeof(FrmExportaAlterData).GetField(nome, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
            Campo<TextBox>("txtPasta").Text = pasta;
            Campo<DateTimePicker>("dtDe").Value = new DateTime(int.Parse(d1.Substring(0, 4)), int.Parse(d1.Substring(4, 2)), int.Parse(d1.Substring(6, 2)));
            Campo<DateTimePicker>("dtAte").Value = new DateTime(int.Parse(d2.Substring(0, 4)), int.Parse(d2.Substring(4, 2)), int.Parse(d2.Substring(6, 2)));
            typeof(FrmExportaAlterData).GetMethod("Preparar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var gr = Graphics.FromImage(bmp)) { var hdc = gr.GetHdc(); PrintWindow(f.Handle, hdc, 0); gr.ReleaseHdc(hdc); }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine($"Capturado em {png}");
            f.Close();
        }

        /// <summary>Captura o FrmBalancete já calculado, para conferência visual.</summary>
        public static void RodarBalancete(string pasta, string d1, string d2, string png)
        {
            var f = new FrmBalancete();
            var _h = f.Handle;
            T Campo<T>(string nome) => (T)typeof(FrmBalancete).GetField(nome, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
            Campo<TextBox>("txtPasta").Text = pasta;
            Campo<DateTimePicker>("dtDe").Value = new DateTime(int.Parse(d1.Substring(0, 4)), int.Parse(d1.Substring(4, 2)), int.Parse(d1.Substring(6, 2)));
            Campo<DateTimePicker>("dtAte").Value = new DateTime(int.Parse(d2.Substring(0, 4)), int.Parse(d2.Substring(4, 2)), int.Parse(d2.Substring(6, 2)));
            typeof(FrmBalancete).GetMethod("Calcular", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
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
            Console.WriteLine($"Capturado em {png}");
            f.Close();
        }
    }
}
