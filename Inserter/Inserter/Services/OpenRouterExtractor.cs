using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Domain.DTOs;
using Infrastructure.AppDbContext;
using Infrastructure.Entities;
using Inserter.Controllers;

namespace Inserter.Services;

public class OpenRouterExtractor(
    IConfiguration configuration,
    MasterDbContext dbContext,
    HttpClient httpClient)
{
    private readonly OpenRouterSettings _openRouterSettings
        = new()
        {
            ApiKey = configuration["OpenRouter:ApiKey"],
            BaseUrl = configuration["OpenRouter:Url"]
        };

    public async Task<ExtractedChannelDTO> Extract(
        List<MessageFileMessageDTO> messages,
        CancellationToken cancellationToken = default)
    {
        // In your OpenRouterExtractor.cs
        
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openRouterSettings.ApiKey}");
        httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://jetsender.ir"); // Required
        httpClient.DefaultRequestHeaders.Add("X-Title", "tapeworm"); // Optional but recommended
        var input = messages
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new
            {
                message_url = x.MessageUrl,
                text = x.Text
            })
            .ToList();

        var inputJson = JsonSerializer.Serialize(input);

        var prompt = $"""
                      Extract product information from the following
                      Persian marketplace messages.

                      The input is an array of messages. Each message contains
                      its URL and original text.

                      EXTRACTION RULES:

                      - Extract only products or services that are actually
                        being offered, sold, or advertised.
                      - Do not invent information.
                      - Do not translate anything.
                      - Do not correct spelling mistakes.
                      - Do not normalize Persian words.
                      - Preserve product names exactly as they appear
                        in the source message whenever possible.
                      - Preserve descriptions from the source instead of
                        rewriting or summarizing them.
                      - A single message may contain multiple products.
                      - Return null when a field cannot be determined.
                      - Price must be a numeric value.
                      - Extract city only when explicitly mentioned.
                      - Extract phone numbers only when explicitly present.
                      - Extract purchase method only when explicitly stated.
                      - messageUrl MUST be copied exactly from the input message.
                      - Do not invent category names or brands.

                      Messages:

                      {inputJson}
                      """;


        var requestBody = new
        {
            model = _openRouterSettings.Models[0],

            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },

            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "product_extraction",
                    strict = true,
                    schema = _openRouterSettings.Schema
                }
            }
        };


        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_openRouterSettings.BaseUrl}/chat/completions"
        );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _openRouterSettings.ApiKey
            );

        

        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );


        var response = await httpClient.SendAsync(
            request,
            cancellationToken
        );

        var responseContent = await response.Content.ReadAsStringAsync(
            cancellationToken
        );


        if (!response.IsSuccessStatusCode)
            throw new Exception(
                $"OpenRouter error: {response.StatusCode}\n{responseContent}"
            );


        using var json = JsonDocument.Parse(responseContent);

        var content = json
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();


        if (string.IsNullOrWhiteSpace(content))
            throw new Exception("Model returned empty response");


        var result = JsonSerializer.Deserialize<ExtractedChannelDTO>(
            content,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );


        return result
               ?? throw new Exception("Failed to deserialize model response");
    }
    
    
    public async Task InsertToDatabase(List<ExtractedChannelDTO> channels)
    {
        List<Channel> channelEntities = [];
        foreach(var channel in channels)
        {
            var entity = new Channel
            {
                ExternalId = channel.ChannelId,
                City = channel.City,
                Description = channel.Description,
                PhoneNumbers = channel.PhoneNumbers,
                Products = [.. channel.Products.Select(p => new Product
                {
                    Category = dbContext.Categories.Any(c => c.CategoryName == p.CategoryName) ? 
                        dbContext.Categories.First(c => c.CategoryName == p.CategoryName) : 
                        new Category
                        {
                            CategoryName = p.CategoryName!
                        },
                    ChannelId = default,
                    Brand = p.Brand,
                    PurchaseMethod = p.PurchaseMethod,
                    Price = p.Price,
                    Description = p.Description,
                    Name = p.Name,
                    MessageUrl = p.MessageUrl
                })]
            };
            channelEntities.Add(entity);
        }
        await dbContext.Channels.AddRangeAsync(channelEntities);
        await dbContext.SaveChangesAsync();
    }
}

public class OpenRouterSettings
{
    public List<string> Models => ["qwen/qwen3-8b:free"];
    public required string? ApiKey { get; set; }
    public required string? BaseUrl { get; set; }

    public object Schema => new
    {
        type = "object",

        properties = new
        {
            city = new
            {
                type = new[] { "string", "null" }
            },

            phoneNumbers = new
            {
                type = new[] { "string", "null" }
            },

            products = new
            {
                type = "array",

                items = new
                {
                    type = "object",

                    properties = new
                    {
                        name = new
                        {
                            type = "string"
                        },

                        categoryName = new
                        {
                            type = new[] { "string", "null" }
                        },

                        brand = new
                        {
                            type = new[] { "string", "null" }
                        },

                        purchaseMethod = new
                        {
                            type = new[] { "string", "null" }
                        },

                        price = new
                        {
                            type = new[] { "number", "null" }
                        },

                        description = new
                        {
                            type = new[] { "string", "null" }
                        },

                        messageUrl = new
                        {
                            type = "string"
                        }
                    },

                    required = new[]
                    {
                        "name",
                        "categoryName",
                        "brand",
                        "purchaseMethod",
                        "price",
                        "description",
                        "messageUrl"
                    }
                }
            }
        },

        required = new[]
        {
            "city",
            "phoneNumbers",
            "products"
        }
    };
}