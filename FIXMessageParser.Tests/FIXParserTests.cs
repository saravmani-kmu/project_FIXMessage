using FIXMessageParser.Models;
using FIXMessageParser.Services;

namespace FIXMessageParser.Tests;

public class FIXParserTests
{
    //  is always exactly 4 hex digits — safe SOH constant
    // (avoid \x01 before hex digits: C# \x is greedy, \x0135 = U+0135, not \x01 + "35")
    private const char Soh = '';

    private static readonly string DictPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FIX42.xml");

    private static FIXParserService BuildParser()
    {
        var dict = new FIXDictionaryService();
        if (File.Exists(DictPath))
            dict.LoadFixDictionary(DictPath);
        return new FIXParserService(dict);
    }

    private static string ToSoh(string pipeMsg) => pipeMsg.Replace('|', Soh);

    // ── Delimiter detection ──────────────────────────────────────────────────

    [Fact]
    public void Parse_PipeDelimiter_ReturnsFields()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128|";

        var fields = parser.ParseMessages(msg);

        Assert.NotEmpty(fields);
        Assert.Contains(fields, f => f.Tag == 8 && f.Value == "FIX.4.2");
        Assert.Contains(fields, f => f.Tag == 35 && f.Value == "D");
        Assert.Contains(fields, f => f.Tag == 55 && f.Value == "AAPL");
    }

    [Fact]
    public void Parse_SohDelimiter_ReturnsFields()
    {
        var parser = BuildParser();
        // Build via Replace so  is used correctly — avoids greedy \x escape pitfall
        string msg = ToSoh("8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128|");

        var fields = parser.ParseMessages(msg);

        Assert.NotEmpty(fields);
        Assert.Contains(fields, f => f.Tag == 55 && f.Value == "AAPL");
        Assert.Contains(fields, f => f.Tag == 35 && f.Value == "D");
    }

    [Fact]
    public void Parse_NoTrailingDelimiter_ReturnsFields()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128";

        var fields = parser.ParseMessages(msg);

        Assert.NotEmpty(fields);
        Assert.Contains(fields, f => f.Tag == 10 && f.Value == "128");
    }

    // ── Multi-message ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_TwoMessagesOnSeparateLines_ReturnsTwoMessageGroups()
    {
        var parser = BuildParser();
        string input =
            "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128|\n" +
            "8=FIX.4.2|9=80|35=8|49=BROKER|56=CLIENT|34=2|52=20240604-10:00:01|37=EXEC1|11=ORD1|17=ER1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|10=201|";

        var fields = parser.ParseMessages(input);
        var messageNumbers = fields.Select(f => f.MessageIndex).Distinct().OrderBy(x => x).ToList();

        Assert.Equal(2, messageNumbers.Count);
        Assert.Equal(1, messageNumbers[0]);
        Assert.Equal(2, messageNumbers[1]);
    }

    [Fact]
    public void Parse_TwoMessagesOnSeparateLines_EachHasCorrectMsgType()
    {
        var parser = BuildParser();
        string input =
            "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128|\n" +
            "8=FIX.4.2|9=80|35=8|49=BROKER|56=CLIENT|34=2|52=20240604-10:00:01|37=EXEC1|11=ORD1|17=ER1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|10=201|";

        var fields = parser.ParseMessages(input);

        Assert.Equal("D", fields.First(f => f.MessageIndex == 1 && f.Tag == 35).Value);
        Assert.Equal("8", fields.First(f => f.MessageIndex == 2 && f.Tag == 35).Value);
    }

    [Fact]
    public void Parse_TwoMessagesWithBlankLineBetween_ReturnsTwoMessageGroups()
    {
        var parser = BuildParser();
        string input =
            "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=128|\r\n" +
            "\r\n" +
            "8=FIX.4.2|9=80|35=8|49=BROKER|56=CLIENT|34=2|52=20240604-10:00:01|37=EXEC1|11=ORD1|17=ER1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|10=201|";

        var fields = parser.ParseMessages(input);

        Assert.Equal(2, fields.Select(f => f.MessageIndex).Distinct().Count());
    }

    [Fact]
    public void Parse_TwoMessagesConcatenatedWithSoh_ReturnsTwoMessageGroups()
    {
        var parser = BuildParser();
        string msg1 = ToSoh("8=FIX.4.2|9=50|35=D|49=A|56=B|34=1|52=20240604-10:00:00|11=O1|55=AAPL|54=1|38=100|40=1|10=100|");
        string msg2 = ToSoh("8=FIX.4.2|9=50|35=8|49=B|56=A|34=2|52=20240604-10:00:01|37=E1|11=O1|17=R1|20=0|150=0|39=0|55=AAPL|38=100|14=0|6=0|10=200|");
        // SOH between them is the trailing SOH of msg1 + leading 8=FIX of msg2
        string input = msg1 + msg2;

        var fields = parser.ParseMessages(input);

        Assert.Equal(2, fields.Select(f => f.MessageIndex).Distinct().Count());
    }

    // ── Header / Body / Trailer sections ─────────────────────────────────────

    [Fact]
    public void Parse_Tag8And35AreHeader()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("Header", fields.First(f => f.Tag == 8).Section);
        Assert.Equal("Header", fields.First(f => f.Tag == 35).Section);
    }

    [Fact]
    public void Parse_Tag10IsTrailer()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("Trailer", fields.First(f => f.Tag == 10).Section);
    }

    [Fact]
    public void Parse_Tag55IsBody()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("Body", fields.First(f => f.Tag == 55).Section);
    }

    // ── Tag name resolution ──────────────────────────────────────────────────

    [Fact]
    public void Parse_WithDictionary_ResolvesTagNames()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("BeginString", fields.First(f => f.Tag == 8).TagName);
        Assert.Equal("MsgType", fields.First(f => f.Tag == 35).TagName);
        Assert.Equal("Symbol", fields.First(f => f.Tag == 55).TagName);
        Assert.Equal("Side", fields.First(f => f.Tag == 54).TagName);
    }

    [Fact]
    public void Parse_WithDictionary_ResolvesEnumDescriptions()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=2|44=150.50|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("BUY", fields.First(f => f.Tag == 54).ValueDescription);
        Assert.Equal("LIMIT", fields.First(f => f.Tag == 40).ValueDescription);
    }

    [Fact]
    public void Parse_UnknownTag_UsesTagNNNFallback()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=50|35=D|49=A|56=B|34=1|52=20240604-10:00:00|9999=CustomValue|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("Tag9999", fields.First(f => f.Tag == 9999).TagName);
    }

    // ── Repeating groups ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_NoAllocsGroup_AllMemberFieldsHaveGroupContext()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=100|35=8|49=BROKER|56=CLIENT|34=1|52=20240604-10:00:00|" +
                     "37=E1|11=O1|17=R1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|" +
                     "78=2|79=ACCT1|80=60|79=ACCT2|80=40|10=200|";

        var fields = parser.ParseMessages(msg);

        var groupFields = fields.Where(f => f.Tag == 79 || f.Tag == 80).ToList();

        Assert.Equal(4, groupFields.Count);
        Assert.All(groupFields, f => Assert.Contains("NoAllocs", f.GroupContext));
    }

    [Fact]
    public void Parse_NoAllocsGroup_IterationsLabeledCorrectly()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=100|35=8|49=BROKER|56=CLIENT|34=1|52=20240604-10:00:00|" +
                     "37=E1|11=O1|17=R1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|" +
                     "78=2|79=ACCT1|80=60|79=ACCT2|80=40|10=200|";

        var fields = parser.ParseMessages(msg);

        var acctFields = fields.Where(f => f.Tag == 79).ToList();

        Assert.Contains("[1/2]", acctFields[0].GroupContext);
        Assert.Contains("[2/2]", acctFields[1].GroupContext);
    }

    [Fact]
    public void Parse_GroupCountTagIsMarked()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=100|35=8|49=BROKER|56=CLIENT|34=1|52=20240604-10:00:00|" +
                     "37=E1|11=O1|17=R1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|" +
                     "78=2|79=ACCT1|80=60|79=ACCT2|80=40|10=200|";

        var fields = parser.ParseMessages(msg);

        Assert.True(fields.First(f => f.Tag == 78).IsGroupCountTag);
    }

    [Fact]
    public void Parse_FieldAfterGroupHasNoGroupContext()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=100|35=8|49=BROKER|56=CLIENT|34=1|52=20240604-10:00:00|" +
                     "37=E1|11=O1|17=R1|20=0|150=2|39=2|55=AAPL|54=1|38=100|32=100|31=150.50|14=100|6=150.50|" +
                     "78=2|79=ACCT1|80=60|79=ACCT2|80=40|10=200|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal(string.Empty, fields.First(f => f.Tag == 10).GroupContext);
    }

    // ── Custom tags ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CustomTagFromCsv_ResolvesName()
    {
        var dict = new FIXDictionaryService();
        if (File.Exists(DictPath)) dict.LoadFixDictionary(DictPath);

        string csvPath = Path.GetTempFileName();
        File.WriteAllText(csvPath, "# comment\n9001,FidessaOrderRef\n9002,FidessaBrokerCode\n");
        dict.LoadCustomTags(csvPath);
        File.Delete(csvPath);

        var parser = new FIXParserService(dict);
        string msg = "8=FIX.4.2|9=50|35=D|49=A|56=B|34=1|52=20240604-10:00:00|9001=REF123|9002=BRKR1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("FidessaOrderRef", fields.First(f => f.Tag == 9001).TagName);
        Assert.Equal("FidessaBrokerCode", fields.First(f => f.Tag == 9002).TagName);
    }

    // ── Field ordering ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_FieldsAreInOriginalOrder()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=CLIENT|56=BROKER|34=1|52=20240604-10:00:00|11=ORD1|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);
        var tags = fields.Select(f => f.Tag).ToList();

        Assert.Equal(new[] { 8, 9, 35, 49, 56, 34, 52, 11, 55, 54, 38, 40, 10 }, tags);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var parser = BuildParser();
        Assert.Empty(parser.ParseMessages(string.Empty));
    }

    [Fact]
    public void Parse_WhitespaceOnlyInput_ReturnsEmpty()
    {
        var parser = BuildParser();
        Assert.Empty(parser.ParseMessages("   \n\n  "));
    }

    [Fact]
    public void Parse_ValueContainingEquals_ParsedCorrectly()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=A|56=B|34=1|52=20240604-10:00:00|58=price=100.50|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.Equal("price=100.50", fields.First(f => f.Tag == 58).Value);
    }

    [Fact]
    public void Parse_SingleMessage_AllFieldsHaveMessageIndex1()
    {
        var parser = BuildParser();
        string msg = "8=FIX.4.2|9=70|35=D|49=A|56=B|34=1|52=20240604-10:00:00|55=AAPL|54=1|38=100|40=1|10=100|";

        var fields = parser.ParseMessages(msg);

        Assert.All(fields, f => Assert.Equal(1, f.MessageIndex));
    }
}
