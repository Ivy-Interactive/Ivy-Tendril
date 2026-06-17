# Ivy.Widgets.ContentInputView

A feature-rich interactive content input widget for Tendril with voice transcription support.

## Features
- **Prompt Area**: Rich text area with auto-growing height.
- **Attachments**: Display list of attachments with names and sizes at the top.
- **Control Bar**:
  - `+` menu with configurable actions.
  - Model selector dropdown.
  - Microphone button supporting direct WebSocket-based PCM16 audio transcription.
  - Submit button.
- **Quick Actions**: Pill buttons for common interactions.

## Usage

```csharp
var inputWidget = new ContentInputView()
    .Placeholder("How can I help you today?")
    .Models(new() { "Build", "Edit", "Chat" })
    .OnSubmit(args => {
        Console.WriteLine($"Submitted value: {args.Value}");
    });
```
