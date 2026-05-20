using Ivy.Tendril.Apps.Views.Sheets;

namespace Ivy.Tendril.Test.Services;

public class FileSheetTests
{
    [Fact]
    public void CreateLinkClickHandler_CapturesFilePath_WhenFileUrlProvided()
    {
        var state = new TestState<string?>(null);
        var handler = FileSheet.CreateLinkClickHandler(state);

        handler("file:///C:/some/path/file.cs");

        Assert.Equal("C:/some/path/file.cs", state.Value);
    }

    [Fact]
    public void CreateLinkClickHandler_DoesNotSetFileState_ForUnrecognizedSchemes()
    {
        var state = new TestState<string?>(null);
        var handler = FileSheet.CreateLinkClickHandler(state);

        handler("ftp://example.com/page");

        Assert.Null(state.Value);
    }

    [Fact]
    public void CreateLinkClickHandler_IsCaseInsensitive()
    {
        var state = new TestState<string?>(null);
        var handler = FileSheet.CreateLinkClickHandler(state);

        handler("FILE:///D:/test/readme.md");

        Assert.Equal("D:/test/readme.md", state.Value);
    }

    [Fact]
    public void CreateLinkClickHandler_InvokesPlanCallback_WhenPlanUrlProvided()
    {
        var fileState = new TestState<string?>(null);
        var planIdCaptured = 0;
        var handler = FileSheet.CreateLinkClickHandler(
            fileState,
            planId => planIdCaptured = planId);

        handler("plan://03156");

        Assert.Equal(3156, planIdCaptured);
        Assert.Null(fileState.Value);
    }

    [Fact]
    public void CreateLinkClickHandler_HandlesPlanIdWithLeadingZeros()
    {
        var fileState = new TestState<string?>(null);
        var planIdCaptured = 0;
        var handler = FileSheet.CreateLinkClickHandler(
            fileState,
            planId => planIdCaptured = planId);

        handler("plan://00123");

        Assert.Equal(123, planIdCaptured);
    }

    [Fact]
    public void CreateLinkClickHandler_IgnoresInvalidPlanIds()
    {
        var fileState = new TestState<string?>(null);
        var callbackInvoked = false;
        var handler = FileSheet.CreateLinkClickHandler(
            fileState,
            _ => callbackInvoked = true);

        handler("plan://notanumber");

        Assert.False(callbackInvoked);
        Assert.Null(fileState.Value);
    }

    private class TestState<T> : IState<T>
    {
        private readonly T _initial;

        public TestState(T initial)
        {
            _initial = initial;
            Value = initial;
        }

        public T Value { get; set; }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public T Set(T value)
        {
            return Value = value;
        }

        public T Set(Func<T, T> setter)
        {
            return Value = setter(Value);
        }

        public T Reset()
        {
            return Value = _initial;
        }

        public IDisposable SubscribeAny(Action action)
        {
            throw new NotImplementedException();
        }

        public IDisposable SubscribeAny(Action<object?> action)
        {
            throw new NotImplementedException();
        }

        public Type GetStateType()
        {
            return typeof(T);
        }

        public object? GetValueAsObject()
        {
            return Value;
        }

        public IEffectTrigger ToTrigger()
        {
            throw new NotImplementedException();
        }
    }
}
