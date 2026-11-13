using Microsoft.Extensions.DependencyInjection;
using TreasuryKit.BuildingBlocks.Application;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Tests;

public sealed class DispatcherTests
{
    private readonly IDispatcher _dispatcher;

    public DispatcherTests()
    {
        var services = new ServiceCollection()
            .AddHandlersFromAssembly(typeof(DispatcherTests).Assembly)
            .AddScoped<IDispatcher, Dispatcher>();
        _dispatcher = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task Routes_command_with_response()
    {
        var result = await _dispatcher.SendAsync(new DoubleIt(21), TestContext.Current.CancellationToken);

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task Routes_command_without_response()
    {
        var result = await _dispatcher.SendAsync(new Fail(), TestContext.Current.CancellationToken);

        Assert.Equal("Test.Failed", result.Error.Code);
    }

    [Fact]
    public async Task Routes_query()
    {
        var result = await _dispatcher.QueryAsync(new Echo("hi"), TestContext.Current.CancellationToken);

        Assert.Equal("hi", result.Value);
    }

    public sealed record DoubleIt(int Value) : ICommand<int>;

    public sealed record Fail : ICommand;

    public sealed record Echo(string Text) : IQuery<string>;

    internal sealed class DoubleHandler : ICommandHandler<DoubleIt, int>
    {
        public Task<Result<int>> HandleAsync(DoubleIt command, CancellationToken cancellationToken) =>
            Task.FromResult<Result<int>>(command.Value * 2);
    }

    internal sealed class FailHandler : ICommandHandler<Fail>
    {
        public Task<Result> HandleAsync(Fail command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure(new Error("Test.Failed", "Failed.")));
    }

    internal sealed class EchoHandler : IQueryHandler<Echo, string>
    {
        public Task<Result<string>> HandleAsync(Echo query, CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>(query.Text);
    }
}
