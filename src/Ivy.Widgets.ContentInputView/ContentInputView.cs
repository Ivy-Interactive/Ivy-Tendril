using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ivy.Widgets.ContentInputView;

[ExternalWidget(
    "frontend/dist/Ivy_Widgets_ContentInputView.js",
    StylePath = "frontend/dist/ivy-widgets-contentinputview.css",
    ExportName = "ContentInputView",
    GlobalName = "Ivy_Widgets_ContentInputView"
)]
public record ContentInputView : WidgetBase<ContentInputView>, IAnyInput
{
    [Prop] public bool Disabled { get; set; }
    [Prop] public string? Placeholder { get; set; } = "How can I help you today?";
    [Prop] public string? Invalid { get; set; }
    [Prop] public bool Nullable { get; set; }
    [Prop] public bool AutoFocus { get; set; }
    [Prop] public string? SubmitLabel { get; init; }

    [Event] public EventHandler<Event<IAnyInput>>? OnBlur { get; set; }
    [Event] public EventHandler<Event<IAnyInput>>? OnFocus { get; set; }

    [Prop] public string Value { get; init; } = "";
    [Prop] public string TranscriptionUrl { get; init; } = "wss://tendril-api.ivy.app/transcribe/ws";
    [Prop] public List<string> Models { get; init; } = new() { "Build", "Edit", "Chat" };
    [Prop] public string SelectedModel { get; init; } = "Build";
    [Prop] public List<AttachedFile> AttachedFiles { get; init; } = new();

    [Event] public Func<Event<ContentInputView, SubmitEventArgs>, ValueTask>? OnSubmit { get; init; }
    [Event] public Func<Event<ContentInputView, string>, ValueTask>? OnChange { get; init; }
    [Event] public Func<Event<ContentInputView, string>, ValueTask>? OnModelChanged { get; init; }
    [Event] public Func<Event<ContentInputView, string>, ValueTask>? OnMenuAction { get; init; }
    [Event] public Func<Event<ContentInputView, string>, ValueTask>? OnQuickAction { get; init; }
    [Event] public Func<Event<ContentInputView, string>, ValueTask>? OnRemoveAttachment { get; init; }
    [Event] public Func<Event<ContentInputView, UploadFileEventArgs>, ValueTask>? OnUploadFile { get; init; }

    public Type[] SupportedStateTypes() => [typeof(string)];
}

public record AttachedFile(string Name, string Type, string? Size = null);

public record SubmitEventArgs(string Value, string SelectedModel, List<AttachedFile> AttachedFiles);

public record UploadFileEventArgs(string Name, string Base64Data);

public static class ContentInputViewExtensions
{
    public static ContentInputView Placeholder(this ContentInputView w, string placeholder) =>
        w with { Placeholder = placeholder };

    public static ContentInputView SubmitLabel(this ContentInputView w, string? label) =>
        w with { SubmitLabel = label };

    public static ContentInputView Value(this ContentInputView w, string value) =>
        w with { Value = value };

    public static ContentInputView TranscriptionUrl(this ContentInputView w, string url) =>
        w with { TranscriptionUrl = url };

    public static ContentInputView Models(this ContentInputView w, List<string> models) =>
        w with { Models = models };

    public static ContentInputView SelectedModel(this ContentInputView w, string model) =>
        w with { SelectedModel = model };

    public static ContentInputView AttachedFiles(this ContentInputView w, List<AttachedFile> files) =>
        w with { AttachedFiles = files };

    public static ContentInputView OnSubmit(
        this ContentInputView w,
        Func<Event<ContentInputView, SubmitEventArgs>, ValueTask> handler
    ) => w with { OnSubmit = handler };

    public static ContentInputView OnSubmit(this ContentInputView w, Action<SubmitEventArgs> handler) =>
        w with
        {
            OnSubmit = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInputView OnChange(
        this ContentInputView w,
        Func<Event<ContentInputView, string>, ValueTask> handler
    ) => w with { OnChange = handler };

    public static ContentInputView OnChange(this ContentInputView w, Action<string> handler) =>
        w with
        {
            OnChange = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInputView Bind(this ContentInputView w, IState<string> state) =>
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
                var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
                if (string.IsNullOrEmpty(tendrilHome))
                {
                    tendrilHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
                }
                var attachmentsDir = Path.Combine(tendrilHome, "Attachments");
                Directory.CreateDirectory(attachmentsDir);

                var fileName = Path.GetFileName(e.Value.Name);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                var uniqueName = $"{nameWithoutExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
                var filePath = Path.Combine(attachmentsDir, uniqueName);

                var bytes = Convert.FromBase64String(e.Value.Base64Data);
                await File.WriteAllBytesAsync(filePath, bytes);

                var fileRef = $" [file: {filePath}]";
                state.Set(state.Value + fileRef);
            })
        };

    public static ContentInputView OnUploadFile(
        this ContentInputView w,
        Func<Event<ContentInputView, UploadFileEventArgs>, Task> handler
    ) => w with { OnUploadFile = async e => await handler(e) };

    public static ContentInputView OnModelChanged(
        this ContentInputView w,
        Func<Event<ContentInputView, string>, ValueTask> handler
    ) => w with { OnModelChanged = handler };

    public static ContentInputView OnModelChanged(this ContentInputView w, Action<string> handler) =>
        w with
        {
            OnModelChanged = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInputView OnMenuAction(
        this ContentInputView w,
        Func<Event<ContentInputView, string>, ValueTask> handler
    ) => w with { OnMenuAction = handler };

    public static ContentInputView OnMenuAction(this ContentInputView w, Action<string> handler) =>
        w with
        {
            OnMenuAction = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInputView OnQuickAction(
        this ContentInputView w,
        Func<Event<ContentInputView, string>, ValueTask> handler
    ) => w with { OnQuickAction = handler };

    public static ContentInputView OnQuickAction(this ContentInputView w, Action<string> handler) =>
        w with
        {
            OnQuickAction = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static ContentInputView OnRemoveAttachment(
        this ContentInputView w,
        Func<Event<ContentInputView, string>, ValueTask> handler
    ) => w with { OnRemoveAttachment = handler };

    public static ContentInputView OnRemoveAttachment(this ContentInputView w, Action<string> handler) =>
        w with
        {
            OnRemoveAttachment = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };
}
