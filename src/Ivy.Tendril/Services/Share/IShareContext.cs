namespace Ivy.Tendril.Services.Share;

public interface IShareContext
{
    bool IsShareMode { get; }
    string Persona { get; }
    void SetPersona(string persona);
}
