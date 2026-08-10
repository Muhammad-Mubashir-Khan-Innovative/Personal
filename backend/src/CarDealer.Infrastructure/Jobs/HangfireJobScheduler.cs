using CarDealer.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace CarDealer.Infrastructure.Jobs;

/// <summary>
/// Durable job scheduling backed by Hangfire's SQL Server store (criterion H4, H5).
/// </summary>
/// <remarks>
/// Hangfire is referenced only here. Callers depend on
/// <see cref="IBackgroundJobScheduler"/>, so replacing the job system is a one-class change
/// (master prompt section 4: "behind a replaceable abstraction").
///
/// Only Hangfire's free core is used. Batches and continuations are Hangfire Pro features
/// and are deliberately avoided - see open item O14.
/// </remarks>
public sealed class HangfireJobScheduler : IBackgroundJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfireJobScheduler(IBackgroundJobClient client) => _client = client;

    public string Enqueue<TJob>(string argument) where TJob : IBackgroundJob
        => _client.Enqueue<JobRunner<TJob>>(runner => runner.RunAsync(argument, CancellationToken.None));

    public string Schedule<TJob>(string argument, TimeSpan delay) where TJob : IBackgroundJob
        => _client.Schedule<JobRunner<TJob>>(
            runner => runner.RunAsync(argument, CancellationToken.None), delay);
}

/// <summary>
/// Adapter that lets Hangfire resolve and invoke an <see cref="IBackgroundJob"/> without
/// the job type itself knowing Hangfire exists.
/// </summary>
public sealed class JobRunner<TJob> where TJob : IBackgroundJob
{
    private readonly TJob _job;

    public JobRunner(TJob job) => _job = job;

    public Task RunAsync(string argument, CancellationToken ct) => _job.ExecuteAsync(argument, ct);
}

/// <summary>
/// A trivial job used to prove the pipeline works end to end, including surviving a restart
/// (criterion H5). Real jobs arrive with the features that need them.
/// </summary>
public sealed class EchoJob : IBackgroundJob
{
    private readonly ILogger<EchoJob> _logger;

    public EchoJob(ILogger<EchoJob> logger) => _logger = logger;

    public Task ExecuteAsync(string argument, CancellationToken ct)
    {
        _logger.LogInformation("EchoJob executed with argument {JobArgument}", argument);
        return Task.CompletedTask;
    }
}
