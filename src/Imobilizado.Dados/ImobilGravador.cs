using System;
using System.Data.OleDb;
using System.Globalization;
using Imobilizado.Core.Dominio;

namespace Imobilizado.Dados
{
    /// <summary>
    /// Cadastro de bens no IMOBIL.DBF via VFPOLEDB: incluir, alterar e baixar.
    /// Datas vazias são gravadas como data em branco do VFP (DBNull) — não como
    /// 0001-01-01 — para não serem lidas como baixa nem pelo motor nem pelo Ju2.
    /// </summary>
    public sealed class ImobilGravador
    {
        private readonly string _pastaDados;
        private const string Tabela = "IMOBIL";

        public ImobilGravador(string pastaDados)
        {
            _pastaDados = pastaDados ?? throw new ArgumentNullException(nameof(pastaDados));
        }

        public bool Existe(string cod)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand($"SELECT COUNT(*) FROM {Tabela} WHERE COD=?", con))
            {
                cmd.Parameters.Add("cod", OleDbType.Char, 5).Value = (cod ?? "").Trim();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public void Incluir(BemEdicao b)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand { Connection = con })
            {
                // datas vazias entram como literal de data vazia do VFP (não como parâmetro,
                // que recusaria — campo NOT NULL). Parâmetros só para datas preenchidas.
                string dAquis = DataSql(cmd, "DATA_AQUIS", b.DataAquisicao);
                string dCorr = DataSql(cmd, "DATA_CORR", b.DataCorrecao);
                string dBaixa = DataSql(cmd, "DATA_BAIXA", b.DataBaixa);
                P(cmd, "COD", OleDbType.Char, 5, b.Codigo);
                P(cmd, "DESC", OleDbType.Char, 35, b.Descricao);
                P(cmd, "CONTAB", OleDbType.Char, 8, b.ContaImobilizado);
                P(cmd, "DEP_ACUM", OleDbType.Char, 8, b.ContaDepAcumulada);
                P(cmd, "RESULTADO", OleDbType.Char, 8, b.ContaResultado);
                PNum(cmd, "VAL_AQUIS", b.ValorAquisicao);
                PNum(cmd, "VAL_UFIR", b.BaseDepreciavel);
                PNum(cmd, "DEP_UFIR", b.DepreciacaoInicial);
                PNum(cmd, "VAL_BAIXA", b.ValorBaixa);
                // ordem dos ? deve casar com a ordem em que os parâmetros foram adicionados
                cmd.CommandText =
                    $"INSERT INTO {Tabela} (DATA_AQUIS, DATA_CORR, DATA_BAIXA, COD, DESC, CONTAB, " +
                    "DEP_ACUM, RESULTADO, VAL_AQUIS, VAL_UFIR, DEP_UFIR, VAL_BAIXA) VALUES (" +
                    $"{dAquis}, {dCorr}, {dBaixa}, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                cmd.ExecuteNonQuery();
            }
        }

        public void Alterar(BemEdicao b)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand { Connection = con })
            {
                string dAquis = DataSql(cmd, "DATA_AQUIS", b.DataAquisicao);
                string dCorr = DataSql(cmd, "DATA_CORR", b.DataCorrecao);
                string dBaixa = DataSql(cmd, "DATA_BAIXA", b.DataBaixa);
                P(cmd, "DESC", OleDbType.Char, 35, b.Descricao);
                P(cmd, "CONTAB", OleDbType.Char, 8, b.ContaImobilizado);
                P(cmd, "DEP_ACUM", OleDbType.Char, 8, b.ContaDepAcumulada);
                P(cmd, "RESULTADO", OleDbType.Char, 8, b.ContaResultado);
                PNum(cmd, "VAL_AQUIS", b.ValorAquisicao);
                PNum(cmd, "VAL_UFIR", b.BaseDepreciavel);
                PNum(cmd, "DEP_UFIR", b.DepreciacaoInicial);
                PNum(cmd, "VAL_BAIXA", b.ValorBaixa);
                P(cmd, "COD", OleDbType.Char, 5, b.Codigo);
                cmd.CommandText =
                    $"UPDATE {Tabela} SET DATA_AQUIS={dAquis}, DATA_CORR={dCorr}, DATA_BAIXA={dBaixa}, " +
                    "DESC=?, CONTAB=?, DEP_ACUM=?, RESULTADO=?, VAL_AQUIS=?, VAL_UFIR=?, DEP_UFIR=?, " +
                    "VAL_BAIXA=? WHERE COD=?";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Para data preenchida: adiciona parâmetro e devolve "?". Para data vazia: devolve o
        /// literal de data vazia do VFP (CTOD('')) e NÃO adiciona parâmetro.
        /// </summary>
        private static string DataSql(OleDbCommand cmd, string nome, DateTime? val)
        {
            if (!val.HasValue) return "CTOD('')";
            cmd.Parameters.Add(nome, OleDbType.Date).Value = val.Value;
            return "?";
        }

        /// <summary>Registra a baixa de um bem (data e valor).</summary>
        public void Baixar(string cod, DateTime dataBaixa, decimal valorBaixa)
        {
            using (var con = ConexaoVfp.Abrir(_pastaDados))
            using (var cmd = new OleDbCommand(
                $"UPDATE {Tabela} SET DATA_BAIXA=?, VAL_BAIXA=? WHERE COD=?", con))
            {
                PData(cmd, "DATA_BAIXA", dataBaixa);
                PNum(cmd, "VAL_BAIXA", valorBaixa);
                P(cmd, "COD", OleDbType.Char, 5, cod);
                cmd.ExecuteNonQuery();
            }
        }

        private static void Bind(OleDbCommand cmd, BemEdicao b)
        {
            P(cmd, "COD", OleDbType.Char, 5, b.Codigo);
            P(cmd, "DESC", OleDbType.Char, 35, b.Descricao);
            P(cmd, "CONTAB", OleDbType.Char, 8, b.ContaImobilizado);
            P(cmd, "DEP_ACUM", OleDbType.Char, 8, b.ContaDepAcumulada);
            P(cmd, "RESULTADO", OleDbType.Char, 8, b.ContaResultado);
            PNum(cmd, "VAL_AQUIS", b.ValorAquisicao);
            PData(cmd, "DATA_AQUIS", b.DataAquisicao);
            PData(cmd, "DATA_CORR", b.DataCorrecao);
            PNum(cmd, "VAL_UFIR", b.BaseDepreciavel);
            PNum(cmd, "DEP_UFIR", b.DepreciacaoInicial);
            PData(cmd, "DATA_BAIXA", b.DataBaixa);
            PNum(cmd, "VAL_BAIXA", b.ValorBaixa);
        }

        private static void P(OleDbCommand cmd, string nome, OleDbType tipo, int tam, string val)
            => cmd.Parameters.Add(nome, tipo, tam).Value = (val ?? "").Trim();

        private static void PNum(OleDbCommand cmd, string nome, decimal val)
            => cmd.Parameters.Add(nome, OleDbType.Numeric).Value = val;

        private static void PData(OleDbCommand cmd, string nome, DateTime? val)
            => cmd.Parameters.Add(nome, OleDbType.Date).Value = val.HasValue ? (object)val.Value : DBNull.Value;
    }
}
