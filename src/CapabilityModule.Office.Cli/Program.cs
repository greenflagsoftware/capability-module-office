using System.CommandLine;
using CapabilityModule.Office.Cli.Commands;

namespace CapabilityModule.Office.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Capability Modules Office CLI — filesystem and document utilities.")
        {
            new ReadCommand().Command(),
            new WriteCommand().Command(),
            new ListCommand().Command(),
            new DocxCommand().Command(),
            new SearchCommand().Command(),
        };

        rootCommand.SetHandler(() =>
        {
            Console.Error.WriteLine("Use --help to see available commands.");
            return Task.FromResult(1);
        });

        return await rootCommand.InvokeAsync(args);
    }
}