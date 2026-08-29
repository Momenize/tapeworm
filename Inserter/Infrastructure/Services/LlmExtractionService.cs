// using System.Net.Http.Json;
// using System.Text.Json;
// using Infrastructure.Entities;
// using Domain.DTOs;
// using Domain.IServices;
// using Microsoft.EntityFrameworkCore;
// using Infrastructure.AppDbContext;
//
// namespace Infrastructure.Services;
//
//
// public sealed class LlmExtractionService(HttpClient httpClient, MasterDbContext db) : ILlmExtractionService
// {
//     private readonly HttpClient _httpClient = httpClient;
//     private readonly MasterDbContext _db = db;
//
//     public async Task<ExtractedChannelDTO> ExtractChannelAsync(
//         ChannelInputDTO channel,
//         CancellationToken cancellationToken = default)
//     {
//         var request = new
//         {
//             model = "qwen3:4b",
//             stream = false,
//
//             format = GetSchema(),
//
//             messages = new object[]
//             {
//             new
//             {
//                 role = "system",
//                 content = """
//                 تو اطلاعات ساختاریافته محصولات را از پیام‌های فارسیِ مربوط به بازارها و کانال‌های فروش استخراج می‌کنی
//
//                 فقط یک JSON معتبر مطابق با ساختار (Schema) ارائه‌شده برگردان.
//
//                 قوانین:
//
//                 * فقط زمانی محصولی را استخراج کن که پیام حاوی یک محصول یا خدمت واقعیِ قابل‌عرضه باشد.
//                 * هیچ اطلاعاتی را حدس نزن یا از خودت اضافه نکن
//                 * اگر تعیین مقدار یک فیلد ممکن نیست، مقدار آن را `null` قرار بده.
//                 * در صورت امکان، نام اصلی محصول را بدون تغییر حفظ کن.
//                 * قیمت‌ها را به صورت مقادیر عددی اعشاری استخراج کن.
//                 * شهر را فقط در صورتی استخراج کن که به‌طور صریح ذکر شده باشد.
//                 * شماره تلفن‌ها را فقط در صورتی استخراج کن که به‌طور صریح وجود داشته باشند.
//                 * روش خرید یا پرداخت را استخراج کن؛ مانند نقدی، اقساطی، چکی، آنلاین و غیره.
//                 * یک دسته‌بندی مختصر و مناسب برای محصول تعیین کن.
//                 * چند محصول را در یک شیء محصول قرار نده.
//                 * هر محصول باید URL پیام اصلی خود را حفظ کند.
//                 
//                 """
//             },
//             new
//             {
//                 role = "user",
//                 content = BuildUserPrompt(channel)
//             }
//             }
//         };
//
//         if (_httpClient.BaseAddress is null)
//             throw new InvalidOperationException("HttpClient BaseAddress is not set. Ensure AddHttpClient<ILlmExtractionService, LlmExtractionService>(...) is used and not overridden.");
//
//         using var req = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
//         {
//             Content = JsonContent.Create(request)
//         };
//
//         using var response = await _httpClient.SendAsync(
//             req,
//             HttpCompletionOption.ResponseHeadersRead,
//             cancellationToken);
//
//         response.EnsureSuccessStatusCode();
//
//         await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
//
//         var ollamaResponse = await JsonSerializer.DeserializeAsync<OllamaResponse>(
//             responseStream,
//             new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
//             cancellationToken);
//
//         if (ollamaResponse?.Message?.Content is null)
//             throw new InvalidOperationException(
//                 "Ollama returned an empty response.");
//
//         return JsonSerializer.Deserialize<ExtractedChannelDTO>(
//                    ollamaResponse.Message.Content,
//                    new JsonSerializerOptions
//                    {
//                        PropertyNameCaseInsensitive = true
//                    })
//                ?? throw new InvalidOperationException(
//                    "Could not deserialize Ollama response.");
//     }
//
//     private static string BuildUserPrompt(ChannelInputDTO channel)
//     {
//         var messages = string.Join(
//             "\n\n",
//             channel.Messages.Select((m, i) => $"""
//             MESSAGE #{i + 1}
//
//             URL: {m.MessageUrl}
//             DATE: {m.DatetimeUtc}
//
//             TEXT:
//             {m.Text}
//             """));
//
//         return $"""
//         CHANNEL INFORMATION
//
//         Channel ID:
//         {channel.ChannelId}
//
//         Channel description:
//         {channel.Description}
//
//         CHANNEL MESSAGES
//
//         {messages}
//
//         اطلاعات کانال و همه محصولاتی که در این پیام‌ها موجود هستند را استخراج کن
//         """;
//     }
//
//     private static object GetSchema()
//     {
//         return new
//         {
//             type = "object",
//             properties = new
//             {
//                 description = new
//                 {
//                     type = new[] { "string", "null" }
//                 },
//                 city = new
//                 {
//                     type = new[] { "string", "null" }
//                 },
//                 phoneNumbers = new
//                 {
//                     type = new[] { "string", "null" }
//                 },
//                 products = new
//                 {
//                     type = "array",
//                     items = new
//                     {
//                         type = "object",
//                         properties = new
//                         {
//                             name = new { type = "string" },
//                             categoryName = new
//                             {
//                                 type = new[] { "string", "null" }
//                             },
//                             brand = new
//                             {
//                                 type = new[] { "string", "null" }
//                             },
//                             purchaseMethod = new
//                             {
//                                 type = new[] { "string", "null" }
//                             },
//                             price = new
//                             {
//                                 type = new[] { "number", "null" }
//                             },
//                             description = new
//                             {
//                                 type = new[] { "string", "null" }
//                             },
//                             messageUrl = new { type = "string" }
//                         },
//                         required = new[]
//                         {
//                         "name",
//                         "categoryName",
//                         "brand",
//                         "purchaseMethod",
//                         "price",
//                         "description",
//                         "messageUrl"
//                     }
//                     }
//                 }
//             },
//             required = new[]
//             {
//             "description",
//             "city",
//             "phoneNumbers",
//             "products"
//         }
//         };
//     }
//
//     public async Task ProcessChannelAsync(
//     ChannelInputDTO input,
//     CancellationToken cancellationToken = default)
//     {
//         var extracted =
//             await ExtractChannelAsync(input, cancellationToken);
//
//         await using var transaction =
//             await _db.Database.BeginTransactionAsync(cancellationToken);
//
//         var channel = await _db.Channels
//             .FirstOrDefaultAsync(
//                 x => x.ExternalId == input.ChannelId,
//                 cancellationToken);
//
//         if (channel is null)
//         {
//             channel = new Channel
//             {
//                 ExternalId = input.ChannelId,
//                 Description = extracted.Description,
//                 City = extracted.City,
//                 PhoneNumbers = extracted.PhoneNumbers
//             };
//
//             _db.Channels.Add(channel);
//
//             await _db.SaveChangesAsync(cancellationToken);
//         }
//         else
//         {
//             channel.Description ??= extracted.Description;
//             channel.City ??= extracted.City;
//             channel.PhoneNumbers ??= extracted.PhoneNumbers;
//         }
//
//         foreach (var product in extracted.Products)
//         {
//             var category = await GetOrCreateCategoryAsync(
//                 product.CategoryName,
//                 cancellationToken);
//
//             var entity = new Product
//             {
//                 ChannelId = channel.Id,
//                 CategoryId = category.Id,
//
//                 Name = product.Name,
//                 Brand = product.Brand,
//                 PurchaseMethod = product.PurchaseMethod,
//                 Price = product.Price,
//                 Description = product.Description,
//                 MessageUrl = product.MessageUrl
//             };
//
//             _db.Products.Add(entity);
//         }
//
//         await _db.SaveChangesAsync(cancellationToken);
//
//         await transaction.CommitAsync(cancellationToken);
//     }
//
//     private async Task<Category> GetOrCreateCategoryAsync(
//         string? categoryName,
//         CancellationToken cancellationToken)
//     {
//         if (string.IsNullOrWhiteSpace(categoryName))
//             categoryName = "عمومی";
//
//         var existing = await _db.Categories
//             .FirstOrDefaultAsync(x => x.CategoryName == categoryName, cancellationToken);
//
//         if (existing is not null)
//             return existing;
//
//         var cat = new Category { CategoryName = categoryName };
//         _db.Categories.Add(cat);
//         await _db.SaveChangesAsync(cancellationToken);
//         return cat;
//     }
// }
//
// public sealed class OllamaResponse
// {
//     public OllamaMessage Message { get; set; } = new();
// }
//
// public sealed class OllamaMessage
// {
//     public string Content { get; set; } = string.Empty;
// }
