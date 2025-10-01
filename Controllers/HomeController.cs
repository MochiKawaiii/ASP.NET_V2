using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VtvNewsApp.Models;
using VtvNewsApp.Services;

namespace VtvNewsApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly INewsService _newsService;
        private readonly ITranslationService _translationService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            INewsService newsService, 
            ITranslationService translationService, 
            ILogger<HomeController> logger)
        {
            _newsService = newsService;
            _translationService = translationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Mở rộng từ khóa tìm kiếm để có nhiều kết quả hơn
            var viewModel = await GetNewsViewModel("vietnam news latest", "vietnam", "Trang Chủ");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Index(NewsViewModel model)
        {
            // Đảm bảo từ khóa tìm kiếm được sử dụng đúng
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [HttpGet]
        public async Task<IActionResult> ThoiSu()
        {
            var viewModel = await GetNewsViewModel("vietnam politics", "thoisu", "Thời Sự");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ThoiSu(NewsViewModel model)
        {
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [HttpGet]
        public async Task<IActionResult> KinhTe()
        {
            var viewModel = await GetNewsViewModel("vietnam economy", "kinhte", "Kinh Tế");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> KinhTe(NewsViewModel model)
        {
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [HttpGet]
        public async Task<IActionResult> TheGioi()
        {
            var viewModel = await GetNewsViewModel("vietnam international", "thegioi", "Thế Giới");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> TheGioi(NewsViewModel model)
        {
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [HttpGet]
        public async Task<IActionResult> TheThao()
        {
            var viewModel = await GetNewsViewModel("vietnam sports", "thethao", "Thể Thao");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> TheThao(NewsViewModel model)
        {
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [HttpGet]
        public async Task<IActionResult> GiaiTri()
        {
            var viewModel = await GetNewsViewModel("vietnam entertainment", "giaitri", "Giải Trí");
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> GiaiTri(NewsViewModel model)
        {
            var resultModel = await ProcessSearch(model);
            return View(resultModel);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // Phương thức hỗ trợ
        private async Task<NewsViewModel> GetNewsViewModel(string query, string activeTab, string categoryName)
        {
            var stopwatch = Stopwatch.StartNew();
            var viewModel = new NewsViewModel
            {
                ActiveTab = activeTab,
                CategoryName = categoryName,
                Query = query // Lưu từ khóa tìm kiếm vào model để hiển thị trong form
            };

            try
            {
                // Tăng số lượng kết quả tìm kiếm lên 100
                var articles = await _newsService.GetArticlesAsync(query, null, "relevancy", 100);
                
                if (articles == null || !articles.Any())
                {
                    _logger.LogWarning($"Không tìm thấy bài viết nào cho danh mục {categoryName} với từ khóa {query}");
                    
                    // Thử lại với ít từ khóa hơn nếu không tìm thấy kết quả
                    string simpleQuery = GetSimplifiedQuery(query);
                    if (simpleQuery != query)
                    {
                        articles = await _newsService.GetArticlesAsync(simpleQuery, null, "relevancy", 50);
                    }
                    
                    if (articles == null || !articles.Any())
                    {
                        viewModel.ErrorMessage = "Không tìm thấy bài viết phù hợp. Vui lòng thử lại sau.";
                        return viewModel;
                    }
                }
                
                // Không lọc kết quả theo từ khóa để hiển thị nhiều bài viết hơn
                viewModel.Articles = await TranslateArticlesAsync(articles);
                
                // Thêm thông tin thống kê
                stopwatch.Stop();
                viewModel.LoadTimeMs = stopwatch.ElapsedMilliseconds;
                viewModel.TotalArticles = viewModel.Articles?.Count ?? 0;
                viewModel.TranslatedCount = viewModel.Articles?.Count(a => !string.IsNullOrEmpty(a.TranslatedTitle)) ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi tải tin tức cho danh mục {categoryName}: {ex.Message}");
                viewModel.ErrorMessage = $"Đã xảy ra lỗi khi tải dữ liệu: {ex.Message}";
                stopwatch.Stop();
                viewModel.LoadTimeMs = stopwatch.ElapsedMilliseconds;
            }

            return viewModel;
        }

        private async Task<NewsViewModel> ProcessSearch(NewsViewModel model)
        {
            var stopwatch = Stopwatch.StartNew();
            var viewModel = new NewsViewModel
            {
                ActiveTab = model.ActiveTab,
                CategoryName = model.CategoryName,
                Query = model.Query,
                FromDate = model.FromDate,
                SortBy = model.SortBy
            };

            try
            {
                DateTime? fromDate = null;
                if (!string.IsNullOrEmpty(model.FromDate))
                {
                    fromDate = DateTime.Parse(model.FromDate);
                }

                // Đảm bảo truy vấn tìm kiếm không trống
                var query = !string.IsNullOrWhiteSpace(model.Query) ? model.Query : "vietnam";

                var articles = await _newsService.GetArticlesAsync(
                    query,
                    fromDate,
                    model.SortBy ?? "relevancy",
                    100);

                if (articles == null || !articles.Any())
                {
                    _logger.LogWarning($"Không tìm thấy kết quả tìm kiếm cho {query}");
                    
                    // Thử lại với ít từ khóa hơn nếu không tìm thấy kết quả
                    string simpleQuery = GetSimplifiedQuery(query);
                    if (simpleQuery != query)
                    {
                        articles = await _newsService.GetArticlesAsync(simpleQuery, fromDate, model.SortBy ?? "relevancy", 50);
                    }
                    
                    if (articles == null || !articles.Any())
                    {
                        viewModel.ErrorMessage = "Không tìm thấy kết quả phù hợp với từ khóa tìm kiếm.";
                        return viewModel;
                    }
                }

                // Không lọc kết quả để hiển thị đủ bài viết tìm được
                viewModel.Articles = await TranslateArticlesAsync(articles);
                
                // Thêm thông tin thống kê
                stopwatch.Stop();
                viewModel.LoadTimeMs = stopwatch.ElapsedMilliseconds;
                viewModel.TotalArticles = viewModel.Articles?.Count ?? 0;
                viewModel.TranslatedCount = viewModel.Articles?.Count(a => !string.IsNullOrEmpty(a.TranslatedTitle)) ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi tìm kiếm: {ex.Message}");
                viewModel.ErrorMessage = $"Đã xảy ra lỗi khi tìm kiếm: {ex.Message}";
                stopwatch.Stop();
                viewModel.LoadTimeMs = stopwatch.ElapsedMilliseconds;
            }

            return viewModel;
        }

        private async Task<List<Article>> TranslateArticlesAsync(List<Article> articles)
        {
            var translatedArticles = new List<Article>();

            // Tăng giới hạn lên 30 bài và xử lý theo batch
            var limitedArticles = articles.Take(30).ToList();
            
            // Chia thành các batch nhỏ để xử lý song song
            const int batchSize = 5;
            var batches = new List<List<Article>>();
            
            for (int i = 0; i < limitedArticles.Count; i += batchSize)
            {
                batches.Add(limitedArticles.Skip(i).Take(batchSize).ToList());
            }
            
            // Xử lý từng batch
            foreach (var batch in batches)
            {
                var batchTasks = batch.Select(async article =>
                {
                    try
                    {
                        // Kiểm tra xem có cần dịch không
                        bool needsTranslation = !string.IsNullOrEmpty(article.Title) && 
                                              !ContainsVietnamese(article.Title);
                        
                        if (needsTranslation)
                        {
                            var (translatedTitle, translatedDescription) = await _translationService.TranslateArticleAsync(
                                article.Title, 
                                article.Description);

                            article.TranslatedTitle = translatedTitle;
                            article.TranslatedDescription = translatedDescription;
                        }
                        else
                        {
                            // Nếu không cần dịch, sử dụng văn bản gốc
                            article.TranslatedTitle = article.Title;
                            article.TranslatedDescription = article.Description;
                        }
                        
                        article.VnPublishedAt = _translationService.ConvertUtcToVnTime(article.PublishedAt);
                        return article;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Lỗi khi dịch bài viết: {ex.Message}");
                        // Vẫn thêm bài viết ngay cả khi không dịch được
                        article.TranslatedTitle = article.Title;
                        article.TranslatedDescription = article.Description;
                        article.VnPublishedAt = _translationService.ConvertUtcToVnTime(article.PublishedAt);
                        return article;
                    }
                });
                
                // Chờ tất cả articles trong batch này hoàn thành
                var batchResults = await Task.WhenAll(batchTasks);
                translatedArticles.AddRange(batchResults);
                
                // Delay giữa các batch để tránh spam
                if (batches.IndexOf(batch) < batches.Count - 1)
                {
                    await Task.Delay(500); // Delay 0.5 giây giữa các batch
                }
            }

            return translatedArticles;
        }
        
        // Kiểm tra xem text có chứa tiếng Việt không
        private bool ContainsVietnamese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            // Kiểm tra các ký tự đặc trưng tiếng Việt
            var vietnameseChars = new[] { 'ă', 'â', 'đ', 'ê', 'ô', 'ơ', 'ư', 'à', 'á', 'ạ', 'ả', 'ã',
                                        'è', 'é', 'ẹ', 'ẻ', 'ẽ', 'ì', 'í', 'ị', 'ỉ', 'ĩ', 'ò', 'ó', 'ọ', 'ỏ', 'õ',
                                        'ù', 'ú', 'ụ', 'ủ', 'ũ', 'ỳ', 'ý', 'ỵ', 'ỷ', 'ỹ' };
            
            return text.ToLower().IndexOfAny(vietnameseChars) >= 0;
        }
        
        // Rút gọn từ khóa tìm kiếm khi không tìm thấy kết quả
        private string GetSimplifiedQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "vietnam";
                
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 2)
                return query;
            
            // Đảm bảo "vietnam" luôn có trong truy vấn đơn giản hóa
            var hasVietnam = words.Any(w => w.Equals("vietnam", StringComparison.OrdinalIgnoreCase));
            
            // Chỉ lấy 2-3 từ khóa quan trọng nhất
            var simplifiedWords = words.Take(3).ToList();
            
            if (!hasVietnam)
            {
                simplifiedWords.Insert(0, "vietnam");
            }
            
            return string.Join(" ", simplifiedWords);
        }
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}