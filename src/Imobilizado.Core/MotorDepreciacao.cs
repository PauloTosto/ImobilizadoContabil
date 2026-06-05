using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Imobilizado.Core.Dominio;

namespace Imobilizado.Core
{
    /// <summary>
    /// Motor de depreciação — port fiel do comportamento do FLT_DEP (Clipper/Ju2),
    /// validado contra os lançamentos SIST_IMOB reais do MOVFIN.
    ///
    /// Regra (depreciação linear sobre custo):
    ///   quota_mensal = BaseDepreciavel * taxaAnualGrupo / 1200
    ///   acumulada(mês) = min(Base, DepreciacaoInicial + quota * meses_desde_partida)
    ///   quota_do_mês  = acumulada(mês) - acumulada(mês anterior)   // > 0 ⇒ gera
    ///
    /// Os bens são agregados pelo par (ContaResultado, ContaDepAcumulada): cada par
    /// vira UMA linha no MOVFIN com a soma das quotas.
    /// </summary>
    public sealed class MotorDepreciacao
    {
        private readonly Func<string, decimal> _taxaPorGrupo;

        /// <param name="taxaPorGrupo">
        /// Resolve a taxa anual (%) a partir da conta-grupo (grau 4). Ex.: "12265000" → 10m.
        /// Retornar 0 para grupos que não depreciam.
        /// </param>
        public MotorDepreciacao(Func<string, decimal> taxaPorGrupo)
        {
            _taxaPorGrupo = taxaPorGrupo ?? throw new ArgumentNullException(nameof(taxaPorGrupo));
        }

        /// <summary>Calcula a quota mensal cheia de um bem (sem considerar saldo/limite).</summary>
        public decimal QuotaMensal(Bem bem)
        {
            var taxa = _taxaPorGrupo(bem.ContaGrupo());
            if (bem.BaseDepreciavel <= 0 || taxa <= 0) return 0m;
            return bem.BaseDepreciavel * taxa / 1200m;
        }

        /// <summary>Depreciação acumulada projetada até o fim de <paramref name="ate"/> (limitada à base).</summary>
        private decimal Acumulada(Bem bem, AnoMes partida, decimal quota, AnoMes ate)
        {
            int meses = Math.Max(0, ate.MesesDesde(partida));
            var bruto = bem.DepreciacaoInicial + quota * meses;
            return Math.Min(bem.BaseDepreciavel, bruto);
        }

        /// <summary>Quota do mês de um único bem (0 se baixado, esgotado ou sem taxa).</summary>
        public decimal QuotaDoMes(Bem bem, AnoMes competencia)
        {
            var quota = QuotaMensal(bem);
            if (quota <= 0) return 0m;

            var partida = bem.DataPartida;
            if (partida == null) return 0m;

            // Bem baixado antes da competência não deprecia mais.
            if (bem.DataBaixa is AnoMes baixa && baixa.EhAnteriorA(competencia)) return 0m;

            var depAte = Acumulada(bem, partida.Value, quota, competencia);
            var depAntes = Acumulada(bem, partida.Value, quota, competencia.MesAnterior());
            var cur = depAte - depAntes;
            return cur > 0 ? cur : 0m;
        }

        /// <summary>
        /// Gera os lançamentos de depreciação de uma competência, já agregados por par de contas.
        /// </summary>
        public IReadOnlyList<LancamentoDepreciacao> GerarLancamentos(IEnumerable<Bem> bens, AnoMes competencia)
        {
            // soma das quotas (em alta precisão) por par (RESULTADO, DEP_ACUM)
            var porPar = new Dictionary<(string deb, string cred), decimal>();
            foreach (var bem in bens)
            {
                var cur = QuotaDoMes(bem, competencia);
                if (cur <= 0) continue;
                var chave = (bem.ContaResultado, bem.ContaDepAcumulada);
                porPar.TryGetValue(chave, out var acc);
                porPar[chave] = acc + cur;
            }

            var data = competencia.UltimoDiaDbf();
            return porPar
                .OrderBy(kv => kv.Key.cred).ThenBy(kv => kv.Key.deb)
                .Select(kv => new LancamentoDepreciacao
                {
                    Data = data,
                    Debito = kv.Key.deb,
                    Credito = kv.Key.cred,
                    Valor = Math.Round(kv.Value, 2, MidpointRounding.AwayFromZero),
                    Historico = MontaHistorico(kv.Value),
                })
                .ToList();
        }

        /// <summary>
        /// Histórico no formato legado: "REF.DEPREC" + valor em 4 casas (largura 11) + " UFIRS".
        /// O VALOR vai arredondado a 2 casas, mas o histórico carrega 4 — igual ao Ju2.
        /// </summary>
        private static string MontaHistorico(decimal valorPreciso)
        {
            var num = Math.Round(valorPreciso, 4, MidpointRounding.AwayFromZero)
                          .ToString("F4", CultureInfo.InvariantCulture)
                          .PadLeft(11);
            return "REF.DEPREC" + num + " UFIRS";
        }
    }
}
