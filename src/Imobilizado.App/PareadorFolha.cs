using System;
using System.Collections.Generic;
using System.Linq;
using Contabil.Core;
using Imobilizado.Dados;

namespace Imobilizado.App
{
    /// <summary>
    /// Pareia a FOLHA solta do SIST_RURAL — porte fiel do FOLHA_TentaAdvinhar do Contabil2020
    /// (a lógica ANTIGA, p/ BATER com o que já foi exportado/lançado pré-corte; a SP nova faz o
    /// equivalente na origem de 01/mai/2026 em diante). Usado tanto no export AlterData quanto no
    /// balancete (p/ ficar compatível com o Contabil2020 nos períodos fechados).
    ///
    /// Por dia de folha:
    ///  - agregados soltos: "DESC. INSS TRAB." (C=IMP#INSS, total INSS), "FOLHA (LIQ.) TRAB."
    ///    (C=PROV#FOLHA, líquido total), "DESC. IRF TRAB." (C=CTA_PG#IRF, só ≥2023),
    ///    "FOLHA SAL. FAMILIA" (D=IMP#INSS), e os "FOLHA TRAB RURAL" (D=centro, bruto).
    ///  - rateia o INSS líquido (INSS − salfam) proporcional ao bruto de cada centro → gera
    ///    D=centro/C=IMP#INSS; retifica o centro p/ D=centro/C=PROV#FOLHA com o líquido;
    ///    IRF vai pro MAIOR centro; salário-família vira D=IMP#INSS/C=IMP#INSS (auto-anula);
    ///    apaga os agregados DESC.INSS e FOLHA(LIQ.).
    /// Guarda contra dia incompleto (pula sem quebrar). Casa centro↔INSS por referência.
    /// </summary>
    public sealed class PareadorFolha
    {
        /// <summary>
        /// Data de corte (exclusiva) do pareamento da folha: ANTES dela a folha é solta (meias-entradas)
        /// e precisa ser pareada aqui (emulando o FrmRazão antigo); A PARTIR dela (01/mai/2026) a folha
        /// JÁ NASCE PAREADA na origem pela SP nova (CLT_GERA_Trabalhador_MOVFIN_PAREADA, com a lógica
        /// CORRIGIDA do salário-família) — então NÃO pode ser re-pareada aqui.
        /// </summary>
        public const string DataCorteFolha = "20260501";

        private readonly PlanoContas _plano;

        public PareadorFolha(PlanoContas plano)
        {
            _plano = plano ?? throw new ArgumentNullException(nameof(plano));
        }

        private static bool Estrela(string s) => s != null && s.TrimStart().StartsWith("*");

