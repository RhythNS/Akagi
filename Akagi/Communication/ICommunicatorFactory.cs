namespace Akagi.Communication;

internal interface ICommunicatorFactory
{
    public ICommunicator? Create(string name);

    public IEnumerable<string> GetAvailableCommunicators();
}
