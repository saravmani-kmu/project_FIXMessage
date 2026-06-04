using System.IO;
using System.Xml.Linq;

namespace FIXMessageParser.Services;

public record TagDef(int Number, string Name, string Type, Dictionary<string, string> Enums);

public record GroupStructure(string Name, int FirstDelimTag, HashSet<int> MemberTags);

public class FIXDictionaryService
{
    private Dictionary<int, TagDef> _tagsByNumber = new();
    private Dictionary<string, int> _tagsByName = new();
    private Dictionary<int, GroupStructure> _groupStructures = new();
    private string _customTagsPath = string.Empty;

    public bool IsLoaded => _tagsByNumber.Count > 0;

    public void LoadFixDictionary(string xmlPath)
    {
        _tagsByNumber.Clear();
        _tagsByName.Clear();
        _groupStructures.Clear();

        var doc = XDocument.Load(xmlPath);
        var root = doc.Root!;

        // Parse <fields> section: number -> name, type, enums
        var fieldsEl = root.Element("fields");
        if (fieldsEl != null)
        {
            foreach (var f in fieldsEl.Elements("field"))
            {
                if (!int.TryParse(f.Attribute("number")?.Value, out int num)) continue;
                string name = f.Attribute("name")?.Value ?? string.Empty;
                string type = f.Attribute("type")?.Value ?? string.Empty;
                var enums = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var v in f.Elements("value"))
                {
                    string enumVal = v.Attribute("enum")?.Value ?? string.Empty;
                    string desc = v.Attribute("description")?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(enumVal))
                        enums[enumVal] = desc;
                }
                _tagsByNumber[num] = new TagDef(num, name, type, enums);
                if (!string.IsNullOrEmpty(name))
                    _tagsByName[name] = num;
            }
        }

        // Parse <messages> section: collect group structures
        // Build: groupCountTag -> GroupStructure (union of member tags across all messages)
        var groupMembersAccum = new Dictionary<int, (string Name, int FirstDelim, HashSet<int> Members)>();

        void ParseGroupsFromContainer(XElement container)
        {
            foreach (var grp in container.Elements("group"))
            {
                string groupName = grp.Attribute("name")?.Value ?? string.Empty;
                if (!_tagsByName.TryGetValue(groupName, out int countTag)) continue;

                var fields = grp.Elements("field").ToList();
                if (fields.Count == 0) continue;

                string firstFieldName = fields[0].Attribute("name")?.Value ?? string.Empty;
                if (!_tagsByName.TryGetValue(firstFieldName, out int firstDelim)) continue;

                var memberSet = new HashSet<int>();
                foreach (var mf in fields)
                {
                    string mfName = mf.Attribute("name")?.Value ?? string.Empty;
                    if (_tagsByName.TryGetValue(mfName, out int mfTag))
                        memberSet.Add(mfTag);
                }

                if (groupMembersAccum.TryGetValue(countTag, out var existing))
                {
                    foreach (var m in memberSet) existing.Members.Add(m);
                }
                else
                {
                    groupMembersAccum[countTag] = (groupName, firstDelim, memberSet);
                }

                // Recurse into nested groups
                ParseGroupsFromContainer(grp);
            }
        }

        var messagesEl = root.Element("messages");
        if (messagesEl != null)
        {
            foreach (var msg in messagesEl.Elements("message"))
                ParseGroupsFromContainer(msg);
        }

        foreach (var (countTag, (name, firstDelim, members)) in groupMembersAccum)
            _groupStructures[countTag] = new GroupStructure(name, firstDelim, members);
    }

    public void LoadCustomTags(string csvPath)
    {
        _customTagsPath = csvPath;
        foreach (var line in File.ReadLines(csvPath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split(',', 2);
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0].Trim(), out int tagNum)) continue;
            string tagName = parts[1].Trim();
            _tagsByNumber[tagNum] = new TagDef(tagNum, tagName, "STRING", new Dictionary<string, string>());
            _tagsByName[tagName] = tagNum;
        }
    }

    public string GetCustomTagsPath() => _customTagsPath;

    public TagDef? GetTag(int tagNum) => _tagsByNumber.TryGetValue(tagNum, out var t) ? t : null;

    public string GetTagName(int tagNum) =>
        _tagsByNumber.TryGetValue(tagNum, out var t) ? t.Name : string.Empty;

    public string GetValueDescription(int tagNum, string value)
    {
        if (!_tagsByNumber.TryGetValue(tagNum, out var t)) return string.Empty;
        return t.Enums.TryGetValue(value, out string? desc) ? desc : string.Empty;
    }

    public bool IsGroupCountTag(int tagNum) => _groupStructures.ContainsKey(tagNum);

    public GroupStructure? GetGroupStructure(int tagNum) =>
        _groupStructures.TryGetValue(tagNum, out var g) ? g : null;

    public HashSet<int> GetHeaderTags()
    {
        // Standard FIX 4.2 header tag numbers
        return new HashSet<int> { 8, 9, 35, 34, 49, 56, 52, 50, 57, 43, 97, 122,
            115, 116, 128, 129, 90, 91, 93, 89, 142, 143, 144, 145, 212, 213 };
    }

    public HashSet<int> GetTrailerTags()
    {
        return new HashSet<int> { 10, 89, 93 };
    }
}
