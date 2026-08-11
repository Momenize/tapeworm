using Domain.DTOs;
using Google.GenAI;
using Google.GenAI.Types;
using Infrastructure.AppDbContext;
using Infrastructure.Entities;
using Inserter.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

public class GeminiExtractor
{
    private readonly Client _client;
    private readonly MasterDbContext _dbContext;

    public GeminiExtractor(IConfiguration configuration, MasterDbContext dbContext)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Gemini API key is missing.");

        _client = new Client(apiKey: apiKey);
        _dbContext = dbContext;
    }

    public async Task<ExtractedChannelDTO> Extract(
    List<MessageFileMessageDTO> messages,
    CancellationToken cancellationToken = default)
    {
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

        var response = await _client.Models.GenerateContentAsync(
            model: "gemini-2.5-flash",
            contents: prompt,
            config: new GenerateContentConfig
            {
                ResponseMimeType = "application/json",
                ResponseSchema = Schema.FromJson(JsonSerializer.Serialize(GetSchema()))
            },
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
            throw new InvalidOperationException(
                "Gemini returned an empty response.");

        var result = JsonSerializer.Deserialize<ExtractedChannelDTO>(
            response.Text,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (result is null)
            throw new InvalidOperationException(
                "Failed to deserialize Gemini response.");

        return result;
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
                    Category = _dbContext.Categories.Any(c => c.CategoryName == p.CategoryName) ? 
                        _dbContext.Categories.Where(c => c.CategoryName == p.CategoryName).First() : 
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
        await _dbContext.Channels.AddRangeAsync(channelEntities);
        await _dbContext.SaveChangesAsync();
    }

    private static object GetSchema() => new
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