namespace Infrastructure.Service;

public class DeepSeekSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiUri { get; set; } = "";
    private const string ExampleOutput =
        @"
Example output (one element):
[
  {
    ""ProductName"": ""قیچی PVC بر اتوماتیک هامبورگ"",
    ""Title"": ""قیچی PVCبر اتوماتیک هامبورگ"",
    ""ChannelId"": ""hamburgtools"",
    ""ChannelUserName"": ""hamburgtools"",
    ""ChannelDescription"": ""کانال رسمی ابزارآلات هامبورگ در روبیکا"",
    ""ChannelCity"": null,
    ""ChannelPhoneNumbers"": null,
    ""CategoryName"": ""قیچی"",
    ""Price"": null,
    ""Currency"": null,
    ""Description"": ""📎قیچی PVCبر اتوماتیک هامبورگ\n\n🔸کد محصول: H5906\n  🔸تیغه فولاد ضد زنگ SK5\n🔸مکانیزم اتوماتیک\n\n🔹️هامبورگ، ابزار همیشگی..."",
    ""Brand"": ""هامبورگ"",
    ""SellMethod"": null,
    ""MessageDate"": ""2026-08-01T07:15:52+00:00""
  }
]";
    public static string Prompt {
        get
        {
            // Prompt for the DeepSeek / extraction service. It must instruct the model to parse
            // the provided JSON array of channels and messages and return a strict JSON array of
            // extracted product objects matching the application's database fields.
            return @"You will receive a JSON array of channel objects. Each channel object has fields:
- channel_id (string)
- status (string)
- description (string)
- messages (array of message objects)

Each message object has fields:
- message_id (string)
- datetime_utc (ISO string)
- text (string|null)
- caption (string|null)

Task: For every message that appears to describe or advertise a product, extract a single product record with the following properties (use null when a value cannot be determined):

- ProductName: string (the product name or short title)
- Title: string (the first meaningful title or headline from the message)
- ChannelId: string (the channel's channel_id)
- ChannelUserName: string (channel username if present)
- ChannelDescription: string (channel description field)
- ChannelCity: string|null (try to infer a city from channel description if present)
- ChannelPhoneNumbers: string|null (comma-separated phone numbers found in channel description or message)
- CategoryName: string (category inferred from channel description or message; use ""General"" if unsure)
- Price: number|null (normalized numeric price in smallest currency unit or integer; remove separators and return numeric only)
- Currency: string|null (e.g. IRR, تومان, ریال, USD, etc., if present)
- Description: string (the full message text to use as product description)
- Brand: string|null (if a brand name is present)
- SellMethod: string|null (e.g., carton, per piece, wholesale, contact for price)
- MessageDate: string (datetime_utc from the message)

Output requirements:
1) Return ONLY valid JSON: a single JSON array of product objects as described above. No explanatory text, no markdown, no surrounding comments.
2) Use null for missing values.
3) Price must be a numeric JSON value (no currency symbols inside the number). If you detect a price like ""1/234/000 ریال"" or with commas, remove non-digits and return as number (e.g. 1234000).
4) Try to extract phone numbers (continuous digits, may include + or 0 prefixes) into ChannelPhoneNumbers as a comma-separated string.
5) CategoryName should be specific when obvious (e.g. ""قیچی"", ""آچار"", ""جعبه بکس""); otherwise return ""General""." + ExampleOutput +
"\nDo not add any extra fields. Strictly follow the schema and JSON-only output rule.";
        }
    }
}
