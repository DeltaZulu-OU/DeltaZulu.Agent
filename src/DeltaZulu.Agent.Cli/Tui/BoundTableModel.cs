using System.Data;

namespace DeltaZulu.Agent.Cli.Tui;

internal sealed class BoundTableModel : IDisposable
{
    private readonly int _limit;
    private readonly Queue<object[]> _rows = new();
    private bool disposedValue;

    public BoundTableModel(int limit)
    {
        _limit = Math.Max(1, limit);
    }

    public DataTable Table { get; } = new("WorkbenchResults");

    public int Count => _rows.Count;

    public void Reset(IEnumerable<string> columns)
    {
        Table.Rows.Clear();
        Table.Columns.Clear();
        _rows.Clear();
        foreach (var column in columns)
        {
            Table.Columns.Add(column, typeof(string));
        }

        if (Table.Columns.Count == 0)
        {
            Table.Columns.Add("Result", typeof(string));
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
        if (Table.Columns.Count == 0 || row.Keys.Any(key => !Table.Columns.Contains(key)))
        {
            var columns = Table.Columns.Cast<DataColumn>().Select(column => column.ColumnName)
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

        AddRaw([.. Table.Columns.Cast<DataColumn>().Select(column => Format(row.TryGetValue(column.ColumnName, out var value) ? value : null))]);
    }

    public void Clear()
    {
        Table.Rows.Clear();
        _rows.Clear();
    }

    private void AddRaw(object[] values)
    {
        while (_rows.Count >= _limit)
        {
            _rows.Dequeue();
            if (Table.Rows.Count > 0)
            {
                Table.Rows.RemoveAt(0);
            }
        }

        _rows.Enqueue(values);
        Table.Rows.Add(values);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        _ => value.ToString() ?? string.Empty
    };

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Table.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
