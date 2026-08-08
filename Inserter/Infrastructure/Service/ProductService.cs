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
                    // common pattern: bearer or api-key header. We add both safely.
                    if (!client.DefaultRequestHeaders.Contains("Authorization"))
                        client.DefaultRequestHeaders.Add("Authorization", deepSeekSettings.ApiKey);
                }

                var payload = new
                {
                    prompt = DeepSeekSettings.Prompt,
                    channels = channels
                };

                var content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(deepSeekSettings.ApiUri, content);
                if (resp.IsSuccessStatusCode)
                {
                    var respStr = await resp.Content.ReadAsStringAsync();
                    try
                    {
                        var respOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var list = JsonSerializer.Deserialize<List<ExtractedProduct>>(respStr, respOptions);
                        if (list is not null)
                            extractedProducts.AddRange(list);
                        else
                        {
                            // try single object
                            var single = JsonSerializer.Deserialize<ExtractedProduct>(respStr, respOptions);
                            if (single is not null)
                                extractedProducts.Add(single);
                        }
                    }
                    catch
                    {
                        // on any parsing error fallback to local extraction
                        extractedProducts.AddRange(ExtractLocally(channels));
                    }
                }
                else
                {
                    // non-success -> fallback to local extraction
                    extractedProducts.AddRange(ExtractLocally(channels));
                }
            }
            catch
            {
                // network or other error -> fallback to local
                extractedProducts.AddRange(ExtractLocally(channels));
            }
        }
        else
        {
            extractedProducts.AddRange(ExtractLocally(channels));
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
        public string? channel_id { get; set; }
        public string? status { get; set; }
        public string? description { get; set; }
        public List<RubikaMessageDto>? messages { get; set; }
    }

    private class RubikaMessageDto
    {
        public string? message_id { get; set; }
        public string? datetime_utc { get; set; }
        public string? text { get; set; }
        public string? caption { get; set; }
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

    // Local heuristic extraction used when DeepSeek is not available or fails
    private static IEnumerable<ExtractedProduct> ExtractLocally(IEnumerable<RubikaChannelDto> channels)
    {
        var results = new List<ExtractedProduct>();
        foreach (var ch in channels)
        {
            var channelId = ch.channel_id;
            var channelDescription = ch.description;
            if (ch.messages == null) continue;

            foreach (var m in ch.messages)
            {
                if (string.IsNullOrWhiteSpace(m.text)) continue;

                var lines = m.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim())
                                  .Where(l => !string.IsNullOrWhiteSpace(l))
                                  .ToArray();
                if (lines.Length == 0) continue;

                // Title: prefer first meaningful line without emojis
                var title = lines.FirstOrDefault(l => l.Any(c => !char.IsPunctuation(c) && !char.IsSymbol(c))) ?? lines[0];

                // Price: look for explicit price patterns
                decimal? price = null;
                // look for numbers followed by تومان or ریال or ﷼
                var priceRegex = new Regex(@"([0-9]{1}[0-9,./\s]{0,30})(?:\s*(?:تومان|ریال|﷼))", RegexOptions.Compiled);
                var mPrice = priceRegex.Match(m.text);
                if (mPrice.Success)
                {
                    var num = Regex.Replace(mPrice.Groups[1].Value, "[^0-9]", "");
                    if (decimal.TryParse(num, out var parsed)) price = parsed;
                }
                else
                {
                    // fallback: look for a line starting with "قیمت" or containing "قیمت"
                    var priceLine = lines.FirstOrDefault(l => l.Contains("قیمت") && l.Any(char.IsDigit));
                    if (priceLine != null)
                    {
                        var digits = Regex.Replace(priceLine, "[^0-9]", "");
                        if (decimal.TryParse(digits, out var parsed)) price = parsed;
                    }
                }

                // Category: try to infer from channel description or default
                var category = "General";
                if (!string.IsNullOrWhiteSpace(channelDescription))
                {
                    // pick first word-ish element
                    var parts = channelDescription.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                   .Select(p => p.Trim())
                                                   .Where(p => p.Length > 3)
                                                   .ToArray();
                    if (parts.Length > 0) category = parts[0];
                }

                // Brand: try to detect common brand words in the text (very heuristic)
                string? brand = null;
                var brandCandidates = new[] { "هامبورگ", "میتا", "فورس", "مولتی", "مستر", "هافنر" };
                foreach (var b in brandCandidates)
                {
                    if (m.text.Contains(b, StringComparison.OrdinalIgnoreCase))
                    {
                        brand = b;
                        break;
                    }
                }

                // Description: use whole text as description
                var desc = m.text;

                results.Add(new ExtractedProduct
                {
                    Title = title,
                    ProductName = title,
                    ChannelId = channelId,
                    ChannelUserName = ch.channel_id,
                    ChannelDescription = channelDescription,
                    CategoryName = category,
                    Price = price,
                    Description = desc,
                    Brand = brand
                });
            }
        }

        return results;
    }
}
