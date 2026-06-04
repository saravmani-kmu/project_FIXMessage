using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FIXMessageParser.Services;

namespace FIXMessageParser;

public partial class TableViewWindow : Window
{
    private readonly FIXDictionaryService _dict;
    private readonly FIXParserService _parser;

    // Maps DataTable column id → display header (tag name)
    private readonly Dictionary<string, string> _colHeaders = new();
    // Maps DataTable column id → tag number (for tooltip)
    private readonly Dictionary<string, int> _colTagNumbers = new();

    private static readonly int[] HeaderTagOrder = { 8, 9, 35, 49, 56, 34, 52, 50, 57, 115, 116, 128, 129, 43, 97, 122 };
    private static readonly HashSet<int> TrailerTags = new() { 10, 89, 93 };

    public TableViewWindow(FIXDictionaryService dict, FIXParserService parser)
    {
        InitializeComponent();
        _dict = dict;
        _parser = parser;
    }

    private void ParseButton_Click(object sender, RoutedEventArgs e)
    {
        string input = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            StatusText.Text = "Nothing to parse.";
            return;
        }

        try
        {
            var fields = _parser.ParseMessages(input);
            if (fields.Count == 0)
            {
                StatusText.Text = "No fields parsed — check input.";
                return;
            }

            var dt = BuildDataTable(fields);
            ResultsGrid.ItemsSource = dt.DefaultView;

            int msgCount = fields.Select(f => f.MessageIndex).Distinct().Count();
            int colCount = dt.Columns.Count - 1; // exclude Msg column
            StatusText.Text = $"{msgCount} message(s), {colCount} tag column(s)";
            FooterText.Text = $"Showing {msgCount} messages × {colCount} tags";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private DataTable BuildDataTable(List<Models.FIXField> fields)
    {
        _colHeaders.Clear();
        _colTagNumbers.Clear();

        // Collect all unique tags across all messages, sorted in FIX order
        var allTags = fields
            .Select(f => f.Tag)
            .Distinct()
            .OrderBy(TagSortKey)
            .ToList();

        var dt = new DataTable();

        // First column: message label
        dt.Columns.Add("Msg", typeof(string));

        // One column per unique tag — use "T{tag}" as internal name for uniqueness
        foreach (int tag in allTags)
        {
            string colId = $"T{tag}";
            string tagName = _dict.GetTagName(tag);
            string displayName = string.IsNullOrEmpty(tagName) ? $"Tag{tag}" : tagName;

            _colHeaders[colId] = displayName;
            _colTagNumbers[colId] = tag;
            dt.Columns.Add(colId, typeof(string));
        }

        // One row per message
        foreach (var group in fields.GroupBy(f => f.MessageIndex).OrderBy(g => g.Key))
        {
            var row = dt.NewRow();

            string msgType = group.FirstOrDefault(f => f.Tag == 35)?.Value ?? "?";
            string msgTypeName = _dict.GetValueDescription(35, msgType);
            row["Msg"] = string.IsNullOrEmpty(msgTypeName)
                ? $"#{group.Key} (35={msgType})"
                : $"#{group.Key} {msgTypeName}";

            // Build tag → value(s) map for this message
            // Repeated tags (groups) are joined with " / "
            var tagValues = group
                .GroupBy(f => f.Tag)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(" / ", g.Select(f => f.Value)));

            foreach (int tag in allTags)
            {
                string colId = $"T{tag}";
                row[colId] = tagValues.TryGetValue(tag, out string? val) ? val : string.Empty;
            }

            dt.Rows.Add(row);
        }

        return dt;
    }

    private int TagSortKey(int tag)
    {
        int hIdx = Array.IndexOf(HeaderTagOrder, tag);
        if (hIdx >= 0) return hIdx;
        if (TrailerTags.Contains(tag)) return 90000 + tag;
        return 10000 + tag; // body tags sorted numerically
    }

    private void ResultsGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        string colId = e.PropertyName;

        if (colId == "Msg")
        {
            e.Column.Header = "Message";
            e.Column.Width = new DataGridLength(150);
            e.Column.IsReadOnly = true;
            return;
        }

        if (_colHeaders.TryGetValue(colId, out string? header))
        {
            int tagNum = _colTagNumbers[colId];
            e.Column.Header = header;
            e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);

            // Add tag number as tooltip on the column header
            var headerBlock = new TextBlock
            {
                Text = header,
                ToolTip = $"Tag {tagNum}",
                FontWeight = System.Windows.FontWeights.SemiBold
            };
            e.Column.Header = headerBlock;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Clear();
        ResultsGrid.ItemsSource = null;
        StatusText.Text = string.Empty;
        FooterText.Text = "Ready — paste messages above and click Parse";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.ItemsSource is not DataView dv) return;

        var sb = new StringBuilder();
        var dt = dv.Table!;

        // Header row using display names
        var headers = dt.Columns.Cast<DataColumn>().Select(c =>
            c.ColumnName == "Msg" ? "Message" :
            _colHeaders.TryGetValue(c.ColumnName, out var h) ? h : c.ColumnName);
        sb.AppendLine(string.Join(",", headers));

        // Data rows
        foreach (DataRowView row in dv)
        {
            var values = dt.Columns.Cast<DataColumn>()
                .Select(c => CsvEscape(row[c.ColumnName]?.ToString() ?? string.Empty));
            sb.AppendLine(string.Join(",", values));
        }

        Clipboard.SetText(sb.ToString());
        StatusText.Text = "Copied to clipboard as CSV";
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(InputTextBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string CsvEscape(string s) =>
        (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
