using System.Data;

namespace DeltaZulu.Agent.Cli.Tui;

internal sealed class BoundTableModel
{
    private readonly int _limit;
    private readonly DataTable _table = new("WorkbenchResults");
    private readonly Queue<object[]> _rows = new();

    public BoundTableModel(int limit)
    {
        _limit = Math.Max(1, limit);
    }

    public DataTable Table => _table;

    public int Count => _rows.Count;

    public void Reset(IEnumerable<string> columns)
    {
        _table.Rows.Clear();
        _table.Columns.Clear();
        _rows.Clear();
        foreach (var column in columns)
        {
            _table.Columns.Add(column, typeof(string));
        }

        if (_table.Columns.Count == 0)
        {
            _table.Columns.Add("Result", typeof(string));
        }
    }

    public void SetRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var columns = rows.Count == 0
            ? ["Result"]
            : rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        Reset(columns);
        if (rows.Count == 0)
        {
            AddRaw(["0 rows"]);
            return;
        }

        foreach (var row in rows)
        {
            AddRaw([.. columns.Select(column => Format(row.TryGetValue(column, out var value) ? value : null))]);
        }
    }

    public void Append(IReadOnlyDictionary<string, object?> row)
    {
        if (_table.Columns.Count == 0 || row.Keys.Any(key => !_table.Columns.Contains(key)))
        {
            var columns = _table.Columns.Cast<DataColumn>().Select(column => column.ColumnName)
                .Concat(row.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var existing = _rows.ToArray();
            Reset(columns);
            foreach (var existingRow in existing)
            {
                AddRaw(existingRow);
            }
        }

        AddRaw([.. _table.Columns.Cast<DataColumn>().Select(column => Format(row.TryGetValue(column.ColumnName, out var value) ? value : null))]);
    }

    public void Clear()
    {
        _table.Rows.Clear();
        _rows.Clear();
    }

    private void AddRaw(object[] values)
    {
        while (_rows.Count >= _limit)
        {
            _rows.Dequeue();
            if (_table.Rows.Count > 0)
            {
                _table.Rows.RemoveAt(0);
            }
        }

        _rows.Enqueue(values);
        _table.Rows.Add(values);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        _ => value.ToString() ?? string.Empty
    };
}
