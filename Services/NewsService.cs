using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VtvNewsApp.Models;

namespace VtvNewsApp.Services
{
    public class NewsService : INewsService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NewsService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _newsApiKey = "3aced82ed23d48b9af48973f9bde61b4"; // Key được cung cấp
        private readonly string _gNewsApiKey = "4dee47ab8112c1b949ecda490be79a17"; // Key được cung cấp
        private bool _useMockData = false;

        public NewsService(HttpClient httpClient, ILogger<NewsService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            
            try 
            {
                // Cấu hình HttpClient với User-Agent header
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("VtvNewsApp", "1.0"));
                
                // Tăng timeout cho độ tin cậy
                _httpClient.Timeout = TimeSpan.FromSeconds(30); // Tăng timeout
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cấu hình HttpClient");
                _useMockData = true;
            }
        }

        public async Task<List<Article>> GetArticlesAsync(string query, DateTime? fromDate, string sortBy, int pageSize = 100)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Nếu truy vấn trống, sử dụng "vietnam" làm từ khóa mặc định
                query = "vietnam";
            }
            else if (!query.ToLower().Contains("vietnam"))
            {
                // Thêm từ khóa vietnam vào tất cả các truy vấn
                query = $"{query} AND vietnam";
            }

            try
            {
                // Thử với NewsAPI trước
                return await GetArticlesFromNewsApiAsync(query, fromDate, sortBy, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi gọi NewsAPI: {ex.Message}. Đang thử với GNewsAPI...");
                
                try
                {
                    // Nếu NewsAPI thất bại, thử với GNews
                    return await GetArticlesFromGNewsAsync(query, fromDate, sortBy, pageSize);
                }
                catch (Exception gEx)
                {
                    _logger.LogError(gEx, $"Lỗi khi gọi GNewsAPI: {gEx.Message}. Trả về dữ liệu mẫu.");
                    return GetMockData(query);
                }
            }
        }

        private async Task<List<Article>> GetArticlesFromNewsApiAsync(string query, DateTime? fromDate, string sortBy, int pageSize)
        {
            // URL cơ bản của NewsAPI
            var url = "https://newsapi.org/v2/everything";
            
            // Tham số cần thiết - loại bỏ giới hạn ngôn ngữ
            var queryParams = new Dictionary<string, string>
            {
                { "q", query },
                { "apiKey", _newsApiKey },
                { "pageSize", pageSize.ToString() },
                // Bỏ tham số language để lấy tin bằng tất cả ngôn ngữ
                { "sortBy", sortBy ?? "publishedAt" }
            };

            // Thêm tham số từ ngày nếu có
            if (fromDate.HasValue)
            {
                queryParams.Add("from", fromDate.Value.ToString("yyyy-MM-dd"));
            }

            // Tạo URL với tham số
            var requestUrl = url + "?" + string.Join("&", queryParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            
            _logger.LogInformation($"Gọi NewsAPI với URL: {requestUrl}");
            
            // Tạo request mới với đầy đủ headers
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", "VtvNewsApp/1.0");
            request.Headers.Add("X-Api-Key", _newsApiKey);
            
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Lỗi từ NewsAPI: {response.StatusCode} - {errorContent}");
                throw new Exception($"NewsAPI trả về lỗi: {response.StatusCode}");
            }
            
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug($"Phản hồi từ NewsAPI: {content}");
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var newsApiResponse = JsonSerializer.Deserialize<NewsApiResponse>(content, options);
            
            if (newsApiResponse == null || newsApiResponse.Articles == null || !newsApiResponse.Articles.Any())
            {
                _logger.LogWarning("Không tìm thấy kết quả từ NewsAPI");
                throw new Exception("Không có kết quả từ NewsAPI");
            }
            
            return newsApiResponse.Articles;
        }

        private async Task<List<Article>> GetArticlesFromGNewsAsync(string query, DateTime? fromDate, string sortBy, int pageSize)
        {
            // URL cơ bản của GNews API
            var url = "https://gnews.io/api/v4/search";
            
            // Tham số cần thiết - loại bỏ giới hạn ngôn ngữ
            var queryParams = new Dictionary<string, string>
            {
                { "q", query },
                { "token", _gNewsApiKey },
                { "max", pageSize.ToString() }
                // Bỏ tham số lang để lấy tin bằng tất cả ngôn ngữ
            };

            // Thêm tham số từ ngày nếu có
            if (fromDate.HasValue)
            {
                queryParams.Add("from", fromDate.Value.ToString("yyyy-MM-dd"));
            }

            // Tạo URL với tham số
            var requestUrl = url + "?" + string.Join("&", queryParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            
            _logger.LogInformation($"Gọi GNews API với URL: {requestUrl}");
            
            // Tạo request mới với đầy đủ headers
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("User-Agent", "VtvNewsApp/1.0");
            
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Lỗi từ GNews API: {response.StatusCode} - {errorContent}");
                throw new Exception($"GNews API trả về lỗi: {response.StatusCode}");
            }
            
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug($"Phản hồi từ GNews API: {content}");
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var gnewsResponse = JsonSerializer.Deserialize<GNewsResponse>(content, options);
            
            if (gnewsResponse == null || gnewsResponse.Articles == null || !gnewsResponse.Articles.Any())
            {
                _logger.LogWarning("Không tìm thấy kết quả từ GNews API");
                throw new Exception("Không có kết quả từ GNews API");
            }
            
            // Chuyển đổi từ định dạng GNews sang định dạng Article của ứng dụng
            return gnewsResponse.Articles.Select(gnewsArticle => new Article
            {
                Source = new Source
                {
                    Id = gnewsArticle.Source?.Id,
                    Name = gnewsArticle.Source?.Name
                },
                Author = gnewsArticle.Source?.Name, // GNews không cung cấp tác giả
                Title = gnewsArticle.Title,
                Description = gnewsArticle.Description,
                Url = gnewsArticle.Url,
                UrlToImage = gnewsArticle.Image,
                PublishedAt = gnewsArticle.PublishedAt,
                Content = gnewsArticle.Content
            }).ToList();
        }

        public List<Article> FilterArticles(List<Article> articles, string term)
        {
            if (string.IsNullOrWhiteSpace(term)) 
                return articles;
                
            // Tách các từ khóa tìm kiếm và lọc theo từng từ 
            var searchTerms = term.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2) // Chỉ sử dụng từ có ý nghĩa
                .ToList();
            
            if (!searchTerms.Any())
                return articles;
            
            // Trả về bài viết có chứa ít nhất một từ khóa
            return articles.Where(article => 
            {
                string title = (article.Title ?? "").ToLower();
                string description = (article.Description ?? "").ToLower();
                string content = (article.Content ?? "").ToLower();
                
                return searchTerms.Any(term => 
                    title.Contains(term) || 
                    description.Contains(term) || 
                    content.Contains(term)
                );
            }).ToList();
        }
        
        // Dữ liệu mẫu sẽ được sử dụng khi API không hoạt động
        private List<Article> GetMockData(string query)
        {
            var currentTime = DateTime.UtcNow.ToString("o");
            
            return new List<Article>
            {
                new Article 
                {
                    Title = "Vietnam's Economy Shows Strong Growth Despite Global Challenges",
                    Description = "Vietnam continues to be one of the fastest-growing economies in Southeast Asia, with GDP growth exceeding expectations in the latest quarter.",
                    UrlToImage = "https://via.placeholder.com/400x250?text=Vietnam+Economy",
                    PublishedAt = currentTime,
                    Url = "#",
                    Source = new Source { Name = "Economic Times" }
                },
                new Article 
                {
                    Title = "Tourism in Vietnam Rebounds to Pre-Pandemic Levels",
                    Description = "International arrivals to Vietnam have surpassed pre-COVID numbers, with significant increases in visitors from Europe, North America, and neighboring Asian countries.",
                    UrlToImage = "https://via.placeholder.com/400x250?text=Vietnam+Tourism",
                    PublishedAt = currentTime,
                    Url = "#",
                    Source = new Source { Name = "Travel Weekly" }
                },
                new Article 
                {
                    Title = "Vietnam Emerges as Key Manufacturing Hub Amid Supply Chain Shifts",
                    Description = "Global companies continue to relocate manufacturing operations to Vietnam as part of their 'China plus one' strategy, driving industrial growth and foreign investment.",
                    UrlToImage = "https://via.placeholder.com/400x250?text=Vietnam+Manufacturing",
                    PublishedAt = currentTime,
                    Url = "#",
                    Source = new Source { Name = "Business Insider" }
                },
                new Article 
                {
                    Title = "Vietnam's Tech Startups Attract Record Investment",
                    Description = "Venture capital flowing into Vietnamese tech startups reached an all-time high this year, with fintech, e-commerce, and edtech sectors leading the way.",
                    UrlToImage = "https://via.placeholder.com/400x250?text=Vietnam+Tech",
                    PublishedAt = currentTime,
                    Url = "#",
                    Source = new Source { Name = "Tech in Asia" }
                },
                new Article 
                {
                    Title = "Vietnam's Coffee Exports Set New Records",
                    Description = "Vietnam, the world's second-largest coffee producer, has reported record export volumes this year despite climate challenges affecting global supply.",
                    UrlToImage = "https://via.placeholder.com/400x250?text=Vietnam+Coffee",
                    PublishedAt = currentTime,
                    Url = "#",
                    Source = new Source { Name = "Reuters" }
                }
            };
        }
    }
    
    // Lớp phụ để deserialization response từ GNews API
    public class GNewsResponse
    {
        public int TotalArticles { get; set; }
        public List<GNewsArticle> Articles { get; set; } = new List<GNewsArticle>();
    }

    public class GNewsArticle
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Content { get; set; }
        public string? Url { get; set; }
        public string? Image { get; set; }
        public string? PublishedAt { get; set; }
        public GNewsSource? Source { get; set; }
    }

    public class GNewsSource
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Id { get; set; }
    }
}