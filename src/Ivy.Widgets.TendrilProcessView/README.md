# Ivy.Widget.Tendril.ProcessView

A process flow visualization widget for Ivy Tendril showing the plan lifecycle: **Create Plan → Drafts → Review**.

## Props

| Prop | Type | Description |
|------|------|-------------|
| `DraftCount` | `int` | Number of plans in draft state |
| `ReviewCount` | `int` | Number of plans in review state |
| `CreatingPlansCount` | `int` | Number of plans currently being created (shows spinner when > 0) |
| `UpdatingPlansCount` | `int` | Number of plans currently being updated (shows spinner when > 0) |
| `ExecutingPlansCount` | `int` | Number of plans currently executing (shows spinner when > 0) |

## Events

| Event | Description |
|-------|-------------|
| `OnCreate` | Fired when clicking the "Create Plan" box |
| `OnDrafts` | Fired when clicking the "Drafts" box |
| `OnReview` | Fired when clicking the "Review" box |
| `OnJobs` | Fired when clicking any of the arrow count numbers |

## Usage

```csharp
new TendrilProcessView()
    .DraftCount(5)
    .ReviewCount(6)
    .CreatingPlansCount(1)
    .UpdatingPlansCount(5)
    .ExecutingPlansCount(5)
    .OnCreate(() => Navigate("/create"))
    .OnDrafts(() => Navigate("/drafts"))
    .OnReview(() => Navigate("/review"))
    .OnJobs(() => Navigate("/jobs"));
```
