using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Exporters
{
    internal class ConsoleExporter : IDataExporter
    {
        private readonly Table _table = new();
        private int _headerSet = 0;

        public void ExportRow(IEnumerable<object> values)
        {
            var entries = values
                .Select(o => o.ToString() ?? "NULL")
                .Select(s => new Text(s))
                .ToArray();

            _table.AddRow(entries);
        }

        public void SetHeader(IEnumerable<string> values)
        {
            if (Interlocked.Exchange(ref _headerSet, 1) != 0)
                return; // TODO: Error handling

            foreach (var value in values)
            {
                var column = new TableColumn(value);
                _table.AddColumn(column);
            }
        }

        public void Flush()
        {
            AnsiConsole.Write(_table);
        }
    }
}
