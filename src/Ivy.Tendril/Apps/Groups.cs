using Ivy;

namespace Ivy.Tendril.Apps.AgentsGroup
{
    [App(group: ["Agents"], order: 10, groupExpanded: true)]
    public class _Index : ViewBase
    {
        public override object Build() => null!;
    }
}

namespace Ivy.Tendril.Apps.FlowsGroup
{
    [App(group: ["Flows"], order: 20, groupExpanded: true)]
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
