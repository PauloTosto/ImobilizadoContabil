using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Recria o RELACIONA.DBF a partir de uma lista (importada da planilha PESQUISA) — espelha o
    /// ImportaRelaciona_Excel + CreateTabRelaciona do Contabil2020:
    ///   - valida NUMCONTA ÚNICO (entre os não-vazios);
    ///   - PULA linhas com NOVOCOD vazio;
    ///   - ORDENA por NOVOCOD; NREG sequencial (1..N);
    ///   - DDL idêntico: NREG INTEGER, NUMCONTA CHAR(8), NOVOCOD CHAR(30), DESCRICAO CHAR(40),
    ///     NOVADESC CHAR(50), REDUZIDO INTEGER.
    /// Faz BACKUP do RELACIONA.DBF atual antes de sobrescrever (e remove .CDX/.FPT p/ recriar limpo).
    /// </summary>
    public sealed class RelacionaGravador
    {
        private readonly string _pasta;
        private readonly string _tabela;

        public RelacionaGravador(string pastaDados, string tabela = "RELACIONA")
        {
            _pasta = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
            _tabela = tabela;
        }

        public sealed class Resultado
        {
            public int Gravados;
            public int PuladosSemNovocod;
            public string CaminhoBackup;
        }

        /// <summary>NUMCONTA (não-vazios) que aparecem mais de uma vez — vazio se OK.</summary>
        public List<string> NumcontasDuplicados(IEnumerable<ItemRelaciona> itens)
            => itens.Where(i => !string.IsNullOrWhiteSpace(i.NumConta))
                    .GroupBy(i => i.NumConta.Trim().ToUpperInvariant())
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

        /// <summary>Recria o RELACIONA.DBF. Lança InvalidOperationException se houver NUMCONTA duplicado.</summary>
        public Resultado Recriar(IReadOnlyList<ItemRelaciona> itens)
        {
            var dup = NumcontasDuplicados(itens);
            if (dup.Count > 0)
                throw new InvalidOperationException(
                    "NUMCONTA duplicado na planilha PESQUISA (corrija antes de importar): " +
                    string.Join(", ", dup.Take(10)) + (dup.Count > 10 ? $" (+{dup.Count - 10})" : ""));

            var dbf = Path.Combine(_pasta, _tabela + ".DBF");
            string backup = null;
            if (File.Exists(dbf))
            {
                backup = Path.Combine(_pasta, _tabela + "_bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".DBF");
                File.Copy(dbf, backup, true);
                foreach (var ext in new[] { ".DBF", ".CDX", ".FPT" })
                {
                    var f = Path.Combine(_pasta, _tabela + ext);
                    if (File.Exists(f)) File.Delete(f);
                }
            }

            var gravar = itens.Where(i => !string.IsNullOrWhiteSpace(i.NovoCod))
                              .OrderBy(i => i.NovoCod.Trim(), StringComparer.Ordinal)
                              .ToList();

            using (var con = ConexaoVfp.Abrir(_pasta))
            {
                using (var cmd = new OleDbCommand(
                    $"CREATE TABLE {_tabela} (NREG INTEGER, NUMCONTA CHAR(8), NOVOCOD CHAR(30), " +
                    "DESCRICAO CHAR(40), NOVADESC CHAR(50), REDUZIDO INTEGER)", con))
                    cmd.ExecuteNonQuery();

                using (var cmd = new OleDbCommand(
                    $"INSERT INTO {_tabela} (NREG, NUMCONTA, NOVOCOD, DESCRICAO, NOVADESC, REDUZIDO) " +
                    "VALUES (?,?,?,?,?,?)", con))
                {
                    var pNreg = cmd.Parameters.Add("NREG", OleDbType.Integer);
                    var pNum = cmd.Parameters.Add("NUMCONTA", OleDbType.Char, 8);
                    var pCod = cmd.Parameters.Add("NOVOCOD", OleDbType.Char, 30);
                    var pDesc = cmd.Parameters.Add("DESCRICAO", OleDbType.Char, 40);
                    var pNova = cmd.Parameters.Add("NOVADESC", OleDbType.Char, 50);
                    var pRed = cmd.Parameters.Add("REDUZIDO", OleDbType.Integer);

                    int nreg = 0;
                    foreach (var it in gravar)
                    {
                        nreg++;
                        pNreg.Value = nreg;
                        pNum.Value = Corta(it.NumConta, 8);
                        pCod.Value = Corta(it.NovoCod, 30);
                        pDesc.Value = Corta(it.Descricao, 40);
                        pNova.Value = Corta(it.NovaDesc, 50);
                        pRed.Value = it.Reduzido;
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return new Resultado
            {
                Gravados = gravar.Count,
                PuladosSemNovocod = itens.Count - gravar.Count,
                CaminhoBackup = backup
            };
        }

        private static string Corta(string s, int n)
        {
            s = (s ?? "").Trim();
            return s.Length > n ? s.Substring(0, n) : s;
        }
    }
}
