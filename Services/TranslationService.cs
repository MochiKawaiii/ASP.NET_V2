using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VtvNewsApp.Models;
using System.Threading;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace VtvNewsApp.Services
{
    public class TranslationService : ITranslationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TranslationService> _logger;
        
        // Rate limiting
        private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(5, 5); // Tăng lên 5 concurrent requests
        private static DateTime _lastRequestTime = DateTime.MinValue;
        private static readonly TimeSpan _requestDelay = TimeSpan.FromMilliseconds(100); // Giảm delay xuống 100ms
        
        // Cache để tránh dịch lại những văn bản đã dịch
        private static readonly ConcurrentDictionary<string, string> _translationCache = new ConcurrentDictionary<string, string>();

        // Sử dụng Google Translate API (miễn phí thông qua API không chính thức)
        private readonly string _primaryApiUrl = "https://translate.googleapis.com/translate_a/single";
        // Backup API: MyMemory Translate API
        private readonly string _backupApiUrl = "https://api.mymemory.translated.net/get";
        // LibreTranslate API (tùy chọn) - có thể tự host hoặc sử dụng dịch vụ công cộng
        private readonly string _libreTranlateUrl = "https://libretranslate.com/translate";

        public TranslationService(HttpClient httpClient, ILogger<TranslationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<(string Title, string Description)> TranslateArticleAsync(string? title, string? description)
        {
            // Đảm bảo title và description không null
            if (title == null) title = string.Empty;
            if (description == null) description = string.Empty;
            
            _logger.LogDebug($"BEGIN TRANSLATION DEBUG =========");
            _logger.LogDebug($"Original Title: {title}");
            _logger.LogDebug($"Original Description: {description}");
            
            try
            {
                // Kiểm tra cache trước khi dịch
                string translatedTitle = string.Empty;
                string translatedDescription = string.Empty;
                
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (_translationCache.TryGetValue(title, out var cachedTitle))
                    {
                        translatedTitle = cachedTitle;
                        _logger.LogDebug($"Using cached translation for title: {title}");
                    }
                    else
                    {
                        translatedTitle = await TranslateTextWithRateLimitAsync(title);
                        if (!string.IsNullOrWhiteSpace(translatedTitle))
                        {
                            _translationCache.TryAdd(title, translatedTitle);
                        }
                    }
                }
                
                if (!string.IsNullOrWhiteSpace(description))
                {
                    if (_translationCache.TryGetValue(description, out var cachedDesc))
                    {
                        translatedDescription = cachedDesc;
                        _logger.LogDebug($"Using cached translation for description");
                    }
                    else
                    {
                        translatedDescription = await TranslateTextWithRateLimitAsync(description);
                        if (!string.IsNullOrWhiteSpace(translatedDescription))
                        {
                            _translationCache.TryAdd(description, translatedDescription);
                        }
                    }
                }
                
                _logger.LogDebug($"Translated Title: {translatedTitle}");
                _logger.LogDebug($"Translated Description: {translatedDescription}");
                _logger.LogDebug($"END TRANSLATION DEBUG ===========");
                
                // Nếu dịch không thành công, sử dụng văn bản gốc
                if (string.IsNullOrWhiteSpace(translatedTitle)) translatedTitle = title;
                if (string.IsNullOrWhiteSpace(translatedDescription)) translatedDescription = description;
                
                return (translatedTitle, translatedDescription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during translation. Using original text.");
                return (title, description);
            }
        }
        
        private async Task<string> TranslateTextWithRateLimitAsync(string text)
        {
            // Rate limiting - chỉ cho phép 1 request tại một thời điểm
            await _rateLimiter.WaitAsync();
            
            try
            {
                // Kiểm tra thời gian delay
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
                if (timeSinceLastRequest < _requestDelay)
                {
                    var delayTime = _requestDelay - timeSinceLastRequest;
                    _logger.LogDebug($"Rate limiting: waiting {delayTime.TotalMilliseconds}ms");
                    await Task.Delay(delayTime);
                }
                
                _lastRequestTime = DateTime.UtcNow;
                
                return await TranslateTextAsync(text);
            }
            finally
            {
                _rateLimiter.Release();
            }
        }
        
        private async Task<string> TranslateTextAsync(string text)
        {
            try
            {
                // Nếu văn bản quá ngắn, không cần dịch
                if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
                {
                    return text;
                }
                
                // Kiểm tra nếu văn bản đã có dấu tiếng Việt
                if (ContainsVietnamese(text))
                {
                    _logger.LogDebug($"Text appears to be Vietnamese, skipping translation: {text.Substring(0, Math.Min(50, text.Length))}");
                    return text;
                }
                
                // ƯU TIÊN sử dụng Google Translate API
                try
                {
                    var apiResult = await TryGoogleTranslateAsync(text);
                    if (!string.IsNullOrWhiteSpace(apiResult) && apiResult != text)
                    {
                        _logger.LogDebug($"Google Translate success: {text.Substring(0, Math.Min(30, text.Length))}... -> {apiResult.Substring(0, Math.Min(30, apiResult.Length))}...");
                        return apiResult;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Google Translate failed, trying backup API");
                }
                
                // Nếu Google Translate thất bại, thử MyMemory API
                try
                {
                    var backupResult = await TryMyMemoryTranslateAsync(text);
                    if (!string.IsNullOrWhiteSpace(backupResult) && backupResult != text)
                    {
                        _logger.LogDebug($"MyMemory Translate success: {text.Substring(0, Math.Min(30, text.Length))}... -> {backupResult.Substring(0, Math.Min(30, backupResult.Length))}...");
                        return backupResult;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MyMemory API failed, using fallback");
                }
                
                // Cuối cùng mới dùng fallback translation
                var fallbackResult = FallbackTranslate(text);
                if (fallbackResult != text && !string.IsNullOrWhiteSpace(fallbackResult))
                {
                    _logger.LogDebug($"Fallback translation: {text.Substring(0, Math.Min(30, text.Length))}... -> {fallbackResult.Substring(0, Math.Min(30, fallbackResult.Length))}...");
                    return fallbackResult;
                }
                
                // Trả về text gốc nếu không dịch được
                _logger.LogDebug($"No translation available for: {text.Substring(0, Math.Min(50, text.Length))}...");
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"All translation methods failed for text: {text}");
                return text;
            }
        }
        
        private bool ContainsVietnamese(string text)
        {
            // Kiểm tra các ký tự đặc trưng tiếng Việt
            var vietnameseChars = new[] { 'ă', 'â', 'đ', 'ê', 'ô', 'ơ', 'ư', 'à', 'á', 'ạ', 'ả', 'ã',
                                        'è', 'é', 'ẹ', 'ẻ', 'ẽ', 'ì', 'í', 'ị', 'ỉ', 'ĩ', 'ò', 'ó', 'ọ', 'ỏ', 'õ',
                                        'ù', 'ú', 'ụ', 'ủ', 'ũ', 'ỳ', 'ý', 'ỵ', 'ỷ', 'ỹ' };
            
            return text.ToLower().IndexOfAny(vietnameseChars) >= 0;
        }
        
        private async Task<string> TryGoogleTranslateAsync(string text)
        {
            try
            {
                // Giới hạn độ dài văn bản để tránh vượt quá giới hạn API
                if (text.Length > 500) // Giảm từ 1000 xuống 500
                {
                    text = text.Substring(0, 500);
                }
                
                // Thêm User-Agent để tránh bị chặn
                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{_primaryApiUrl}?client=gtx&sl=auto&tl=vi&dt=t&q={Uri.EscapeDataString(text)}");
                    
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                _logger.LogDebug($"Sending translation request to Google Translate API");
                
                // Thêm timeout ngắn hơn để tránh chờ quá lâu
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                
                // Gửi yêu cầu đến API
                var response = await _httpClient.SendAsync(request, cts.Token);
                
                // Kiểm tra phản hồi
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"Google Translate API returned status code: {response.StatusCode}");
                    return string.Empty;
                }
                
                // Đọc phản hồi
                var responseContent = await response.Content.ReadAsStringAsync();
                
                // Parse phản hồi JSON - Google Translate trả về mảng phức tạp
                using var doc = JsonDocument.Parse(responseContent);
                
                // Xây dựng kết quả dịch từ mảng các đoạn
                var translationArray = doc.RootElement[0];
                var stringBuilder = new StringBuilder();
                
                for (int i = 0; i < translationArray.GetArrayLength(); i++)
                {
                    var translatedPart = translationArray[i][0];
                    if (translatedPart.ValueKind != JsonValueKind.Null)
                    {
                        stringBuilder.Append(translatedPart.GetString());
                    }
                }
                
                var result = stringBuilder.ToString();
                _logger.LogDebug($"Google Translate successful: {text.Substring(0, Math.Min(50, text.Length))}... -> {result.Substring(0, Math.Min(50, result.Length))}...");
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Google Translate request timeout");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Error with Google Translate API");
                return string.Empty;
            }
        }
        
        private async Task<string> TryMyMemoryTranslateAsync(string text)
        {
            try
            {
                // Giới hạn độ dài văn bản
                if (text.Length > 400)
                {
                    text = text.Substring(0, 400);
                }
                
                // Xây dựng URL với langpair=en|vi để dịch từ tiếng Anh sang tiếng Việt
                string url = $"{_backupApiUrl}?q={Uri.EscapeDataString(text)}&langpair=en|vi";
                
                _logger.LogDebug($"Sending translation request to MyMemory API");
                
                // Thêm timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                
                // Gửi yêu cầu GET đến API
                var response = await _httpClient.GetAsync(url, cts.Token);
                
                // Kiểm tra phản hồi
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"MyMemory API returned status code: {response.StatusCode}");
                    return string.Empty;
                }
                
                // Đọc phản hồi
                var responseContent = await response.Content.ReadAsStringAsync();
                
                // Parse phản hồi JSON
                using var doc = JsonDocument.Parse(responseContent);
                
                if (doc.RootElement.TryGetProperty("responseData", out var responseData))
                {
                    if (responseData.TryGetProperty("translatedText", out var translatedTextElement))
                    {
                        var translatedText = translatedTextElement.GetString();
                        
                        if (!string.IsNullOrWhiteSpace(translatedText) && translatedText != text)
                        {
                            _logger.LogDebug($"MyMemory translation successful: {text.Substring(0, Math.Min(50, text.Length))}... -> {translatedText.Substring(0, Math.Min(50, translatedText.Length))}...");
                            return translatedText;
                        }
                    }
                }
                
                _logger.LogDebug("MyMemory API returned no valid translation");
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("MyMemory API request timeout");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Error with MyMemory API");
                return string.Empty;
            }
        }
        
        // Tùy chọn: Thêm phương thức dùng LibreTranslate nếu cần
        private async Task<string> TryLibreTranslateAsync(string text)
        {
            try
            {
                // Nếu văn bản quá dài, cắt bớt
                if (text.Length > 1000)
                {
                    text = text.Substring(0, 1000);
                }
                
                // Tạo JSON payload
                var payload = JsonSerializer.Serialize(new 
                {
                    q = text,
                    source = "auto",
                    target = "vi",
                    format = "text"
                });
                
                // Tạo request với Content-Type là application/json
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                
                _logger.LogInformation($"Sending translation request to LibreTranslate API");
                
                // Gửi POST request
                var response = await _httpClient.PostAsync(_libreTranlateUrl, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"LibreTranslate API returned status code: {response.StatusCode}");
                    return string.Empty;
                }
                
                // Đọc phản hồi
                var responseContent = await response.Content.ReadAsStringAsync();
                
                // Parse phản hồi JSON
                using var doc = JsonDocument.Parse(responseContent);
                var translatedText = doc.RootElement.GetProperty("translatedText").GetString();
                
                _logger.LogInformation($"LibreTranslate successful: {text} -> {translatedText}");
                
                return translatedText ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error with LibreTranslate API: {text}");
                return string.Empty;
            }
        }
        
        // Phương thức dự phòng với từ điển mở rộng
        private string FallbackTranslate(string text)
        {
            try
            {
                // Dictionary từ nhiều ngôn ngữ sang Tiếng Việt - Mở rộng hơn
                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Từ khóa tin tức phổ biến
                    {"breaking news", "tin nóng"}, {"latest news", "tin mới nhất"}, {"news update", "cập nhật tin tức"},
                    {"top story", "tin hàng đầu"}, {"headline", "tiêu đề"}, {"report", "báo cáo"},
                    
                    // Địa danh
                    {"Vietnam", "Việt Nam"}, {"Vietnamese", "Việt Nam"}, {"Hanoi", "Hà Nội"}, 
                    {"Ho Chi Minh City", "Thành phố Hồ Chí Minh"}, {"Saigon", "Sài Gòn"},
                    {"Da Nang", "Đà Nẵng"}, {"Hue", "Huế"}, {"Can Tho", "Cần Thơ"},
                    
                    // Kinh tế
                    {"economy", "kinh tế"}, {"economic", "kinh tế"}, {"GDP", "GDP"}, {"growth", "tăng trưởng"},
                    {"investment", "đầu tư"}, {"trade", "thương mại"}, {"export", "xuất khẩu"}, {"import", "nhập khẩu"},
                    {"market", "thị trường"}, {"stock", "cổ phiếu"}, {"business", "kinh doanh"}, {"company", "công ty"},
                    {"corporation", "tập đoàn"}, {"industry", "công nghiệp"}, {"manufacturing", "sản xuất"},
                    {"technology", "công nghệ"}, {"innovation", "sáng tạo"}, {"startup", "khởi nghiệp"},
                    
                    // Chính trị
                    {"politics", "chính trị"}, {"political", "chính trị"}, {"government", "chính phủ"},
                    {"president", "chủ tịch"}, {"minister", "bộ trưởng"}, {"ministry", "bộ"},
                    {"parliament", "quốc hội"}, {"policy", "chính sách"}, {"law", "luật"}, {"legal", "pháp lý"},
                    
                    // Xã hội
                    {"society", "xã hội"}, {"social", "xã hội"}, {"people", "người dân"}, {"citizen", "công dân"},
                    {"education", "giáo dục"}, {"health", "sức khỏe"}, {"healthcare", "chăm sóc sức khỏe"},
                    {"hospital", "bệnh viện"}, {"school", "trường"}, {"university", "đại học"},
                    
                    // Thể thao
                    {"sports", "thể thao"}, {"football", "bóng đá"}, {"soccer", "bóng đá"}, {"basketball", "bóng rổ"},
                    {"volleyball", "bóng chuyền"}, {"tennis", "quần vợt"}, {"swimming", "bơi lội"},
                    {"athletics", "điền kinh"}, {"championship", "giải vô địch"}, {"tournament", "giải đấu"},
                    {"Olympic", "Olympic"}, {"medal", "huy chương"}, {"victory", "chiến thắng"},
                    
                    // Giải trí
                    {"entertainment", "giải trí"}, {"movie", "phim"}, {"film", "phim"}, {"cinema", "rạp chiếu phim"},
                    {"music", "nhạc"}, {"singer", "ca sĩ"}, {"actor", "diễn viên"}, {"actress", "nữ diễn viên"},
                    {"celebrity", "ngôi sao"}, {"festival", "lễ hội"}, {"concert", "hòa nhạc"},
                    
                    // Thời tiết và môi trường
                    {"weather", "thời tiết"}, {"climate", "khí hậu"}, {"environment", "môi trường"},
                    {"pollution", "ô nhiễm"}, {"typhoon", "bão"}, {"flood", "lũ lụt"}, {"drought", "hạn hán"},
                    
                    // Giao thông
                    {"traffic", "giao thông"}, {"transport", "vận tải"}, {"airport", "sân bay"}, 
                    {"highway", "đường cao tốc"}, {"bridge", "cầu"}, {"railway", "đường sắt"},
                    
                    // Công nghệ
                    {"internet", "internet"}, {"digital", "kỹ thuật số"}, {"smartphone", "điện thoại thông minh"},
                    {"artificial intelligence", "trí tuệ nhân tạo"}, {"AI", "AI"}, {"robot", "robot"},
                    
                    // Từ vựng chung
                    {"new", "mới"}, {"old", "cũ"}, {"big", "lớn"}, {"small", "nhỏ"}, {"good", "tốt"}, {"bad", "xấu"},
                    {"important", "quan trọng"}, {"significant", "đáng kể"}, {"major", "chính"}, {"minor", "phụ"},
                    {"increase", "tăng"}, {"decrease", "giảm"}, {"improve", "cải thiện"}, {"develop", "phát triển"},
                    {"successful", "thành công"}, {"failure", "thất bại"}, {"progress", "tiến bộ"}, {"achievement", "thành tựu"},
                    
                    // Thời gian
                    {"today", "hôm nay"}, {"yesterday", "hôm qua"}, {"tomorrow", "ngày mai"}, 
                    {"morning", "sáng"}, {"afternoon", "chiều"}, {"evening", "tối"}, {"night", "đêm"},
                    {"week", "tuần"}, {"month", "tháng"}, {"year", "năm"}, {"century", "thế kỷ"},
                    
                    // Nước khác
                    {"China", "Trung Quốc"}, {"Chinese", "Trung Quốc"}, {"Japan", "Nhật Bản"}, {"Japanese", "Nhật Bản"},
                    {"Korea", "Hàn Quốc"}, {"Korean", "Hàn Quốc"}, {"Thailand", "Thái Lan"}, {"Singapore", "Singapore"},
                    {"America", "Mỹ"}, {"American", "Mỹ"}, {"USA", "Mỹ"}, {"Europe", "Châu Âu"}, {"European", "Châu Âu"}
                };
                
                string result = text;
                
                // Thay thế các từ/cụm từ
                foreach (var entry in translations)
                {
                    // Sử dụng Regex để thay thế chính xác hơn
                    var pattern = $@"\b{Regex.Escape(entry.Key)}\b";
                    result = Regex.Replace(result, pattern, entry.Value, RegexOptions.IgnoreCase);
                }
                
                return result;
            }
            catch
            {
                return text; // Trả về văn bản gốc nếu có lỗi
            }
        }

        public string ConvertUtcToVnTime(string? utcTimeStr)
        {
            if (string.IsNullOrEmpty(utcTimeStr))
                return string.Empty;

            try
            {
                if (DateTime.TryParse(utcTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime utcTime))
                {
                    // Chuyển đổi UTC sang giờ Việt Nam (UTC+7)
                    var vnTime = utcTime.AddHours(7);
                    return vnTime.ToString("yyyy-MM-dd HH:mm:ss") + " (Giờ VN)";
                }
                return utcTimeStr;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting time: {utcTimeStr}");
                return utcTimeStr;
            }
        }
    }
}