using Spectre.Console;
using Sylvan.Data.Csv;
using System.Data;

namespace EgsExporter.Exporters
{
    internal class CsvExporter : IDataExporter
    {
        private readonly string _file;
        private readonly List<object[]> _rows = [];

        private DataColumn[] _headers = [];
        private int _headerSet = 0;

        public CsvExporter(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException("file is null or white space", nameof(file));

            _file = file;
        }

        public void ExportRow(IEnumerable<object> values)
        {
            // TODO: Validate row length?

            _rows.Add(values.ToArray());
        }

        public void Flush()
        {
            using var writer = new StreamWriter(_file, append: false);
            using var csv = CsvDataWriter.Create(writer);

            var dt = new DataTable();
            dt.Columns.AddRange(_headers);

            foreach (var row in _rows)
            {
                dt.Rows.Add(row);
            }

            var reader = dt.CreateDataReader();
            csv.Write(reader);
        }

        public void SetHeader(IEnumerable<string> values)
        {
            if (Interlocked.Exchange(ref _headerSet, 1) != 0)
                return; // TODO: Error handling

            _headers = values.Select(x => new DataColumn
            {
                DataType = typeof(string),
                ColumnName = x
            }).ToArray();
        }
    }
}
