using Ivy.Helpers;

namespace Ivy.Tendril.Helpers;

public static class BuilderExtensions
{
    public static IBuilder<TModel> TitleCase<TModel>(this IBuilderFactory<TModel> factory)
    {
        return factory.Func<TModel, string>(value => StringHelper.ToTitleCase(value));
    }
}
