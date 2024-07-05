using EgsExporter.Exporters;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EgsExporter.Commands
{
    public class BaseExportSettings : CommandSettings
    {
        [CommandArgument(0, "<scenario_path>")]
        [Description("Path to the scenario directory to export from")]
        public string ScenarioPath { get; set; } = string.Empty;

        [CommandOption("-o|--output")]
        [Description("Changes the export output directory")]
        [DefaultValue("./output")]
        public string OutputPath { get; set; } = "./output";

        [CommandOption("-t|--type")]
        [Description("How to export the data")]
        [DefaultValue(ExportType.Console)]
        public ExportType ExportType { get; set; } = ExportType.Console;

        public override ValidationResult Validate()
        {
            if (string.IsNullOrEmpty(ScenarioPath))
                return ValidationResult.Error("ScenarioPath is empty");

            if (!Directory.Exists(ScenarioPath))
                return ValidationResult.Error("ScenarioPath does not exist");

            return base.Validate();
        }

        internal IDataExporter? CreateExporter(string arg)
        {
            if (ExportType != ExportType.Console && !Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            IDataExporter? exporter = ExportType switch
            {
                ExportType.Console => new ConsoleExporter(),
                ExportType.Csv => new CsvExporter(Path.Combine(OutputPath!, $"{arg}.csv")),
                _ => null
            };

            return exporter;
        }
    }

    public enum ExportType
    {
        Console,
        Csv,
        Sqlite
    }
}
