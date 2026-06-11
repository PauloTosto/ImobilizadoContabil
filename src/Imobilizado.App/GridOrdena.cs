using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Imobilizado.App
{
    /// <summary>
    /// Dá ordenação por CLIQUE NO CABEÇALHO a um DataGridView bound numa List&lt;T&gt; (inclusive
    /// tipo anônimo): clica → ordena ascendente; clica de novo → inverte. Re-binda uma lista
    /// ordenada do MESMO tipo e preserva a formatação das colunas (header, largura, formato,
    /// alinhamento, visibilidade) capturada antes do re-bind. Uso: GridOrdena.Aplicar(dgv);
    /// </summary>
    public static class GridOrdena
    {
        public static void Aplicar(DataGridView dgv)
        {
            string ordCol = null;
            bool asc = true;

            dgv.ColumnHeaderMouseClick += (s, e) =>
            {
                if (e.ColumnIndex < 0) return;
                var col = dgv.Columns[e.ColumnIndex];
                var nome = col.DataPropertyName.Length > 0 ? col.DataPropertyName : col.Name;
                if (!(dgv.DataSource is IList fonte) || fonte.Count == 0) return;

                var tipoLista = fonte.GetType();
                if (!tipoLista.IsGenericType) return;
                var tipoItem = tipoLista.GetGenericArguments()[0];
                var prop = tipoItem.GetProperty(nome);
                if (prop == null) return;

                if (nome == ordCol) asc = !asc; else { ordCol = nome; asc = true; }

                // ordena com comparação segura (null primeiro; IComparable; senão string)
                int Cmp(object a, object b)
                {
                    var va = a == null ? null : prop.GetValue(a);
                    var vb = b == null ? null : prop.GetValue(b);
                    if (va == null && vb == null) return 0;
                    if (va == null) return -1;
                    if (vb == null) return 1;
                    if (va is IComparable ca && va.GetType() == vb.GetType()) return ca.CompareTo(vb);
                    return string.Compare(va.ToString(), vb.ToString(), StringComparison.OrdinalIgnoreCase);
                }
                var itens = fonte.Cast<object>().ToList();
                itens.Sort(Cmp);
                if (!asc) itens.Reverse();

                // re-binda numa List<T> do MESMO tipo do item (bind de List<object> não gera colunas)
                var nova = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tipoItem));
                foreach (var it in itens) nova.Add(it);

                // captura o estado visual das colunas p/ restaurar após o re-bind
                var estados = dgv.Columns.Cast<DataGridViewColumn>().Select(c => new
                {
                    c.Name,
                    c.HeaderText,
                    c.FillWeight,
                    c.Visible,
                    Formato = c.DefaultCellStyle.Format,
                    Alin = c.DefaultCellStyle.Alignment,
                    c.DisplayIndex,
                }).ToList();

                dgv.DataSource = nova;

                foreach (var st in estados)
                {
                    if (!dgv.Columns.Contains(st.Name)) continue;
                    var c = dgv.Columns[st.Name];
                    c.HeaderText = st.HeaderText;
                    c.FillWeight = st.FillWeight;
                    c.Visible = st.Visible;
                    c.DefaultCellStyle.Format = st.Formato;
                    c.DefaultCellStyle.Alignment = st.Alin;
                    try { c.DisplayIndex = st.DisplayIndex; } catch { }
                }
                foreach (DataGridViewColumn c in dgv.Columns)
                {
                    c.SortMode = DataGridViewColumnSortMode.Programmatic;
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;
                }
                if (dgv.Columns.Contains(nome))
                    dgv.Columns[nome].HeaderCell.SortGlyphDirection = asc ? SortOrder.Ascending : SortOrder.Descending;
            };
        }
    }
}
