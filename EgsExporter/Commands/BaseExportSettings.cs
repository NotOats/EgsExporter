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
        [CommandArgument(0, "[scenario_path]")]
        [Description("Path to the scenario directory to export from")]
        public string? ScenarioPath { get; set; }

        [CommandOption("-o|--output")]
        [Description("Changes the export output directory")]
        [DefaultValue("./output")]
        public string? OutputPath { get; set; }

        [CommandOption("-t|--type")]
        [Description("How to export the data")]
        [DefaultValue(ExportType.Console)]
        public ExportType ExportType { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrEmpty(ScenarioPath))
                return ValidationResult.Error("ScenarioPath is empty");

            if (!Directory.Exists(ScenarioPath))
                return ValidationResult.Error("ScenarioPath does not exist");

            return base.Validate();
        }
    }

    public enum ExportType
    {
        Console,
        Csv,
        Sqlite
    }
}
