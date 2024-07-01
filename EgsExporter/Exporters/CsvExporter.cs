using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Exporters
{
    internal class CsvExporter : IDataExporter, IDisposable
    {
        private readonly CsvWriter _csv;
        private int _headerSet = 0;
        private int _disposed = 0;

        public CsvExporter(string file)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };

            var stream = new StreamWriter(file, append: false);
            _csv = new CsvWriter(stream, config);
        }

        public void Dispose()
        {
            Flush();
            _csv.Dispose();

            _disposed = 0;
        }

        public void ExportRow(IEnumerable<object> values)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            foreach (var value in values)
            {
                _csv.WriteField(value);
            }

            _csv.NextRecord();
        }

        public void Flush()
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            _csv.Flush();
        }

        public void SetHeader(IEnumerable<string> values)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            if (Interlocked.Exchange(ref _headerSet, 1) != 0)
                return; // TODO: Error handling

            foreach (var value in values)
            {
                _csv.WriteField(value);
            }

            _csv.NextRecord();
        }
    }
}
