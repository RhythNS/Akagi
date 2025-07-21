namespace Akagi.Connectors.Tatoeba;

internal interface ITatoebaConnector
{
    public Task<string> GetExample(string query, TatoebaUserConfig tatoebaUserConfig);
}
