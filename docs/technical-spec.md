# FIX Message Parser — Technical Specification

## Technology Stack

| Component | Choice |
|-----------|--------|
| Framework | .NET 8 (net8.0-windows) |
| UI | WPF (Windows Presentation Foundation) |
| Language | C# 12 |
| FIX Dictionary | QuickFIXn.Core 1.14.0 + QuickFIXn.FIX4.2 1.13.0 |
| XML Parsing | System.Xml.Linq (XDocument) |

---

## Project Structure

```
FIXMessageParser/
├── FIXMessageParser.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml             — UI layout and DataGrid definition
├── MainWindow.xaml.cs          — Event handlers, wires up services
├── custom_tags.csv             — User-editable proprietary tag definitions
├── DataDictionary/
│   └── FIX42.xml               — Standard FIX 4.2 data dictionary (from QuickFIXn package)
├── Models/
│   └── FIXField.cs             — Flat model bound to the DataGrid
├── Services/
│   ├── FIXDictionaryService.cs — Loads and queries tag/group definitions
│   └── FIXParserService.cs     — Splits and parses raw FIX strings
└── Converters/
    └── RowStyleConverter.cs    — WPF value converters for row colouring
```

---

## Data Model

### `FIXField`

Flat model — one instance per tag in the parsed output, bound directly to the DataGrid.

| Property | Type | Description |
|----------|------|-------------|
| `MessageIndex` | int | 1-based message number |
| `MessageLabel` | string | e.g. `"Msg 1 (NewOrderSingle)"` |
| `Section` | string | `"Header"`, `"Body"`, or `"Trailer"` |
| `Tag` | int | FIX tag number |
| `TagName` | string | Human-readable field name from dictionary |
| `Value` | string | Raw string value from the message |
| `ValueDescription` | string | Enum description if applicable |
| `GroupContext` | string | e.g. `"NoAllocs [1/2]"` |
| `IsGroupCountTag` | bool | True for NUMINGROUP-type tags (drives bold font) |

---

## Services

### `FIXDictionaryService`

Responsible for all tag and group metadata.

**Initialisation — `LoadFixDictionary(string xmlPath)`**

Parses `FIX42.xml` using `XDocument` in two passes:

1. **Fields pass** — reads the `<fields>` section, builds:
   - `Dictionary<int, TagDef> _tagsByNumber` — tag number → `(Number, Name, Type, Enums)`
   - `Dictionary<string, int> _tagsByName` — name → tag number (used during group parsing)

2. **Group structure pass** — recursively walks every `<message>/<group>` element:
   - Resolves the group name to its tag number via `_tagsByName`
   - Records the first `<field>` inside the group as the **first delimiter tag**
   - Collects all `<field>` elements inside the group into a **member tag set**
   - Unions member sets across all messages that share the same group count tag
   - Stores in `Dictionary<int, GroupStructure> _groupStructures`

**`LoadCustomTags(string csvPath)`**

Reads a `TagNumber,TagName` CSV. Inserts or overwrites entries in `_tagsByNumber`. Called after `LoadFixDictionary` so custom tags take precedence.

**Key lookup methods**

| Method | Returns |
|--------|---------|
| `GetTagName(int)` | Field name string, empty if unknown |
| `GetValueDescription(int, string)` | Enum description, empty if not an enum field |
| `IsGroupCountTag(int)` | True if tag is a NUMINGROUP group delimiter |
| `GetGroupStructure(int)` | `GroupStructure` record with FirstDelimTag and MemberTags |
| `GetHeaderTags()` | `HashSet<int>` of standard FIX 4.2 header tag numbers |
| `GetTrailerTags()` | `HashSet<int>` of standard FIX 4.2 trailer tag numbers |

---

### `FIXParserService`

Converts a raw pasted string into a flat list of `FIXField` objects.

#### Step 1 — Delimiter Normalisation (`NormalizeDelimiters`)

Checks which delimiter is present in priority order and replaces it with the native SOH (`\x01`):

```
\x01 (already SOH) → no change
|    → replace with SOH
^    → replace with SOH
^A   → replace with SOH
\x01 (literal text) → replace with SOH
```

#### Step 2 — Message Splitting (`SplitIntoMessages`)

