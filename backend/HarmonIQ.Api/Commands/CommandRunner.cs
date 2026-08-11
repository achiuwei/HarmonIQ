using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Commands;

/// <summary>
/// Dispatch seam between the CLI and the web host. Program.cs calls
/// <see cref="TryRunAsync"/> once, after migrations and before <c>app.Run()</c>: if
/// <c>args[0]</c> matches a registered <see cref="IHarmonIQCommand.Name"/>, it is resolved
/// from DI, run, and its exit code returned; otherwise this returns <c>null</c> and the
/// caller falls through to starting the web host normally. Commands are registered as
/// <c>IHarmonIQCommand</c> from their own service module (see <see cref="HarmonIQ.Api.Infrastructure.IServiceModule"/>),
/// so later tasks add commands without ever touching Program.cs.
/// </summary>
public static class CommandRunner
{
    public static async Task<int?> TryRunAsync(string[] args, IServiceProvider services, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var commands = services.GetServices<IHarmonIQCommand>()
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        if (args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp(commands);
            return 0;
        }

        var match = commands.FirstOrDefault(c => c.Name == args[0]);
        if (match is null)
        {
            return null;
        }

        return await match.RunAsync(args[1..], ct);
    }

    private static void PrintHelp(IReadOnlyCollection<IHarmonIQCommand> commands)
    {
        Console.WriteLine("HarmonIQ commands:");
        if (commands.Count == 0)
        {
            Console.WriteLine("  (none registered)");
        }
        foreach (var command in commands)
        {
            Console.WriteLine($"  {command.Name,-16} {command.Description}");
        }
        Console.WriteLine();
        Console.WriteLine("Run with no arguments to start the web host instead.");
    }
}
