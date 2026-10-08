using System.Text;

namespace PerfPilot.Core.Services;

public sealed record CsvTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

/// <summary>
/// Minimal RFC 4180 CSV parser for pasted test data. The first row is the header.
/// Tab-separated data (pasted from a spreadsheet) is detected automatically.
/// </summary>
public static class CsvParser
{
    public static CsvTable Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CsvTable([], []);
        }

        var delimiter = DetectDelimiter(text);
        var records = ReadRecords(text, delimiter)
            .Where(r => !(r.Count == 1 && string.IsNullOrWhiteSpace(r[0])))
            .ToList();
        if (records.Count == 0)
        {
            return new CsvTable([], []);
        }

        var columns = records[0].Select(c => c.Trim()).ToList();
        var rows = new List<IReadOnlyDictionary<string, string>>(records.Count - 1);
        foreach (var record in records.Skip(1))
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Length == 0 || row.ContainsKey(columns[i]))
                {
                    continue;
                }
                row[columns[i]] = i < record.Count ? record[i] : string.Empty;
            }
            rows.Add(row);
        }

        return new CsvTable(columns, rows);
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var firstLine = end < 0 ? text : text[..end];
        return firstLine.Contains('\t') && !firstLine.Contains(',') ? '\t' : ',';
    }

    private static IEnumerable<List<string>> ReadRecords(string text, char delimiter)
    {
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                record.Add(field.ToString());
                field.Clear();
                yield return record;
                record = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
