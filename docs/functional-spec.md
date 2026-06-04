# FIX Message Parser — Functional Specification

## Overview

A personal desktop tool to paste and parse FIX 4.2 messages into a human-readable grid.
Built for debugging Fidessa messages that use standard FIX 4.2 with optional proprietary tags.

---

## Features

### 1. Paste and Parse

- Paste one or more FIX messages into the input text box
- Click **Parse** to decode all messages at once
- The result grid populates immediately showing every field

### 2. Multi-Message Support

- Multiple messages can be pasted together (e.g. copied from a log file)
- Each message is auto-detected by its `8=FIX.4.2` prefix
- Messages are numbered (Msg 1, Msg 2 …) and shown in alternating row colours for easy separation

### 3. Delimiter Auto-Detection

The parser accepts any of these common delimiters without configuration:

| Delimiter | Description |
|-----------|-------------|
| `\x01` (SOH) | Native FIX delimiter |
| `\|` (pipe) | Most common log format |
| `^` (caret) | Alternate log format |
| `^A` | Text representation of SOH |
| `\x01` (literal) | Escaped hex notation |

### 4. Result Grid Columns

| Column | Description |
|--------|-------------|
| **Msg** | Message number (1-based) |
| **Sec** | Section: `Header`, `Body`, or `Trailer` |
| **Tag** | FIX tag number |
| **Field Name** | Human-readable name resolved from FIX 4.2 dictionary |
| **Value** | Raw tag value |
| **Description** | Enum description where applicable (e.g. `Side=1 → BUY`) |
| **Group Context** | Repeating group membership, e.g. `NoAllocs [2/2]` |

### 5. Repeating Group Handling

- Correctly identifies repeating group start (count tag, e.g. `78=NoAllocs`)
- Tracks each group instance and labels it `GroupName [N/Total]`
- Supports nested groups
- Group count tags are shown in **bold** in the Field Name column

### 6. Enum Value Descriptions

Standard FIX 4.2 enum values are decoded automatically:

| Tag | Value | Description shown |
|-----|-------|-------------------|
| 35 | D | NEW_ORDER_SINGLE |
| 54 | 1 | BUY |
| 40 | 2 | LIMIT |
| 39 | 2 | FILLED |
| 150 | 2 | FILL |
| 59 | 0 | DAY |

### 7. Custom / Fidessa Tags

- Click **Load Custom Tags...** to load a CSV file with proprietary tag definitions
- Format: one tag per line — `TagNumber,TagName`
- Lines starting with `#` are treated as comments
- Custom tags are merged with the built-in FIX 4.2 dictionary
- A `custom_tags.csv` template is provided in the application folder

Example `custom_tags.csv`:
```
# Fidessa proprietary tags
9000,FidessaOrderRef
9001,FidessaBrokerCode
9002,FidessaClientRef
```

### 8. Copy Cell Value

- Click any cell to select it
- Press **Ctrl+C** to copy the cell value to clipboard
- **Shift+Click** or **Ctrl+Click** selects multiple cells; **Ctrl+C** copies all selected values tab-separated

### 9. Copy as CSV

- Click **Copy as CSV** to copy the entire result grid to clipboard as a CSV
- Format: `Msg,Section,Tag,FieldName,Value,Description,GroupContext`
- Useful for pasting into Excel for further analysis

### 10. Section Colour Coding

| Section | Colour |
|---------|--------|
| Header | Blue text |
| Body | Black text |
| Trailer | Orange/brown text |
| Group count tag | Semi-bold field name |

---

## Supported Message Types

All standard FIX 4.2 message types are supported for field name resolution and group detection:

| MsgType | Name |
|---------|------|
| 0 | Heartbeat |
| 8 | ExecutionReport |
| D | NewOrderSingle |
| F | OrderCancelRequest |
| G | OrderCancelReplaceRequest |
| 9 | OrderCancelReject |
| AE | TradeCaptureReport |
| J | Allocation |
| … | All other FIX 4.2 types |

---

## Custom Tags CSV Format

```
# Lines starting with # are ignored
# Format: TagNumber,TagName
9000,FidessaField1
9001,FidessaOrderRef
```

- Tag numbers must be integers
- Tag names must not contain commas
- Custom tags override built-in definitions if the same tag number is used
