using FIXMessageParser.Models;

namespace FIXMessageParser.Services;

public class FIXParserService
{
    private readonly FIXDictionaryService _dict;
    private static readonly char Soh = '\x01';

    public FIXParserService(FIXDictionaryService dict) => _dict = dict;

    public List<FIXField> ParseMessages(string input)
    {
        var allFields = new List<FIXField>();
        var rawMessages = SplitIntoMessages(input);

        for (int i = 0; i < rawMessages.Count; i++)
        {
            var tagValues = SplitToTagValues(rawMessages[i]);
            if (tagValues.Count == 0) continue;

            string msgType = tagValues.FirstOrDefault(tv => tv.Tag == 35).Value ?? "?";
            string msgTypeName = _dict.GetValueDescription(35, msgType);
            string label = string.IsNullOrEmpty(msgTypeName)
                ? $"Msg {i + 1} (35={msgType})"
                : $"Msg {i + 1} ({msgTypeName})";

            var fields = ProcessMessage(i + 1, label, tagValues);
            allFields.AddRange(fields);
        }

        return allFields;
    }

    private List<string> SplitIntoMessages(string input)
    {
        string normalized = NormalizeDelimiters(input.Trim());

        // Split on "8=FIX" boundaries to handle multiple pasted messages
        var messages = new List<string>();
        int start = 0;
        while (start < normalized.Length)
        {
            int next = normalized.IndexOf($"{Soh}8=FIX", start + 1, StringComparison.Ordinal);
            if (next < 0)
            {
                messages.Add(normalized[start..].Trim(Soh).Trim());
                break;
            }
            messages.Add(normalized[start..(next)].Trim(Soh).Trim());
            start = next + 1; // skip the SOH, start from "8=FIX"
        }

        return messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
    }

    private string NormalizeDelimiters(string raw)
    {
        // Try to detect what delimiter is used
        // Common patterns: SOH (\x01), pipe (|), caret (^), literal "^A", space-separated
        if (raw.Contains(Soh)) return raw;

        if (raw.Contains('|'))
            return raw.Replace('|', Soh);

        if (raw.Contains('^'))
            return raw.Replace('^', Soh);

        if (raw.Contains("^A", StringComparison.Ordinal))
            return raw.Replace("^A", Soh.ToString());

        if (raw.Contains("\\x01", StringComparison.Ordinal))
            return raw.Replace("\\x01", Soh.ToString());

        // Fall back: treat as-is (might be space or newline separated)
        return raw;
    }

    private List<(int Tag, string Value)> SplitToTagValues(string message)
    {
        var result = new List<(int, string)>();
        var parts = message.Split(Soh, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string tagStr = part[..eq];
            string val = part[(eq + 1)..];
            if (int.TryParse(tagStr, out int tag))
                result.Add((tag, val));
        }
        return result;
    }

    private List<FIXField> ProcessMessage(int msgIndex, string msgLabel, List<(int Tag, string Value)> tagValues)
    {
        var fields = new List<FIXField>();
        var headerTags = _dict.GetHeaderTags();
        var trailerTags = _dict.GetTrailerTags();

        // Group tracking state
        var groupStack = new Stack<GroupState>();

        foreach (var (tag, value) in tagValues)
        {
            // Try to resolve group boundaries using member set check
            ResolveGroupStack(groupStack, tag);

            // Build group context label
            string groupContext = BuildGroupContext(groupStack);

            // Determine section
            string section = trailerTags.Contains(tag) ? "Trailer"
                           : headerTags.Contains(tag) ? "Header"
                           : "Body";

            string tagName = _dict.GetTagName(tag);
            string valueDesc = _dict.GetValueDescription(tag, value);
            bool isGroupCount = _dict.IsGroupCountTag(tag);

            fields.Add(new FIXField
            {
                MessageIndex = msgIndex,
                MessageLabel = msgLabel,
                Section = section,
                Tag = tag,
                TagName = string.IsNullOrEmpty(tagName) ? $"Tag{tag}" : tagName,
                Value = value,
                ValueDescription = valueDesc,
                GroupContext = groupContext,
                IsGroupCountTag = isGroupCount
            });

            // Start new group if this is a group count tag
            if (isGroupCount && int.TryParse(value, out int count) && count > 0)
            {
                var grpDef = _dict.GetGroupStructure(tag);
                if (grpDef != null)
                {
                    groupStack.Push(new GroupState(
                        grpDef.Name, count, grpDef.FirstDelimTag, grpDef.MemberTags, 0));
                }
            }
        }

        return fields;
    }

    private void ResolveGroupStack(Stack<GroupState> stack, int tag)
    {
        while (stack.Count > 0)
        {
            var top = stack.Peek();

            // Current tag is the group's first delimiter
            if (tag == top.FirstDelimTag)
            {
                if (top.SeenCount < top.ExpectedCount)
                {
                    top.SeenCount++;
                    return; // continuing in this group
                }
                else
                {
                    stack.Pop(); // exceeded count, tag belongs to outer scope
                    continue;
                }
            }

            // Current tag is in the group's member set → stays in group
            if (top.MemberTags.Contains(tag))
                return;

            // Current tag not in group → group ended naturally
            stack.Pop();
        }
    }

    private string BuildGroupContext(Stack<GroupState> stack)
    {
        if (stack.Count == 0) return string.Empty;
        var parts = stack.Reverse()
            .Where(g => g.SeenCount > 0)
            .Select(g => $"{g.Name} [{g.SeenCount}/{g.ExpectedCount}]");
        return string.Join(" > ", parts);
    }

    private class GroupState
    {
        public string Name { get; }
        public int ExpectedCount { get; }
        public int FirstDelimTag { get; }
        public HashSet<int> MemberTags { get; }
        public int SeenCount { get; set; }

        public GroupState(string name, int expected, int firstDelim, HashSet<int> members, int seen)
        {
            Name = name;
            ExpectedCount = expected;
            FirstDelimTag = firstDelim;
            MemberTags = members;
            SeenCount = seen;
        }
    }
}
