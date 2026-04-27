namespace Ivy.Tendril.Services.SessionParsers;

public interface ISessionParser
{
    string Name { get; }
    CostCalculation Parse(string filePath, IModelPricingService pricingService);
}
