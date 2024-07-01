using EgsExporter.Exporters;
using EgsLib.ConfigFiles;
using EgsLib.ConfigFiles.Ecf;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace EgsExporter.Commands
{
    public class ExportTradersConfig : BaseExportSettings
    {
        [CommandOption("--trader-file", IsHidden = true)]
        [Description("The trader file to export's full path")]
        public string? TraderFilePath { get; set; }

        public override ValidationResult Validate()
        {
            var b = base.Validate();
            if (!b.Successful)
                return b;

            TraderFilePath ??= Path.Combine(ScenarioPath!, @"Content\Configuration\TraderNPCConfig.ecf");
            if (!File.Exists(TraderFilePath))
                return ValidationResult.Error("Trader file does not exist");

            return ValidationResult.Success();
        }
    }

    public class ExportTraders : Command<ExportTradersConfig>
    {
        public override int Execute(CommandContext context, ExportTradersConfig settings)
        {
            IDataExporter? exporter = settings.ExportType switch
            {
                ExportType.Console => new ConsoleExporter(),
                _ => null
            };

            if (exporter == null)
            {
                AnsiConsole.WriteLine($"Error: Unsupported data exporter {settings.ExportType}");
                return 1;
            }

            exporter.SetHeader(["Name", "Discount", "Type", "Item", "Price", "Quantity"]);

            // TODO: Add localization support
            var ecf = new EcfFile(settings.TraderFilePath!);
            var objects = ecf.ParseObjects();

            if (objects == null)
            {
                AnsiConsole.WriteLine($"Error: Failed to parase traders file at {settings.TraderFilePath}");
                return 1;
            }

            foreach (var obj in objects)
            {
                var trader = new Trader(obj);

                // Buyables
                foreach (var buyable in trader.Buys)
                {
                    var value = buyable.BuyMarketFactor ? $"mf={buyable.BuyValue}" : buyable.BuyValue.ToString();

                    exporter.ExportRow([trader.Name, trader.Discount ?? 1, "Buy", buyable.Name, value, buyable.BuyAmount]);
                }

                // Sellables
                foreach (var sellable in trader.Sells)
                {
                    var value = sellable.SellMarketFactor ? $"mf={sellable.BuyValue}" : sellable.BuyValue.ToString();

                    exporter.ExportRow([trader.Name, trader.Discount ?? 1, "Sell", sellable.Name, value, sellable.SellAmount]);
                }
            }

            exporter.Flush();

            return 0;
        }
    }
}
