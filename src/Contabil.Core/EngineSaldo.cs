using System;
using System.Collections.Generic;
using System.Globalization;
using Imobilizado.Core.Dbf;

namespace Contabil.Core
{
    /// <summary>
    /// Calcula o saldo das contas — port da fatia do balancete (FLT_BAL.Verifique_Sdos):
    ///
    ///   saldo(conta, até) = PLACON.SDO (âncora, na PLACON.DATA)
    ///                     + Σ débitos − Σ créditos do MOVFIN em (PLACON.DATA, até],
    ///       contando um movimento só se a CONTRAPARTIDA passar em check_conta.
    ///
    /// check_conta(contrapartida): conta se for vazia, ou conta ANALÍTICA
    /// (NUMCONTA[6:8] != "000"). (A exceção de contas financeiras oficiais do Clipper
    /// ainda não é replicada — não afeta as contas de produção/em-curso da apropriação.)
    ///
    /// Apelidos do MOVFIN ("CTA_PG # COELBA") são resolvidos via PLACON.DESC2 → NUMCONTA.
    /// </summary>
    public sealed class EngineSaldo
    {
        private readonly PlanoContas _plano;

        public EngineSaldo(PlanoContas plano)
        {
            _plano = plano ?? throw new ArgumentNullException(nameof(plano));
        }

        /// <summary>check_conta: a contrapartida habilita o movimento a contar no saldo?</summary>
        public bool CheckConta(string contrapartidaRaw)
        {
            var v = (contrapartidaRaw ?? "").Trim();
            if (v.Length == 0) return true;                 // contrapartida vazia conta
            var nc = _plano.Resolver(v);
            return PlanoContas.EhAnalitica(nc);             // só analítica conta
        }

        /// <summary>
        /// Saldo de todas as contas até <paramref name="dataLimite"/> ("YYYYMMDD"), numa
        /// única passada pelo MOVFIN. <paramref name="excluir"/> permite ignorar lançamentos
        /// (ex.: o próprio lote SIST_APROP do período, ou manuais de fechamento).
        /// </summary>
        public Dictionary<string, decimal> SaldosAte(string caminhoMovfin, string dataLimite,
            Func<string /*doc*/, string /*data*/, bool> excluir = null)
        {
            // inicia cada conta no seu SDO âncora
            var saldo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _plano.Contas) saldo[kv.Key] = kv.Value.Sdo;

            var dbf = new DbfReader(caminhoMovfin);
            foreach (var r in dbf.Registros())
            {
                var data = r["DATA"].Trim();
                if (data.Length < 8 || string.Compare(data, dataLimite, StringComparison.Ordinal) > 0) continue;
                if (excluir != null && excluir(r["DOC"].Trim(), data)) continue;

                var debRaw = r["DEBITO"];
                var credRaw = r["CREDITO"];
                var valor = Num(r["VALOR"]);
                var contaD = _plano.Resolver(debRaw);
                var contaC = _plano.Resolver(credRaw);

                // débito na conta: conta se a data > âncora da conta e a contrapartida (crédito) passa em check_conta
                if (contaD != null && _plano.Contas.TryGetValue(contaD, out var cd)
                    && MaiorQueAncora(data, cd.DataAncora) && CheckConta(credRaw))
                    saldo[contaD] += valor;

                if (contaC != null && _plano.Contas.TryGetValue(contaC, out var cc)
                    && MaiorQueAncora(data, cc.DataAncora) && CheckConta(debRaw))
                    saldo[contaC] -= valor;
            }
            return saldo;
        }

        /// <summary>Apuração de uma conta no período: VAL1 (saldo anterior), VAL2 (débitos), VAL3 (créditos).</summary>
        public struct Apuracao
        {
            public decimal Val1, Val2, Val3;
            public decimal SaldoFinal => Val1 + Val2 - Val3;     // saldo ao fim do período
            public decimal SaldoAnterior => Val1;
            public decimal MovimentoLiquidoCredor => Val3 - Val2; // p/ contas de receita (créditos - débitos)
            public bool TeveMovimento => Val2 != 0 || Val3 != 0;
        }

        /// <summary>
        /// Apura VAL1/VAL2/VAL3 de todas as contas para o período [data1, data2], numa passada
        /// pelo MOVFIN. Espelha FLT_BAL.Verifique_Sdos: VAL1 = SDO âncora + movimentos
        /// (âncora, data1); VAL2 = débitos [data1,data2]; VAL3 = créditos [data1,data2];
        /// sempre filtrando por check_conta(contrapartida).
        /// </summary>
        public Dictionary<string, Apuracao> ApurarPeriodo(string caminhoMovfin, string data1, string data2,
            Func<string, string, bool> excluir = null)
        {
            var ap = new Dictionary<string, Apuracao>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _plano.Contas)
                ap[kv.Key] = new Apuracao { Val1 = kv.Value.Sdo };

            var dbf = new DbfReader(caminhoMovfin);
            foreach (var r in dbf.Registros())
            {
                var data = r["DATA"].Trim();
                if (data.Length < 8 || string.Compare(data, data2, StringComparison.Ordinal) > 0) continue;
                if (excluir != null && excluir(r["DOC"].Trim(), data)) continue;

                var debRaw = r["DEBITO"];
                var credRaw = r["CREDITO"];
                var valor = Num(r["VALOR"]);
                var contaD = _plano.Resolver(debRaw);
                var contaC = _plano.Resolver(credRaw);
                bool antes = string.Compare(data, data1, StringComparison.Ordinal) < 0;

                if (contaD != null && _plano.Contas.TryGetValue(contaD, out var cd)
                    && MaiorQueAncora(data, cd.DataAncora) && CheckConta(credRaw))
                {
                    var a = ap[contaD];
                    if (antes) a.Val1 += valor; else a.Val2 += valor;
                    ap[contaD] = a;
                }
                if (contaC != null && _plano.Contas.TryGetValue(contaC, out var cc)
                    && MaiorQueAncora(data, cc.DataAncora) && CheckConta(debRaw))
                {
                    var a = ap[contaC];
                    if (antes) a.Val1 -= valor; else a.Val3 += valor;
                    ap[contaC] = a;
                }
            }
            return ap;
        }

        /// <summary>
        /// Saldos com ROLLUP das contas sintéticas: o saldo direto de cada conta é somado nela
        /// e em TODOS os seus ancestrais (definidos pela máscara 1.1.1.2.3 do número, não pelo
        /// GRAU — que vem em branco na maioria das contas). Equivale ao Mat_Sint do FLT_BAL.
        /// </summary>
        public Dictionary<string, decimal> SaldosComRollup(string caminhoMovfin, string dataLimite,
            Func<string, string, bool> excluir = null)
        {
            var direto = SaldosAte(caminhoMovfin, dataLimite, excluir);
            var resultado = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var nc in _plano.Contas.Keys) resultado[nc] = 0m;

            // só as folhas (analíticas) têm saldo próprio; sintéticas são puro rollup das filhas.
            foreach (var kv in _plano.Contas)
            {
                if (!HierarquiaContas.EhAnalitica(kv.Key)) continue;
                direto.TryGetValue(kv.Key, out var d);
                resultado[kv.Key] += d;
                foreach (var anc in HierarquiaContas.Ancestrais(kv.Key))
                    if (resultado.ContainsKey(anc)) resultado[anc] += d;
            }
            return resultado;
        }

        private static bool MaiorQueAncora(string data, string ancora)
            => string.IsNullOrEmpty(ancora) || string.Compare(data, ancora, StringComparison.Ordinal) > 0;

        private static decimal Num(string s)
            => decimal.TryParse((s ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
