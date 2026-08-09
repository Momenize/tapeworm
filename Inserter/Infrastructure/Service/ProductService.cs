using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Models;
using Domain;
using Domain.DTOs;
using Domain.Models;

namespace Infrastructure.Service;

public class ProductService(AppDbContext dbContext, DeepSeekSettings deepSeekSettings, MessagesFilePath filePath) : IProductService
{
    public async Task AddProducts()
    {
        if (filePath.FilePath == null || filePath.FilePath.Length == 0)
        {
            throw new FileNotFoundException("rubika_messages.json not found or file empty");
        }

        var json = await File.ReadAllTextAsync(filePath.FilePath!);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<RubikaChannelDto>? channels = null;
        try
        {
            channels = JsonSerializer.Deserialize<List<RubikaChannelDto>>(json, jsonOptions);
        }
        catch
        {
            // fallback: try to parse root element and extract array if present
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    channels = JsonSerializer.Deserialize<List<RubikaChannelDto>>(doc.RootElement.GetRawText(), jsonOptions);
                }
                else if (doc.RootElement.TryGetProperty("channels", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    channels = JsonSerializer.Deserialize<List<RubikaChannelDto>>(arr.GetRawText(), jsonOptions);
                }
                else
                {
                    channels = new List<RubikaChannelDto>();
                }
            }
            catch
            {
                channels = new List<RubikaChannelDto>();
            }
        }

        if (channels == null || channels.Count == 0)
            return; // nothing to do

        List<ExtractedProduct> extractedProducts = new();

        // If DeepSeek endpoint is configured, call it. Otherwise fallback to local extraction.
        if (!string.IsNullOrWhiteSpace(deepSeekSettings.ApiUri))
        {
            try
            {
                using var client = new HttpClient();
                if (!string.IsNullOrWhiteSpace(deepSeekSettings.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {deepSeekSettings.ApiKey}");
                }

                // DeepSeek API expects this format
                var payload = new
                {
                    model = "deepseek-chat", // or "deepseek-coder" depending on your needs
                    messages = new[]
                    {
            new {
                role = "user",
                content = DeepSeekSettings.Prompt // Your prompt text here
            }
        },
                    temperature = 0.7, // Optional: controls randomness
                    max_tokens = 1000 // Optional: max length of response
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload, jsonOptions),
                    Encoding.UTF8,
                    "application/json"
                );

                var resp = await client.PostAsync(deepSeekSettings.ApiUri, content);

                if (resp.IsSuccessStatusCode)
                {
                    var respStr = await resp.Content.ReadAsStringAsync();

                    // Parse the response - DeepSeek returns a chat completion response
                    try
                    {
                        var responseObj = JsonSerializer.Deserialize<DeepSeekResponse>(respStr,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (responseObj?.Choices?.FirstOrDefault()?.Message?.Content is string contentStr)
                        {
                            // Now parse the content string which should contain your extracted products
                            // Assuming contentStr is JSON that can be deserialized to List<ExtractedProduct>
                            var list = JsonSerializer.Deserialize<List<ExtractedProduct>>(contentStr,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (list is not null)
                                extractedProducts.AddRange(list);
                            else
                            {
                                // Try single object
                                var single = JsonSerializer.Deserialize<ExtractedProduct>(contentStr,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (single is not null)
                                    extractedProducts.Add(single);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Cannot extract channels 1: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    var errorContent = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"Cannot extract channels 2. Status: {resp.StatusCode}, Error: {errorContent}");
                    return;
                }
            }
            catch
            {
                // network or other error -> fallback to local
                Console.WriteLine("Cannot extract channels 3");
                return;
            }
        }
        else
        {
            Console.WriteLine("Cannot extract channels 4");
            return;
        }

        // Persist extracted products into database. Ensure channels and categories exist.
        foreach (var p in extractedProducts)
        {
            // find or create channel
            string channelUser = p.ChannelUserName ?? p.ChannelId ?? "";
            ChannelModel? channel = null;
            if (!string.IsNullOrWhiteSpace(channelUser))
            {
                channel = await dbContext.Set<ChannelModel>().FirstOrDefaultAsync(c => c.UserName == channelUser);
            }

            if (channel == null)
            {
                // try by channel id
                if (!string.IsNullOrWhiteSpace(p.ChannelId))
                {
                    channel = await dbContext.Set<ChannelModel>().FirstOrDefaultAsync(c => c.UserName == p.ChannelId);
                }
            }

            if (channel == null)
            {
                channel = new ChannelModel { UserName = channelUser, Description = p.ChannelDescription };
                dbContext.Add(channel);
                await dbContext.SaveChangesAsync(); // save so channel gets Id for FK
            }

            // find or create category
            string categoryName = string.IsNullOrWhiteSpace(p.CategoryName) ? "General" : p.CategoryName;
            var category = await dbContext.Set<CategoryModel>().FirstOrDefaultAsync(c => c.CategoryName == categoryName);
            if (category == null)
            {
                category = new CategoryModel { CategoryName = categoryName };
                dbContext.Add(category);
                await dbContext.SaveChangesAsync();
            }

            // create product
            var product = new ProductModel
            {
                ProductName = p.ProductName ?? p.Title ?? "",
                ChannelId = channel.Id,
                Channel = channel,
                CategoryId = category.Id,
                Category = category,
                Price = p.Price,
                Description = p.Description,
                Brand = p.Brand,
                SellMethod = p.SellMethod
            };

            dbContext.Add(product);
        }

        await dbContext.SaveChangesAsync();
    }
    public async Task<List<ProductDTO>> GetChannelProducts(int channelId)
    {
        var result = await dbContext.Products
            .Where(x => x.ChannelId == channelId)
            .Select(ProductDTO.Expr()).ToListAsync();
        return result;
    }
    

    // Minimal DTOs for the JSON structure we have
    private class RubikaChannelDto
    {
        public string? ChannelId { get; set; }
        public string? Status { get; set; }
        public string? Description { get; set; }
        public List<RubikaMessageDto>? Messages { get; set; }
    }

    private class RubikaMessageDto
    {
        public string? MessageId { get; set; }
        public string? DatetimeUtc { get; set; }
        public string? Text { get; set; }
        public string? Caption { get; set; }
    }

    private class ExtractedProduct
    {
        public string? Title { get; set; }
        public string? ProductName { get; set; }
        public string? ChannelId { get; set; }
        public string? ChannelUserName { get; set; }
        public string? ChannelDescription { get; set; }
        public string? CategoryName { get; set; }
        public decimal? Price { get; set; }
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? SellMethod { get; set; }
    }

    public class DeepSeekResponse
    {
        public string? Id { get; set; }
        public string? Object { get; set; }
        public long Created { get; set; }
        public string? Model { get; set; }
        public List<Choice> Choices { get; set; } = [];
        public Usage? Usage { get; set; }
    }

    public class Choice
    {
        public int Index { get; set; }
        public Message? Message { get; set; }
        public string? FinishReason { get; set; }
    }

    public class Message
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    public class Usage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

}
