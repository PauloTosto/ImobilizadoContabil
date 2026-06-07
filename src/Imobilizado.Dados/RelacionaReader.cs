using System;
using System.Collections.Generic;
using System.Data.OleDb;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Item do de-para RELACIONA.DBF: NUMCONTA (8 díg., chave) → código no AlterData.
    /// Colunas: NREG(I), NUMCONTA(C,8), NOVOCOD(C,30), DESCRICAO(C,40), NOVADESC(C,50), REDUZIDO(I).
    /// </summary>
    public sealed class ItemRelaciona
    {
        public string NumConta;   // chave (8 díg.)
        public string NovoCod;    // código contábil (alternativa/debug)
        public string Descricao;
        public string NovaDesc;
        public int Reduzido;      // código REDUZIDO do AlterData (modo padrão de export)
    }

    /// <summary>
    /// Lê RELACIONA.DBF (de-para NUMCONTA → REDUZIDO/NOVOCOD) usado para traduzir as contas
    /// do MOVFIN para os códigos que o AlterData espera no lote de importação.
    /// Espelha o dictrelaciona do FrmRelaciona.PesquiseRazao do Contabil2020.
    /// NUMCONTA precisa ser ÚNICO — duplicata é erro (igual ao Contabil2020).
    /// </summary>
    public sealed class RelacionaReader
    {
        private readonly string _pasta;
        private readonly string _tabela;

        public RelacionaReader(string pastaDados, string tabela = "RELACIONA")
        {
            _pasta = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
            _tabela = tabela;
        }

        /// <summary>Diagnóstico cru: total de registros, quantos têm NUMCONTA, e os dados de uma conta específica.</summary>
        public string Diagnostico(string contaAlvo)
        {
            var sb = new System.Text.StringBuilder();
            using (var con = ConexaoVfp.Abrir(_pasta))
            {
                using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {_tabela}", con))
                    sb.AppendLine($"Total de registros em {_tabela}: {cmd.ExecuteScalar()}");
                using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {_tabela} WHERE NUMCONTA<>''", con))
                    sb.AppendLine($"  com NUMCONTA preenchido: {cmd.ExecuteScalar()}");
                using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {_tabela} WHERE NUMCONTA=?", con))
                {
                    cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (contaAlvo ?? "").Trim();
                    sb.AppendLine($"  registros com NUMCONTA='{contaAlvo}': {cmd.ExecuteScalar()}");
                }
                using (var cmd = new OleDbCommand($"SELECT NUMCONTA, REDUZIDO, NOVOCOD, DESCRICAO FROM {_tabela} WHERE NUMCONTA=?", con))
                {
                    cmd.Parameters.Add("nc", OleDbType.Char, 8).Value = (contaAlvo ?? "").Trim();
                    using (var rd = cmd.ExecuteReader())
                        while (rd.Read())
                            sb.AppendLine($"    -> NUMCONTA='{rd[0]}' REDUZIDO={rd[1]} NOVOCOD='{rd[2]}' DESC='{rd[3]}'");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Carrega o de-para num dicionário NUMCONTA(trim) → ItemRelaciona.
        /// </summary>
        /// <param name="duplicados">NUMCONTAs duplicados encontrados (vazio = ok).</param>
        public Dictionary<string, ItemRelaciona> Carregar(out List<string> duplicados)
        {
            var mapa = new Dictionary<string, ItemRelaciona>(StringComparer.OrdinalIgnoreCase);
            var dups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var con = ConexaoVfp.Abrir(_pasta))
            using (var cmd = new OleDbCommand(
                $"SELECT NUMCONTA, NOVOCOD, DESCRICAO, NOVADESC, REDUZIDO FROM {_tabela}", con))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    var nc = (rd["NUMCONTA"] == DBNull.Value ? "" : Convert.ToString(rd["NUMCONTA"])).Trim();
                    if (nc.Length == 0) continue;

                    var item = new ItemRelaciona
                    {
                        NumConta = nc,
                        NovoCod = (rd["NOVOCOD"] == DBNull.Value ? "" : Convert.ToString(rd["NOVOCOD"])).Trim(),
                        Descricao = (rd["DESCRICAO"] == DBNull.Value ? "" : Convert.ToString(rd["DESCRICAO"])).Trim(),
                        NovaDesc = (rd["NOVADESC"] == DBNull.Value ? "" : Convert.ToString(rd["NOVADESC"])).Trim(),
                        Reduzido = (rd["REDUZIDO"] == DBNull.Value ? 0 : Convert.ToInt32(rd["REDUZIDO"]))
                    };

                    if (mapa.ContainsKey(nc)) dups.Add(nc);
                    else mapa[nc] = item;
                }
            }

            duplicados = new List<string>(dups);
            return mapa;
        }
    }
}
