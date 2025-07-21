using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Akagi.Connectors.Tatoeba;

internal class TatoebaConnector : ITatoebaConnector
{
    internal class Options
    {
        public string Url { get; set; } = string.Empty;
    }

    private readonly string _url;

    public TatoebaConnector(IOptionsMonitor<Options> options)
    {
        _url = options.CurrentValue.Url;
    }

    public async Task<string> GetExample(string query, TatoebaUserConfig tatoebaUserConfig)
    {
        using HttpClient httpClient = new();

        UriBuilder builder = new($"{_url}/sentences");
        System.Collections.Specialized.NameValueCollection queryParams = HttpUtility.ParseQueryString(builder.Query);

        queryParams["lang"] = tatoebaUserConfig.TargetLanguage;
        queryParams["trans:lang"] = tatoebaUserConfig.TranslationLanguage;
        queryParams["q"] = query;
        queryParams["sort"] = "relevance";
        queryParams["is_unapproved"] = "no";
        queryParams["limit"] = tatoebaUserConfig.MaxSentences.ToString();

        builder.Query = queryParams.ToString();

        HttpRequestMessage request = new(HttpMethod.Get, builder.Uri);

        HttpResponseMessage responseMessage = await httpClient.SendAsync(request);
        if (!responseMessage.IsSuccessStatusCode)
        {
            throw new Exception($"Request failed with status code {responseMessage.StatusCode}: {await responseMessage.Content.ReadAsStringAsync()}");
        }

        string content = await responseMessage.Content.ReadAsStringAsync();

        TatoebaResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<TatoebaResponse>(content);
        }
        catch (JsonException ex)
        {
            throw new Exception("Failed to deserialize Tatoeba response.", ex);
        }

        if (response is null || response.Data is null || response.Data.Count == 0 || response.Paging is null)
        {
            return "No examples found.";
        }

        StringBuilder resultBuilder = new();
        resultBuilder.AppendLine($"Found {response.Paging.Total} sentences.");
        resultBuilder.AppendLine();

        foreach (Example example in response.Data)
        {
            if (!string.IsNullOrEmpty(example.Text))
            {
                resultBuilder.AppendLine(example.Text);
            }

            if (example.Translations is not null)
            {
                foreach (List<Translation> translationGroup in example.Translations)
                {
                    foreach (Translation translation in translationGroup)
                    {
                        if (!string.IsNullOrEmpty(translation.Text) &&
                            !string.IsNullOrEmpty(translation.Language) &&
                            string.Equals(translation.Language, tatoebaUserConfig.TranslationLanguage, StringComparison.OrdinalIgnoreCase))
                        {
                            resultBuilder.AppendLine(translation.Text);
                        }
                    }
                }
            }
            resultBuilder.AppendLine();
        }

        return resultBuilder.ToString().Trim();
    }
}
