using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FIXMessageParser.Models;
using FIXMessageParser.Services;
using Microsoft.Win32;

namespace FIXMessageParser;

public partial class MainWindow : Window
{
    private readonly FIXDictionaryService _dictService = new();
    private readonly FIXParserService _parserService;
    private FIXField? _rightClickedField;
    private string _rightClickedColumn = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _parserService = new FIXParserService(_dictService);
        LoadBuiltInDictionary();
    }

    private void LoadBuiltInDictionary()
    {
        string xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataDictionary", "FIX42.xml");
        if (File.Exists(xmlPath))
        {
            _dictService.LoadFixDictionary(xmlPath);
            DictionaryStatusText.Text = "Dictionary: built-in FIX 4.2";
        }
        else
        {
            DictionaryStatusText.Text = "Dictionary: FIX42.xml not found — tag names unavailable";
        }
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
            var fields = _parserService.ParseMessages(input);
            ResultsGrid.ItemsSource = fields;

            int msgCount = fields.Select(f => f.MessageIndex).Distinct().Count();
            int fieldCount = fields.Count;
            StatusText.Text = $"{fieldCount} fields across {msgCount} message(s)";
            FooterText.Text = $"Parsed {msgCount} message(s), {fieldCount} fields total";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Clear();
        ResultsGrid.ItemsSource = null;
        StatusText.Text = string.Empty;
        FooterText.Text = "Ready — paste a FIX message above and click Parse";
    }

    private void LoadDictButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load Custom Tags CSV",
            Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            _dictService.LoadCustomTags(dlg.FileName);
            string fileName = Path.GetFileName(dlg.FileName);
            DictionaryStatusText.Text = $"Dictionary: FIX 4.2 + {fileName}";
            StatusText.Text = $"Custom tags loaded from {fileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load custom tags:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.ItemsSource is not System.Collections.IEnumerable items) return;

        var sb = new StringBuilder();
        sb.AppendLine("Msg,Section,Tag,FieldName,Value,Description,GroupContext");

        foreach (var item in items)
        {
            if (item is Models.FIXField f)
            {
                sb.AppendLine($"{f.MessageIndex},{f.Section},{f.Tag}," +
                              $"{Csv(f.TagName)},{Csv(f.Value)},{Csv(f.ValueDescription)},{Csv(f.GroupContext)}");
            }
        }

        Clipboard.SetText(sb.ToString());
        StatusText.Text = "Copied to clipboard as CSV";
    }

    private void InputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(InputTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ResultsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Walk up the visual tree from the click source to find the DataGridCell
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not DataGridCell)
            element = VisualTreeHelper.GetParent(element);

        if (element is not DataGridCell cell) return;

        _rightClickedField = cell.DataContext as FIXField;
        _rightClickedColumn = cell.Column?.Header?.ToString() ?? string.Empty;

        // Update "Copy Cell" header to show which column was right-clicked
        MenuCopyCell.Header = string.IsNullOrEmpty(_rightClickedColumn)
            ? "Copy Cell"
            : $"Copy  \"{_rightClickedColumn}\"";
    }

    private void MenuCopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedField == null) return;
        string value = GetColumnValue(_rightClickedField, _rightClickedColumn);
        SetClipboard(value);
    }

    private void MenuCopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedField == null) return;
        SetClipboard(_rightClickedField.Value);
    }

    private void MenuCopyTagName_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedField == null) return;
        SetClipboard(_rightClickedField.TagName);
    }

    private void MenuCopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedField == null) return;
        var f = _rightClickedField;
        string row = string.Join("\t", f.MessageIndex, f.Section, f.Tag,
                                       f.TagName, f.Value, f.ValueDescription, f.GroupContext);
        SetClipboard(row);
    }

    private string GetColumnValue(FIXField f, string column) => column switch
    {
        "Msg"           => f.MessageIndex.ToString(),
        "Sec"           => f.Section,
        "Tag"           => f.Tag.ToString(),
        "Field Name"    => f.TagName,
        "Value"         => f.Value,
        "Description"   => f.ValueDescription,
        "Group Context" => f.GroupContext,
        _               => f.Value
    };

    private void SetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Clipboard.SetText(text);
        StatusText.Text = $"Copied: {(text.Length > 40 ? text[..40] + "…" : text)}";
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}