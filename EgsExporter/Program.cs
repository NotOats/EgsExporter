using EgsExporter.Commands;
using EgsLib.ConfigFiles;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<ExportTraders>("traders")
        .WithDescription("Exports TraderNPCConfig.ecf")
        .WithExample("traders", @"""C:\Path\To\Empyrion - Galactic Survival""", "--type=Csv")
        .WithExample("traders", @"""C:\Path\To\Empyrion - Galactic Survival""", "--type=Console", "--group-items=true");

    config.AddCommand<ExportTraderSpreadsheet>("trader-spreadsheet")
        .WithDescription("Exports a fully functional Trader spreadsheet")
        .WithExample("trader-spreadsheet", @"""C:\Path\To\Empyrion - Galactic Survival""", "--type=Csv", "--output=\"./vanilla\"");

#if DEBUG
    config.PropagateExceptions();
    config.ValidateExamples();
#endif
});

await app.RunAsync(args);