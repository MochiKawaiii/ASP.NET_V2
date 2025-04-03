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
            var viewModel = new NewsViewModel
            {
                ActiveTab = activeTab,
                CategoryName = categoryName,
                Query = query // Lưu từ khóa tìm kiếm vào model để hiển thị trong form
            };

            try
            {
                // Tăng số lượng kết quả tìm kiếm
                var articles = await _newsService.GetArticlesAsync(query, null, "relevancy", 50);
                
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi tải tin tức cho danh mục {categoryName}: {ex.Message}");
                viewModel.ErrorMessage = $"Đã xảy ra lỗi khi tải dữ liệu: {ex.Message}";
            }

            return viewModel;
        }

        private async Task<NewsViewModel> ProcessSearch(NewsViewModel model)
        {
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
                    50);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi tìm kiếm: {ex.Message}");
                viewModel.ErrorMessage = $"Đã xảy ra lỗi khi tìm kiếm: {ex.Message}";
            }

            return viewModel;
        }

        private async Task<List<Article>> TranslateArticlesAsync(List<Article> articles)
        {
            var translatedArticles = new List<Article>();

            foreach (var article in articles)
            {
                try
                {
                    var (translatedTitle, translatedDescription) = await _translationService.TranslateArticleAsync(
                        article.Title, 
                        article.Description);

                    article.TranslatedTitle = translatedTitle;
                    article.TranslatedDescription = translatedDescription;
                    article.VnPublishedAt = _translationService.ConvertUtcToVnTime(article.PublishedAt);

                    translatedArticles.Add(article);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Lỗi khi dịch bài viết: {ex.Message}");
                    // Vẫn thêm bài viết ngay cả khi không dịch được
                    article.TranslatedTitle = article.Title;
                    article.TranslatedDescription = article.Description;
                    article.VnPublishedAt = _translationService.ConvertUtcToVnTime(article.PublishedAt);
                    translatedArticles.Add(article);
                }
            }

            return translatedArticles;
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