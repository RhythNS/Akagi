namespace Akagi.Connectors.Desu;

internal interface IDesuConnector
{
    public string Lookup(string word, DesuUserConfig userConfig);
}