Scans the normalised string for `SOH8=FIX` boundaries. Each boundary starts a new message. This handles logs where multiple messages are concatenated.

#### Step 3 — Tag-Value Extraction (`SplitToTagValues`)

Splits each message on SOH, then splits each part on the first `=` to produce `List<(int Tag, string Value)>` in original order. Order is preserved — this is critical for group detection.

#### Step 4 — Message Processing (`ProcessMessage`)

Iterates through `(tag, value)` pairs maintaining a group tracking stack:

**Group State Machine**

Each active group is represented by a `GroupState` object on a `Stack<GroupState>`:

```
GroupState {
    Name           — group name e.g. "NoAllocs"
    ExpectedCount  — value of the count tag
    FirstDelimTag  — tag number of the first member field
    MemberTags     — HashSet of all valid member tag numbers
    SeenCount      — how many delimiter occurrences seen so far
}
```

**`ResolveGroupStack(stack, currentTag)` — called before processing each field**

```
while stack is not empty:
    top = stack.Peek()

    if currentTag == top.FirstDelimTag:
        if top.SeenCount < top.ExpectedCount:
            top.SeenCount++
            return          // new group iteration started
        else:
            stack.Pop()     // exceeded count — tag belongs to outer scope
            continue        // re-check against outer group

    if top.MemberTags.Contains(currentTag):
        return              // tag is a valid member, stay in group

    stack.Pop()             // tag not in group — group ended naturally
    continue
```

This member-set approach correctly identifies where the last group iteration ends without relying on a simple count-exceeded heuristic, which would incorrectly pop the group before the last iteration's trailing fields are processed.

**After `ResolveGroupStack`:**

- `BuildGroupContext` serialises the stack to a display string, e.g. `"NoAllocs [2/2]"`
- Section is determined by checking against header/trailer tag sets
- If the current tag is itself a NUMINGROUP tag and its value > 0, a new `GroupState` is pushed

---

## UI Architecture

### `MainWindow.xaml`

Pure XAML layout — no code-behind logic beyond event wiring.

**Layout structure (5-row Grid):**

```
Row 0 — Title bar (dark header, dictionary status on right)
Row 1 — Input TextBox (Consolas font, multiline, placeholder text)
Row 2 — Button bar (Parse / Clear / Load Custom Tags / Copy as CSV + status label)
Row 3 — Results DataGrid (fills remaining space)
Row 4 — Footer status bar
```

### Value Converters (`RowStyleConverter.cs`)

| Converter | Input → Output | Used for |
|-----------|---------------|----------|
| `MessageIndexToBackgroundConverter` | `int` → `Brush` | Alternating white/alice-blue rows per message |
| `SectionToForegroundConverter` | `string` → `Brush` | Blue for Header, orange for Trailer, black for Body |
| `GroupCountTagToWeightConverter` | `bool` → `FontWeight` | Semi-bold for group count tag rows |

### Cell Selection and Copy

- `SelectionUnit="Cell"` — individual cell selection
- `ClipboardCopyMode="ExcludeHeader"` — Ctrl+C copies selected cell value(s) without the column header
- Multiple cells selected via Shift+Click or Ctrl+Click are copied tab-separated

---

## FIX42.xml and NuGet Packages

`QuickFIXn.FIX4.2` is used solely to supply the `DataDictionary/FIX42.xml` file, which is copied to the build output via the project's `<Content>` item group. The `QuickFIXn.Core` library is referenced but its C# classes are not used directly — all XML parsing is done via `System.Xml.Linq` for full control over the group structure extraction.

The XML is loaded once at startup. Loading typically takes < 50 ms.

---

## Configuration and Extensibility

### Adding Custom Tags

Edit `custom_tags.csv` in the application folder, or click **Load Custom Tags...** at runtime:

```csv
# TagNumber,TagName
9000,FidessaOrderRef
9001,FidessaBrokerCode
```

### Adding Custom Enum Values

Custom tags loaded from CSV are treated as `STRING` type with no enum descriptions. To get enum descriptions for custom tags, contribute them directly in the CSV by creating an extended XML (future enhancement).

### Supporting Other FIX Versions

Replace `DataDictionary/FIX42.xml` with a different QuickFIXn spec file (e.g. `FIX44.xml`). The dictionary service is version-agnostic and will parse any conforming QuickFIXn XML spec.
