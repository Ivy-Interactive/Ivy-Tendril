using System.Reactive.Disposables;

namespace Ivy.Tendril.AppShell;

internal class PluginDialogHost(ITendrilPluginContributions pluginContext) : ViewBase
{
    public override object? Build()
    {
        var activeDialogId = UseState<string?>(null);

        UseEffect(() =>
        {
            void OnOpen(string id) => activeDialogId.Set(id);
            pluginContext.DialogOpenRequested += OnOpen;
            return Disposable.Create(() => pluginContext.DialogOpenRequested -= OnOpen);
        });

        if (activeDialogId.Value is not { } id) return null;
        if (!pluginContext.DialogFactories.TryGetValue(id, out var factory)) return null;

        var openState = new DialogOpenState(activeDialogId);
        return factory(openState);
    }

    private sealed class DialogOpenState(IState<string?> activeDialogId) : IState<bool>
    {
        public bool Value
        {
            get => activeDialogId.Value != null;
            set { if (!value) activeDialogId.Set(null); }
        }

        public bool Set(bool value)
        {
            if (!value) activeDialogId.Set(null);
            return value;
        }

        public bool Set(Func<bool, bool> setter)
        {
            var newValue = setter(Value);
            if (!newValue) activeDialogId.Set(null);
            return newValue;
        }

        public bool Reset()
        {
            activeDialogId.Set(null);
            return false;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            return activeDialogId.Subscribe(new MappedObserver(observer));
        }

        public IDisposable SubscribeAny(Action action) => activeDialogId.SubscribeAny(action);
        public IDisposable SubscribeAny(Action<object?> action) => activeDialogId.SubscribeAny(v => action(v != null));
        public Type GetStateType() => typeof(bool);
        public object? GetValueAsObject() => Value;
        public IEffectTrigger ToTrigger() => activeDialogId.ToTrigger();
        public void Dispose() { }

        private sealed class MappedObserver(IObserver<bool> inner) : IObserver<string?>
        {
            public void OnNext(string? value) => inner.OnNext(value != null);
            public void OnError(Exception error) => inner.OnError(error);
            public void OnCompleted() => inner.OnCompleted();
        }
    }
}
