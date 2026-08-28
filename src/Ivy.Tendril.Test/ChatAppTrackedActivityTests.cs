using System;
using System.Collections.Generic;
using System.IO;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatAppTrackedActivityTests
{
    [Fact]
    public void AgentLaunchHelper_GetEnvironment_SetsTendrilChatSessionId()
    {
        var configService = new ConfigService(new TendrilSettings(), Path.GetTempPath());
        var env = AgentLaunchHelper.GetEnvironment(configService, "claude", "sess-12345");

        Assert.True(env.ContainsKey("TENDRIL_CHAT_SESSION_ID"));
        Assert.Equal("sess-12345", env["TENDRIL_CHAT_SESSION_ID"]);
    }

    [Fact]
    public void JobStartCommand_BuildJobArgs_UsesExplicitChatSessionId()
    {
        var settings = new JobStartSettings
        {
            JobType = "CreatePlan",
            Description = "Test description",
            Project = "Tendril",
            ChatSessionId = "chat-custom-id"
        };

        var args = JobStartCommand.BuildJobArgs(settings);

        Assert.NotNull(args);
        Assert.Equal("chat-custom-id", args.ChatSessionId);
    }

    [Fact]
    public void JobStartCommand_BuildJobArgs_FallsBackToEnvironmentVariable()
    {
        var oldVal = Environment.GetEnvironmentVariable("TENDRIL_CHAT_SESSION_ID");
        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_CHAT_SESSION_ID", "env-session-abc");

            var settings = new JobStartSettings
            {
                JobType = "CreatePlan",
                Description = "Test description",
                Project = "Tendril"
            };

            var args = JobStartCommand.BuildJobArgs(settings);

            Assert.NotNull(args);
            Assert.Equal("env-session-abc", args.ChatSessionId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_CHAT_SESSION_ID", oldVal);
        }
    }

    [Fact]
    public void PlanDatabaseService_UpsertAndMapJob_PersistsChatSessionId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilDbTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dbPath = Path.Combine(tempDir, "tendril.db");
            using var dbService = new PlanDatabaseService(dbPath, NullLogger<PlanDatabaseService>.Instance);

            var job = new JobItem
            {
                Id = "00999",
                Type = "ExecutePlan",
                PlanFile = "00013-TestPlan",
                Project = "TestProj",
                Status = JobStatus.Running,
                ChatSessionId = "chat-session-xyz"
            };

            dbService.UpsertJob(job);

            var loaded = dbService.GetJobById("00999");
            Assert.NotNull(loaded);
            Assert.Equal("00999", loaded!.Id);
            Assert.Equal("chat-session-xyz", loaded.ChatSessionId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ChatWidgetDto_PropertiesAreSetCorrectly()
    {
        var jobDto = new ChatTrackedJobDto("00001", "CreatePlan", "00013", "Test Plan", "Running", "Writing...", "2026-08-28T12:00:00Z", "15s");
        var planDto = new ChatTrackedPlanDto("00013", "Test Plan", "00013-TestPlan", "Executing");

        Assert.Equal("00001", jobDto.Id);
        Assert.Equal("CreatePlan", jobDto.Type);
        Assert.Equal("00013", jobDto.PlanId);
        Assert.Equal("Test Plan", jobDto.PlanTitle);
        Assert.Equal("Running", jobDto.Status);
        Assert.Equal("Writing...", jobDto.StatusMessage);
        Assert.Equal("15s", jobDto.Duration);

        Assert.Equal("00013", planDto.Id);
        Assert.Equal("Test Plan", planDto.Title);
        Assert.Equal("00013-TestPlan", planDto.FolderName);
        Assert.Equal("Executing", planDto.Status);
    }
}
