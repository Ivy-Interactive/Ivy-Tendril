using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ContentInput",
    GlobalName = "IvyTendrilWidgets"
)]
public record ContentInput : WidgetBase<ContentInput>, IAnyInput
{
    [Prop] public bool Disabled { get; set; }
    [Prop] public string? Placeholder { get; set; } = "How can I help you today?";
    [Prop] public string? Invalid { get; set; }
    [Prop] public bool Nullable { get; set; }
    [Prop] public bool AutoFocus { get; set; }
    [Prop] public string? SubmitLabel { get; init; }
    [Prop] public string? UploadUrl { get; init; }

    [Event] public EventHandler<Event<IAnyInput>>? OnBlur { get; set; }
    [Event] public EventHandler<Event<IAnyInput>>? OnFocus { get; set; }

    [Prop] public string Value { get; init; } = "";
    [Prop] public string TranscriptionUrl { get; init; } = "wss://tendril-api.ivy.app/transcribe/ws";
    [Prop] public List<string> Models { get; init; } = new() { "Build", "Edit", "Chat" };
    [Prop] public string SelectedModel { get; init; } = "Build";
    [Prop] public List<AttachedFile> AttachedFiles { get; init; } = new();
    [Prop] public List<string> MenuOptions { get; init; } = new();

    [Event] public Func<Event<ContentInput, SubmitEventArgs>, ValueTask>? OnSubmit { get; init; }
    [Event] public Func<Event<ContentInput, string>, ValueTask>? OnChange { get; init; }
    [Event] public Func<Event<ContentInput, string>, ValueTask>? OnModelChanged { get; init; }
    [Event] public Func<Event<ContentInput, string>, ValueTask>? OnMenuAction { get; init; }
    [Event] public Func<Event<ContentInput, string>, ValueTask>? OnQuickAction { get; init; }
    [Event] public Func<Event<ContentInput, string>, ValueTask>? OnRemoveAttachment { get; init; }
    [Event] public Func<Event<ContentInput, UploadFileEventArgs>, ValueTask>? OnUploadFile { get; init; }

    public Type[] SupportedStateTypes() => [typeof(string)];
}

public record AttachedFile(string Name, string Type, string? Size = null);

public record SubmitEventArgs(string Value, string SelectedModel, List<AttachedFile> AttachedFiles);

public record UploadFileEventArgs(string Name, string? Base64Data = null, string? FilePath = null);

public static class ContentInputExtensions
{
    public static ContentInput MenuOptions(this ContentInput w, List<string> menuOptions) =>
        w with { MenuOptions = menuOptions };

    public static ContentInput MenuOptions(this ContentInput w, params string[] menuOptions) =>
        w with { MenuOptions = menuOptions.ToList() };

    public static ContentInput Placeholder(this ContentInput w, string placeholder) =>
        w with { Placeholder = placeholder };

    public static ContentInput SubmitLabel(this ContentInput w, string? label) =>
        w with { SubmitLabel = label };

    public static ContentInput UploadUrl(this ContentInput w, string? url) =>
        w with { UploadUrl = url };

    public static ContentInput Value(this ContentInput w, string value) =>
        w with { Value = value };

    public static ContentInput TranscriptionUrl(this ContentInput w, string url) =>
        w with { TranscriptionUrl = url };

    public static ContentInput Models(this ContentInput w, List<string> models) =>
        w with { Models = models };

    public static ContentInput SelectedModel(this ContentInput w, string model) =>
        w with { SelectedModel = model };

    public static ContentInput AttachedFiles(this ContentInput w, List<AttachedFile> files) =>
        w with { AttachedFiles = files };

    public static ContentInput OnSubmit(
        this ContentInput w,
        Func<Event<ContentInput, SubmitEventArgs>, ValueTask> handler
    ) => w with { OnSubmit = handler };

    public static ContentInput OnSubmit(this ContentInput w, Action<SubmitEventArgs> handler) =>
        w with
        {
            OnSubmit = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInput OnChange(
        this ContentInput w,
        Func<Event<ContentInput, string>, ValueTask> handler
    ) => w with { OnChange = handler };

    public static ContentInput OnChange(this ContentInput w, Action<string> handler) =>
        w with
        {
            OnChange = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInput Bind(this ContentInput w, IState<string> state) =>
        w with
        {
            Value = state.Value,
            OnChange = e =>
            {
                state.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnUploadFile = w.OnUploadFile ?? (async e =>
            {
                var filePath = e.Value.FilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
                    if (string.IsNullOrEmpty(tendrilHome))
                    {
                        tendrilHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
                    }
                    var tempDir = Path.Combine(tendrilHome, "Temp");
                    Directory.CreateDirectory(tempDir);

                    var fileName = Path.GetFileName(e.Value.Name);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).Replace(" ", "_");
                    var ext = Path.GetExtension(fileName);
                    var uniqueName = $"{nameWithoutExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
                    filePath = Path.Combine(tempDir, uniqueName);

                    var bytes = Convert.FromBase64String(e.Value.Base64Data ?? "");
                    await File.WriteAllBytesAsync(filePath, bytes);
                }

                var fileRef = $" [file: {filePath}]";
                state.Set(state.Value + fileRef);
            })
        };

    public static ContentInput OnUploadFile(
        this ContentInput w,
        Func<Event<ContentInput, UploadFileEventArgs>, Task> handler
    ) => w with { OnUploadFile = async e => await handler(e) };

    public static ContentInput OnModelChanged(
        this ContentInput w,
        Func<Event<ContentInput, string>, ValueTask> handler
    ) => w with { OnModelChanged = handler };

    public static ContentInput OnModelChanged(this ContentInput w, Action<string> handler) =>
        w with
        {
            OnModelChanged = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInput OnMenuAction(
        this ContentInput w,
        Func<Event<ContentInput, string>, ValueTask> handler
    ) => w with { OnMenuAction = handler };

    public static ContentInput OnMenuAction(this ContentInput w, Action<string> handler) =>
        w with
        {
            OnMenuAction = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInput OnQuickAction(
        this ContentInput w,
        Func<Event<ContentInput, string>, ValueTask> handler
    ) => w with { OnQuickAction = handler };

    public static ContentInput OnQuickAction(this ContentInput w, Action<string> handler) =>
        w with
        {
            OnQuickAction = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInput OnRemoveAttachment(
        this ContentInput w,
        Func<Event<ContentInput, string>, ValueTask> handler
    ) => w with { OnRemoveAttachment = handler };

    public static ContentInput OnRemoveAttachment(this ContentInput w, Action<string> handler) =>
        w with
        {
            OnRemoveAttachment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };
}
