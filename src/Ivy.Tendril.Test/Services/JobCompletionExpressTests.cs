using System;
using System.Collections.Concurrent;
using System.IO;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class JobCompletionExpressTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _promptsRoot;

    public JobCompletionExpressTests()
    {
        _promptsRoot = Path.Combine(_tempDir.Path, "Prompts");
        Directory.CreateDirectory(_promptsRoot);
    }

    public void Dispose() => _tempDir.Dispose();

    private JobCompletionHandler CreateHandler(IConfigService? configService = null) => new(
        configService: configService,
        logger: NullLogger.Instance,
        modelPricingService: null,
        planReaderService: null,
        telemetryService: null,
        planWatcherService: null,
        promptsRoot: _promptsRoot);

    [Fact]
    public void HandleCompletion_WithExpressCreatePlan_ChainsExecutePlan()
    {
        var configService = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var handler = CreateHandler(configService);
        var job = new JobItem
        {
            Id = "J0001",
            Status = JobStatus.Completed,
            PlanFile = "00015-AddFeature",
            TypedArgs = new CreatePlanArgs("Add a feature", "ProjectX", Express: true)
        };

        var jobs = new ConcurrentDictionary<string, JobItem>();
        JobArgsBase? chainedArgs = null;

        handler.HandleCompletion(
            job,
            jobs,
            persistJob: _ => {},
            raiseNotification: _ => {},
            raisePropertyChanged: () => {},
            startJobSkipDepCheck: args =>
            {
                chainedArgs = args;
                return "J0002";
            }
        );

        Assert.NotNull(chainedArgs);
        var execArgs = Assert.IsType<ExecutePlanArgs>(chainedArgs);
        Assert.Equal(Path.Combine(configService.PlanFolder, "00015-AddFeature"), execArgs.FolderPath);
    }

    [Fact]
    public void HandleCompletion_WithNonExpressCreatePlan_DoesNotChainExecutePlan()
    {
        var handler = CreateHandler();
        var job = new JobItem
        {
            Id = "J0001",
            Status = JobStatus.Completed,
            PlanFile = "/plans/00015-AddFeature",
            TypedArgs = new CreatePlanArgs("Add a feature", "ProjectX", Express: false)
        };

        var jobs = new ConcurrentDictionary<string, JobItem>();
        JobArgsBase? chainedArgs = null;

        handler.HandleCompletion(
            job,
            jobs,
            persistJob: _ => {},
            raiseNotification: _ => {},
            raisePropertyChanged: () => {},
            startJobSkipDepCheck: args =>
            {
                chainedArgs = args;
                return "J0002";
            }
        );

        Assert.Null(chainedArgs);
    }
}
