using Ivy;

namespace Ivy.Tendril.Apps.Orchestration
{
    [App(group: ["Orchestration"], order: 10, groupExpanded: true)]
    public class _Index : ViewBase
    {
        public override object Build() => null!;
    }
}

namespace Ivy.Tendril.Apps.Automations
{
    [App(group: ["Automations"], order: 20, groupExpanded: true)]
    public class _Index : ViewBase
    {
        public override object Build() => null!;
    }
}

namespace Ivy.Tendril.Apps.Overview
{
    [App(group: ["Overview"], order: 40, groupExpanded: true)]
    public class _Index : ViewBase
    {
        public override object Build() => null!;
    }
}

namespace Ivy.Tendril.Apps.Other
{
    [App(group: ["Other"], order: 50, groupExpanded: true)]
    public class _Index : ViewBase
    {
        public override object Build() => null!;
    }
}
