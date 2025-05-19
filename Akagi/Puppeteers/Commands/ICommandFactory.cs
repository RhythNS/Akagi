namespace Akagi.Puppeteers.Commands;

internal interface ICommandFactory
{
    public T Create<T>() where T : Command;
}
