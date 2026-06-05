using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Contabil.Core;

namespace Imobilizado.App
{
    /// <summary>
    /// Sugestão de contas (apelidos do DESC2 + números) para autocompletar campos de conta.
    /// Os apelidos são exatamente o que aparece em DEBITO/CREDITO do MOVFIN — ou seja, as
    /// contas que efetivamente têm passe/movimento.
    /// </summary>
    internal static class Autocomplete
    {
        public static AutoCompleteStringCollection DeContas(PlanoContas plano)
        {
            var col = new AutoCompleteStringCollection();
            if (plano == null) return col;
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in plano.Contas.Values)
            {
                if (!string.IsNullOrWhiteSpace(c.Desc2) && vistos.Add(c.Desc2)) col.Add(c.Desc2);
                if (!string.IsNullOrWhiteSpace(c.NumConta) && vistos.Add(c.NumConta)) col.Add(c.NumConta);
            }
            return col;
        }

        public static void Aplicar(TextBox t, AutoCompleteStringCollection fonte)
        {
            if (t == null || fonte == null) return;
            t.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            t.AutoCompleteSource = AutoCompleteSource.CustomSource;
            t.AutoCompleteCustomSource = fonte;
        }
    }
}
