using Projects;

var builder = DistributedApplication.CreateBuilder(args);


var mongo = builder.AddMongoDB("Mongo")
                   .WithLifetime(ContainerLifetime.Persistent);

var akagiDb = mongo.AddDatabase("AkagiDB");

var telegramToken = builder.AddParameter("TelegramToken", true);
var telegramCreatorId = builder.AddParameter("TelegramCreatorId", false);

var discordToken = builder.AddParameter("DiscordToken", true);

var geminiApiKey = builder.AddParameter("GeminiApiKey", true);

var openRouterApiKey = builder.AddParameter("OpenRouterApiKey", true);
var openRouterBaseUrl = builder.AddParameter("OpenRouterBaseUrl", "https://openrouter.ai/api/v1/", false);
var openRouterResponsePath = builder.AddParameter("OpenRouterResponsePath", "chat/completions", false);

var tatoebaUrl = builder.AddParameter("TatoebaUrl", "https://api.tatoeba.org/unstable", false);

var socketServerIp = builder.AddParameter("SocketServerIp", "127.0.0.1", false);
var socketServerPort = builder.AddParameter("SocketServerPort", "6000", false);

var serilogConfig = builder.AddParameter("SerilogConfig", """{"Serilog":{"Using":["Serilog.Sinks.Console"],"MinimumLevel":{"Default":"Information","Override":{"Microsoft":"Warning","System":"Warning"}},"WriteTo":[{"Name":"Console"}],"Enrich":["FromLogContext","WithMachineName","WithThreadId"]}}""", false);

var akagi = builder.AddProject<Akagi>("Akagi")
                   .WithReference(akagiDb, "MongoDB")
                   .WaitFor(akagiDb)
                   .WithEnvironment("Telegram__token", telegramToken)
                   .WithEnvironment("Telegram__creatorId", telegramCreatorId)
                   .WithEnvironment("Discord__token", discordToken)
                   .WithEnvironment("Gemini__apikey", geminiApiKey)
                   .WithEnvironment("OpenRouter__apikey", openRouterApiKey)
                   .WithEnvironment("OpenRouter__baseUrl", openRouterBaseUrl)
                   .WithEnvironment("OpenRouter__ResponsePath", openRouterResponsePath)
                   .WithEnvironment("Tatoeba__url", tatoebaUrl)
                   .WithEnvironment("Socket__Ip", socketServerIp)
                   .WithEnvironment("Socket__Port", socketServerPort)
                   .WithEnvironment("LLMDefinitions__Definitions__0__type", "Gemini")
                   .WithEnvironment("LLMDefinitions__Definitions__0__model", "gemini-2.5-flash-lite")
                   .WithEnvironment("LLMDefinitions__Definitions__1__type", "Gemini")
                   .WithEnvironment("LLMDefinitions__Definitions__1__model", "gemini-2.5-flash")
                   .WithEnvironment("LLMDefinitions__Definitions__2__type", "Gemini")
                   .WithEnvironment("LLMDefinitions__Definitions__2__model", "gemini-2.5-pro")
                   .WithEnvironment("LLMDefinitions__Definitions__3__type", "Gemini")
                   .WithEnvironment("LLMDefinitions__Definitions__3__model", "gemini-3-pro-preview")
                   .WithEnvironment("LLMDefinitions__Definitions__4__type", "OpenRouter")
                   .WithEnvironment("LLMDefinitions__Definitions__4__model", "deepseek/deepseek-v3.2")
                   .WithEnvironment("LLMDefinitions__Definitions__5__type", "OpenRouter")
                   .WithEnvironment("LLMDefinitions__Definitions__5__model", "tngtech/deepseek-r1t2-chimera")
                   .WithEnvironment("LLMDefinitions__Definitions__6__type", "OpenRouter")
                   .WithEnvironment("LLMDefinitions__Definitions__6__model", "google/gemini-2.5-flash-lite")
                   .WithEnvironment("LLMDefinitions__Definitions__7__type", "OpenRouter")
                   .WithEnvironment("LLMDefinitions__Definitions__7__model", "thedrummer/cydonia-24b-v4.1")
                   .WithEnvironment("Serilog__Using__0", "Serilog.Sinks.Console")
                   .WithEnvironment("Serilog__MinimumLevel__Default", "Information")
                   .WithEnvironment("Serilog__MinimumLevel__Override__Microsoft", "Warning")
                   .WithEnvironment("Serilog__MinimumLevel__Override__System", "Warning")
                   .WithEnvironment("Serilog__WriteTo__0__Name", "Console")
                   .WithEnvironment("Serilog__Enrich__0", "FromLogContext")
                   .WithEnvironment("Serilog__Enrich__1", "WithMachineName")
                   .WithEnvironment("Serilog__Enrich__2", "WithThreadId");

var akagiWebDb = mongo.AddDatabase("AkagiWebDB");

var analysisSql = builder.AddMySql("AnalysisSQL")
                         .WithLifetime(ContainerLifetime.Persistent);
var akagiWebSqlDb = analysisSql.AddDatabase("AkagiWebSQLDB");

var googleClientId = builder.AddParameter("GoogleClientId", true);
var googleClientSecret = builder.AddParameter("GoogleClientSecret", true);

var akagiWeb = builder.AddProject<Akagi_Web>("AkagiWeb")
                      .WithReference(akagiWebDb, "MongoDB")
                      .WaitFor(akagiWebDb)
                      .WithReference(akagiWebSqlDb, "MySQL")
                      .WaitFor(akagiWebSqlDb)
                      .WithEnvironment("Google__ClientId", googleClientId)
                      .WithEnvironment("Google__ClientSecret", googleClientSecret)
                      .WithEnvironment("Socket__Ip", socketServerIp)
                      .WithEnvironment("Socket__Port", socketServerPort)
                      .WithEnvironment("Serilog__Using__0", "Serilog.Sinks.Console")
                      .WithEnvironment("Serilog__MinimumLevel__Default", "Information")
                      .WithEnvironment("Serilog__MinimumLevel__Override__Microsoft", "Warning")
                      .WithEnvironment("Serilog__MinimumLevel__Override__System", "Warning")
                      .WithEnvironment("Serilog__WriteTo__0__Name", "Console")
                      .WithEnvironment("Serilog__Enrich__0", "FromLogContext")
                      .WithEnvironment("Serilog__Enrich__1", "WithMachineName")
                      .WithEnvironment("Serilog__Enrich__2", "WithThreadId");

builder.Build().Run();
