using HarmonIQ.Api.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HarmonIQ.Tests;

public class CommandRunnerTests
{
    private class FakeCommand : IHarmonIQCommand
    {
        public string Name => "fake";
        public string Description => "A fake command for tests.";
        public List<string[]> Calls { get; } = [];
        public int ExitCode { get; set; } = 42;

        public Task<int> RunAsync(string[] args, CancellationToken ct)
        {
            Calls.Add(args);
            return Task.FromResult(ExitCode);
        }
    }

    private static IServiceProvider BuildServices(IHarmonIQCommand command)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHarmonIQCommand>(command);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DispatchesRegisteredCommandByNameWithRemainingArgsAndReturnsItsExitCode()
    {
        var fake = new FakeCommand { ExitCode = 7 };
        var services = BuildServices(fake);

        var code = await CommandRunner.TryRunAsync(["fake", "--foo", "bar"], services, default);

        Assert.Equal(7, code);
        Assert.Single(fake.Calls);
        Assert.Equal(["--foo", "bar"], fake.Calls[0]);
    }

    [Fact]
    public async Task UnknownFirstArgReturnsNullSoTheWebHostStartsNormally()
    {
        var services = BuildServices(new FakeCommand());

        var code = await CommandRunner.TryRunAsync(["not-a-command"], services, default);

        Assert.Null(code);
    }

    [Fact]
    public async Task NoArgsReturnsNull()
    {
        var services = BuildServices(new FakeCommand());

        var code = await CommandRunner.TryRunAsync([], services, default);

        Assert.Null(code);
    }

    [Fact]
    public async Task HelpListsRegisteredCommandNamesAndReturnsZeroWithoutRunningThem()
    {
        var fake = new FakeCommand();
        var services = BuildServices(fake);

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int? code;
        try
        {
            code = await CommandRunner.TryRunAsync(["--help"], services, default);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, code);
        Assert.Empty(fake.Calls);
        Assert.Contains("fake", writer.ToString());
    }
}
