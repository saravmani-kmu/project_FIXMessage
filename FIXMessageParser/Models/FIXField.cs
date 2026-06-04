namespace FIXMessageParser.Models;

public class FIXField
{
    public int MessageIndex { get; set; }
    public string MessageLabel { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public int Tag { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueDescription { get; set; } = string.Empty;
    public string GroupContext { get; set; } = string.Empty;
    public bool IsGroupCountTag { get; set; }
}
