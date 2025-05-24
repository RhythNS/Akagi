namespace Akagi.Communication;

internal abstract class Document
{
    public abstract string Name { get; }

    private ICommunicator? _communicator;
    protected ICommunicator Communicator
    {
        get
        {
            if (_communicator == null)
            {
                throw new InvalidOperationException("Document has not been initialized with a communicator.");
            }
            return _communicator;
        }
    }

    public void Init(ICommunicator communicator)
    {
        _communicator = communicator;
    }

    public abstract Task<MemoryStream?> GetStream();
}