        /// <summary>
        /// Pareia a folha. <paramref name="dataCorteExclusiva"/> ("YYYYMMDD"): se informado, só
        /// pareia os dias estritamente ANTERIORES a essa data (ex.: "20260501" = só pré-maio;
        /// de maio em diante a folha já nasce pareada na origem). null = pareia todos os dias.
        /// </summary>
        public List<LancamentoMovfin> Parear(List<LancamentoMovfin> orig, string dataCorteExclusiva = null)
        {
            bool EhBanco2(string s) => s != null && s.Trim().Length == 2 && _plano.BancoPorNumero(s.Trim()) != null;
            bool Cont(LancamentoMovfin r, string sub) => (r.Historico ?? "").IndexOf(sub, StringComparison.Ordinal) >= 0;
            const string DOCF = "SIST_RURAL NW";

            var creditoVazio = orig.Where(r => !EhBanco2(r.Debito) && string.IsNullOrWhiteSpace(r.Credito)
                && !Estrela(r.Debito) && !string.IsNullOrWhiteSpace(r.Debito) && (r.Doc ?? "").Trim() == DOCF).ToList();
            var debitoVazio = orig.Where(r => !EhBanco2(r.Credito) && string.IsNullOrWhiteSpace(r.Debito)
                && !Estrela(r.Credito) && !string.IsNullOrWhiteSpace(r.Credito) && (r.Doc ?? "").Trim() == DOCF).ToList();

            var removidos = new HashSet<LancamentoMovfin>();
            var novos = new List<LancamentoMovfin>();

            foreach (var dia in debitoVazio.Select(r => r.Data).Distinct().ToList())
            {
                // respeita o corte: só pareia datas anteriores ao corte (pós-corte já vem pareado da origem)
                if (dataCorteExclusiva != null && string.Compare(dia, dataCorteExclusiva, StringComparison.Ordinal) >= 0) continue;

                var regSalFam = creditoVazio.FirstOrDefault(r => r.Data == dia && Cont(r, "FOLHA SAL. FAMILIA"));
                var regProvINSS = debitoVazio.FirstOrDefault(r => r.Data == dia && Cont(r, "DESC. INSS TRAB."));
                int ano = int.Parse(dia.Substring(0, 4));
                LancamentoMovfin regIrFonte = null; decimal valIR = 0m;
                if (ano >= 2023)
                {
                    regIrFonte = debitoVazio.FirstOrDefault(r => r.Data == dia && Cont(r, "DESC. IRF TRAB."));
                    if (regIrFonte != null) valIR = regIrFonte.Valor;
                }
                var rowProvFolha = debitoVazio.FirstOrDefault(r => r.Data == dia && Cont(r, "FOLHA"));
                var lancFazendas = creditoVazio.Where(r => r.Data == dia && Cont(r, "FOLHA TRAB RURAL")).ToList();

                // dia incompleto → não pareia (deixa as meias-entradas como estão)
                if (regSalFam == null || regProvINSS == null || rowProvFolha == null || lancFazendas.Count == 0) continue;

                // base do rateio = Σ bruto − IRF (o IRF sai da base; vai pro maior centro à parte)
                decimal somalanc = lancFazendas.Sum(r => r.Valor);
                if (valIR > 0) somalanc -= valIR;
                if (somalanc == 0) continue;
                var maxFaz = (valIR > 0) ? lancFazendas.OrderByDescending(r => r.Valor).FirstOrDefault() : null;
                decimal valoraRatear = regProvINSS.Valor - regSalFam.Valor;

                var fazToInss = new Dictionary<LancamentoMovfin, LancamentoMovfin>();
                foreach (var faz in lancFazendas)
                {
                    if (faz.Valor == 0m) { removidos.Add(faz); continue; }
                    decimal vIRF = (maxFaz != null && ReferenceEquals(maxFaz, faz)) ? valIR : 0m;
                    // INSS rateia sobre o BRUTO (o IRF só sai do líquido do maior centro, na retificação)
                    decimal valLanc = Math.Round(faz.Valor / somalanc * valoraRatear, 2);
                    if (valLanc <= 0m) continue;
                    fazToInss[faz] = new LancamentoMovfin
                    {
                        Recno = 0, MovId = faz.MovId, OutroId = 0, Data = faz.Data, Doc = faz.Doc,
                        Debito = faz.Debito, Credito = regProvINSS.Credito, Valor = valLanc,
                        Historico = regSalFam.Historico, DocFisc = faz.DocFisc, Forn = faz.Forn
                    };
                }
                // ajuste de arredondamento: joga a diferença no MAIOR rateio
                decimal somaRateado = fazToInss.Values.Sum(r => r.Valor);
                if (somaRateado != valoraRatear && fazToInss.Count > 0)
                {
                    var rmax = fazToInss.Values.OrderByDescending(r => r.Valor).First();
                    rmax.Valor = rmax.Valor - (valoraRatear - somaRateado);
                }
                // retifica os centros: líquido = (bruto − IRF) − INSS; contrapartida = PROV#FOLHA
                foreach (var faz in lancFazendas)
                {
                    if (!fazToInss.TryGetValue(faz, out var inss)) continue;
                    decimal vIRF = (maxFaz != null && ReferenceEquals(maxFaz, faz)) ? valIR : 0m;
                    faz.Valor = (faz.Valor - vIRF) - inss.Valor;
                    faz.Credito = rowProvFolha.Credito;
                }
                novos.AddRange(fazToInss.Values);

                if (regIrFonte != null && maxFaz != null)
                {
                    // o IRF NÃO é subtraído de novo do centro (a retificação acima já tirou o IRF do
                    // líquido do maior centro, uma única vez). O IRF vira só mais um débito ao centro:
                    regIrFonte.Debito = maxFaz.Debito;       // IRF: D=maior centro / C=CTA_PG#IRF
                    regIrFonte.Valor = valIR;
                }
                regSalFam.Credito = regProvINSS.Credito;     // salário-família: D=IMP#INSS / C=IMP#INSS (auto-anula)
                removidos.Add(regProvINSS);                  // apaga agregado INSS
                removidos.Add(rowProvFolha);                 // apaga agregado FOLHA(LIQ.)
            }

            var result = orig.Where(r => !removidos.Contains(r)).ToList();
            result.AddRange(novos);
            return result;
        }
    }
}
