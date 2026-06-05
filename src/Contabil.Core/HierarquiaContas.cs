using System;
using System.Collections.Generic;

namespace Contabil.Core
{
    /// <summary>
    /// Hierarquia do plano de contas pela ESTRUTURA DO NÚMERO (máscara 1.1.1.2.3 — prefixos
    /// cumulativos de 1,2,3,5,8 dígitos). É a fonte confiável: o campo GRAU do placon vem
    /// em branco na maioria das contas. O nível e a conta-pai são deduzidos do número.
    /// </summary>
    public static class HierarquiaContas
    {
        // comprimento do prefixo significativo por nível (índice 1..5)
        private static readonly int[] Prefixo = { 0, 1, 2, 3, 5, 8 };

        /// <summary>Nível 1..5 de uma conta, pela posição do último dígito significativo.</summary>
        public static int Nivel(string numConta)
        {
            var nc = (numConta ?? "").Trim();
            if (nc.Length != 8) return 0;
            int ultimo = 0;
            for (int i = 0; i < 8; i++) if (nc[i] != '0') ultimo = i + 1;
            if (ultimo <= 1) return 1;
            if (ultimo == 2) return 2;
            if (ultimo == 3) return 3;
            if (ultimo <= 5) return 4;
            return 5;
        }

        /// <summary>Analítica = nível 5 (posições 6-8 significativas).</summary>
        public static bool EhAnalitica(string numConta) => Nivel(numConta) == 5;

        /// <summary>Número da conta-pai (um nível acima), ou null se for nível 1.</summary>
        public static string ContaPai(string numConta)
        {
            var nc = (numConta ?? "").Trim();
            int nivel = Nivel(nc);
            if (nivel <= 1) return null;
            int pref = Prefixo[nivel - 1];
            return nc.Substring(0, pref) + new string('0', 8 - pref);
        }

        /// <summary>Todos os ancestrais (pai, avô, ...) até o nível 1.</summary>
        public static IEnumerable<string> Ancestrais(string numConta)
        {
            var p = ContaPai(numConta);
            while (p != null) { yield return p; p = ContaPai(p); }
        }

        /// <summary>Grau derivado do número (1..5), como string — para gravar no placon.</summary>
        public static string GrauDerivado(string numConta) => Nivel(numConta).ToString();

        /// <summary>
        /// Número válido na máscara: 8 dígitos e, para níveis 1-4, posições além do prefixo
        /// devem ser zero (ex.: nível 4 = 5 dígitos + "000"). Nível 5 é livre nas posições 6-8.
        /// </summary>
        public static bool MascaraValida(string numConta, out string erro)
        {
            erro = null;
            var nc = (numConta ?? "").Trim();
            if (nc.Length != 8) { erro = "O número deve ter 8 dígitos."; return false; }
            foreach (var c in nc) if (c < '0' || c > '9') { erro = "O número deve conter só dígitos."; return false; }
            int nivel = Nivel(nc);
            int pref = Prefixo[nivel];
            for (int i = pref; i < 8; i++)
                if (nc[i] != '0') { erro = "Número inconsistente com a hierarquia (1.1.1.2.3)."; return false; }
            return true;
        }
    }
}
