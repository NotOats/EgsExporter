using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Exporters
{
    internal interface IDataExporter
    {
        void SetHeader(IEnumerable<string> values);
        void ExportRow(IEnumerable<object> values);
        void Flush();
    }
}
