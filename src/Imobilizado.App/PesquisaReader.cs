using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Imobilizado.Dados;
using OfficeOpenXml;

namespace Imobilizado.App
{
    /// <summary>
    /// Lê a planilha <b>PESQUISA</b> de um .xlsx (o de-para que alimenta o RELACIONA.DBF do AlterData).
    /// Espelha a exigência do Contabil2020: a planilha DENTRO do arquivo tem que se chamar PESQUISA e
    /// conter as colunas NUMCONTA, NOVOCOD, NOVADESC, REDUZIDO (DESCRICAO é opcional).
    /// </summary>
    public static class PesquisaReader
    {
        public const string NomePlanilha = "PESQUISA";

        public static List<ItemRelaciona> Ler(string caminhoXlsx)
        {
            var fi = new FileInfo(caminhoXlsx);
            if (!fi.Exists) throw new FileNotFoundException("Arquivo não encontrado: " + caminhoXlsx);

            using (var pkg = new ExcelPackage(fi))
            {
                var ws = pkg.Workbook.Worksheets[NomePlanilha];
                if (ws == null)
                    throw new InvalidOperationException(
                        $"A planilha precisa se chamar '{NomePlanilha}'. Encontradas: " +
                        string.Join(", ", pkg.Workbook.Worksheets.Select(w => w.Name)));
                if (ws.Dimension == null) throw new InvalidOperationException("Planilha PESQUISA vazia.");

                // mapeia cabeçalhos da linha 1
                var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    var h = (ws.Cells[1, c].Text ?? "").Trim();
                    if (h.Length > 0 && !col.ContainsKey(h)) col[h] = c;
                }
                foreach (var req in new[] { "NUMCONTA", "NOVOCOD", "NOVADESC", "REDUZIDO" })
                    if (!col.ContainsKey(req))
                        throw new InvalidOperationException("Coluna obrigatória ausente na PESQUISA: " + req);
                bool temDesc = col.ContainsKey("DESCRICAO");

                var itens = new List<ItemRelaciona>();
                for (int r = 2; r <= ws.Dimension.End.Row; r++)
                {
                    string Cel(string nome) => col.ContainsKey(nome) ? (ws.Cells[r, col[nome]].Text ?? "").Trim() : "";
                    var num = Cel("NUMCONTA");
                    var cod = Cel("NOVOCOD");
                    if (num.Length == 0 && cod.Length == 0) continue;   // linha vazia

                    int red = 0;
                    var vRed = ws.Cells[r, col["REDUZIDO"]].Value;
                    if (vRed != null) { try { red = Convert.ToInt32(vRed, CultureInfo.InvariantCulture); } catch { red = 0; } }

                    itens.Add(new ItemRelaciona
                    {
                        NumConta = num,
                        NovoCod = cod,
                        Descricao = temDesc ? Cel("DESCRICAO") : "",
                        NovaDesc = Cel("NOVADESC"),
                        Reduzido = red
                    });
                }
                return itens;
            }
        }
    }
}
