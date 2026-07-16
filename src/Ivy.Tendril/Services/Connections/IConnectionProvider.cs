using System.Net.Http;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Connections;

public interface IConnectionProvider
{
    string ProviderName { get; }
    Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connectionString, HttpClient client);
    Task<(bool Success, string Result)> ExecuteActionAsync(string connectionString, string action, string argsJson, HttpClient client);
}
