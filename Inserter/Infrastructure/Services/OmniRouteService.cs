using Domain.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Infrastructure.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Domain.DTOs;
using AppDbContext;
using Entities;



public class OmniRouteService(MasterDbContext dbContext, IConfiguration configuration, HttpClient httpClient) : IOmniRouteService
{
    private readonly string _apiKey = configuration["OmniRoute:ApiKey"] ?? throw new InvalidOperationException("OmniRoute API key is missing.");

    private readonly object _schema = new
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
                        },
                        
                        index = new
                        {
                            type = "integer"
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
                        "messageUrl",
                        "index"
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

    public async Task<ChannelOutputDTO> Extract(ChannelInputDTO channel, 
        CancellationToken cancellationToken = default)
    {
        

        var inputJson = JsonSerializer.Serialize(channel);

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
                      - Extract only the product-specific descriptive information.
                      Do not copy channel links, social media links, repeated advertisements,
                      or contact information into the description.
                      Keep the description concise while preserving important specifications.
                      - A single message may contain multiple products.
                      - Return null when a field cannot be determined.
                      - Price must be a numeric value. The price unit for all products must be identical and in Persian Rials. If it's in Tumans, it must be converted to Rials by multiplying by 10.
                      - Extract city only when explicitly mentioned.
                      - Extract phone numbers only when explicitly present.
                      - Extract purchase method only when explicitly stated.
                      - messageUrl MUST be copied exactly from the input message.
                      - Do not invent category names or brands.
                      - Ignore field index in messages and pass it without changing it.

                      Messages:

                      {inputJson}
                      """;

        var requestBody = new
        {
            model = "auto",
            stream = false,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "product_extraction",
                    strict = true,
                    schema = _schema
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"OmniRoute error: {response.StatusCode}\n{responseContent}");

        // Parse response tolerant to SSE chunked responses and non-JSON wrappers.
        // Some providers stream reasoning chunks before the final structured JSON content,
        // so we must aggregate the actual "delta.content" or "message.content" fragments.
        string? contentString = null;
        var trimmed = responseContent.Trim();

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var dataChunks = trimmed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[5..].Trim())
                .Where(data => !string.IsNullOrWhiteSpace(data) && !string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (dataChunks.Count > 0)
            {
                var contentBuilder = new StringBuilder();

                foreach (var chunk in dataChunks)
                {
                    try
                    {
                        using var chunkJson = JsonDocument.Parse(chunk);
                        var root = chunkJson.RootElement;

                        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var choice in choices.EnumerateArray())
                        {
                            if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                            {
                                if (message.TryGetProperty("content", out var messageContent) && messageContent.ValueKind == JsonValueKind.String)
                                    contentBuilder.Append(messageContent.GetString());
                            }

                            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                            {
                                if (delta.TryGetProperty("content", out var deltaContent) && deltaContent.ValueKind == JsonValueKind.String)
                                    contentBuilder.Append(deltaContent.GetString());
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore non-JSON SSE metadata lines and keep scanning the remaining chunks.
                    }
                }

                contentString = contentBuilder.ToString();
            }
        }

        JsonDocument? parsed = null;
        try
        {
            if (string.IsNullOrWhiteSpace(contentString))
            {
                try
                {
                    parsed = JsonDocument.Parse(trimmed);
                }
                catch (JsonException)
                {
                    // Attempt to extract the first JSON payload from the raw response.
                    var first = responseContent.IndexOf('{');
                    var last = responseContent.LastIndexOf('}');
                    if (first >= 0 && last > first)
                    {
                        var sub = responseContent.Substring(first, last - first + 1);
                        parsed = JsonDocument.Parse(sub);
                    }
                }

                if (parsed is null)
                    throw new Exception($"Failed to parse model response as JSON. Raw response: {responseContent}");

                var root = parsed.RootElement;

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    throw new Exception($"No choices returned from model. Raw response: {responseContent}");

                var firstChoice = choices[0];

                if (firstChoice.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.Object)
                {
                    if (messageProp.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                        contentString = contentProp.GetString();
                }

                if (string.IsNullOrWhiteSpace(contentString) && firstChoice.TryGetProperty("content", out var directContent) && directContent.ValueKind == JsonValueKind.String)
                    contentString = directContent.GetString();
            }

            if (string.IsNullOrWhiteSpace(contentString))
                throw new Exception($"Model returned empty or unparsable content. Raw response: {responseContent}");
        }
        finally
        {
            parsed?.Dispose();
        }
        
        var result = JsonSerializer.Deserialize<ChannelOutputDTO>(contentString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        return result ?? throw new Exception("Failed to deserialize model response");
    }

    public async Task InsertToDatabase(List<ExtractedChannelDTO> channels)
    {
        var channelEntities = new List<Channel>();

        foreach (var channel in channels)
        {
            var products = new List<Product>();

            foreach (var p in channel.Products)
            {
                var category = await dbContext.Categories.FirstOrDefaultAsync(c => c.CategoryName == p.CategoryName);
                if (category == null)
                {
                    category = new Category { CategoryName = p.CategoryName! };
                    await dbContext.Categories.AddAsync(category);
                    // will be saved with channels later
                }

                products.Add(new Product
                {
                    Category = category,
                    Brand = p.Brand,
                    PurchaseMethod = p.PurchaseMethod,
                    Price = p.Price ?? 0,
                    Description = p.Description,
                    Name = p.Name,
                    MessageUrl = p.MessageUrl
                });
            }

            var entity = new Channel
            {
                ExternalId = channel.ChannelId,
                City = channel.City,
                Description = channel.Description,
                PhoneNumbers = channel.PhoneNumbers,
                Products = products
            };

            channelEntities.Add(entity);
        }

        await dbContext.Channels.AddRangeAsync(channelEntities);
        await dbContext.SaveChangesAsync();
    }

    public async Task AddChannel(ExtractedChannelDTO channel)
    {
        await dbContext.Channels
            .AddAsync(new Channel()
            {
                Description = channel.Description,
                City = channel.City,
                ExternalId = channel.ChannelId,
                PhoneNumbers = channel.PhoneNumbers
            });
        await dbContext.SaveChangesAsync();
    }
    public async Task<bool> ChannelWithIdExists(string channelId)
    {
        return await dbContext.Channels
            .AnyAsync(x => x.ExternalId == channelId);
    }
}