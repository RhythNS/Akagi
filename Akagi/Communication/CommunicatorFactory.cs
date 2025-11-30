namespace Akagi.Communication;

internal interface ICommunicatorFactory
{
    public ICommunicator? Create(string name);

    public IEnumerable<string> GetAvailableCommunicators();
}

internal class CommunicatorFactory : ICommunicatorFactory
{
    private readonly IEnumerable<ICommunicator> _communicators;

    public CommunicatorFactory(IEnumerable<ICommunicator> communicators)
    {
        _communicators = communicators;
    }
    
    public ICommunicator? Create(string name)
    {
        return _communicators.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    public IEnumerable<string> GetAvailableCommunicators()
    {
        return _communicators.Select(c => c.Name);
    }
}
