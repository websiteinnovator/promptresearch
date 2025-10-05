// Controllers/HomeController.cs
using Microsoft.AspNetCore.Mvc;
using PromptPlatform.Services;
using PromptPlatform.ViewModels;

namespace PromptPlatform.Controllers
{
    public class HomeController : Controller
    {
        private readonly IPromptService _promptService;

        public HomeController(IPromptService promptService)
        {
            _promptService = promptService;
        }

        public async Task<IActionResult> Index(PromptSearchViewModel model)
        {
            var userId = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            var language = Request.Cookies["Language"] ?? "en";
            
            var result = await _promptService.SearchPromptsAsync(model, userId, language);
            return View(result);
        }

        [HttpPost]
        public IActionResult SetLanguage(string language, string returnUrl)
        {
            Response.Cookies.Append("Language", language, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
            return LocalRedirect(returnUrl ?? "/");
        }
    }
}

// Controllers/PromptController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PromptPlatform.Services;
using PromptPlatform.ViewModels;

namespace PromptPlatform.Controllers
{
    public class PromptController : Controller
    {
        private readonly IPromptService _promptService;

        public PromptController(IPromptService promptService)
        {
            _promptService = promptService;
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            var language = Request.Cookies["Language"] ?? "en";
            
            var prompt = await _promptService.GetPromptDetailAsync(id, userId, language);
            if (prompt == null)
                return NotFound();

            return View(prompt);
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePreview([FromBody] PromptPreviewRequest request)
        {
            var userId = User.Identity?.IsAuthenticated == true ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value : null;
            var sessionId = HttpContext.Session.Id;
            var language = Request.Cookies["Language"] ?? "en";

            // Check usage limit
            var canUse = await _promptService.CanUsePromptAsync(userId, sessionId);
            if (!canUse)
            {
                return Json(new { success = false, message = "Daily limit reached. Please log in for unlimited access." });
            }

            try
            {
                var preview = await _promptService.GeneratePreviewAsync(request, userId, language);
                
                // Log usage
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _promptService.LogUsageAsync(request.PromptTemplateId, userId, sessionId, ipAddress);

                return Json(new { success = true, data = preview });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ToggleLike(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var isLiked = await _promptService.ToggleLikeAsync(id, userId);
            return Json(new { success = true, isLiked });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddComment([FromBody] AddCommentViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var success = await _promptService.AddCommentAsync(model, userId);
            return Json(new { success });
        }

        [Authorize]
        public IActionResult Create()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create(CreatePromptViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var promptId = await _promptService.CreatePromptAsync(model, userId);
            return RedirectToAction(nameof(Details), new { id = promptId });
        }
    }
}

// Controllers/ProfileController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PromptPlatform.Services;
using PromptPlatform.ViewModels;

namespace PromptPlatform.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IUserProfileService _userProfileService;

        public ProfileController(IUserProfileService userProfileService)
        {
            _userProfileService = userProfileService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var profile = await _userProfileService.GetUserProfileAsync(userId);
            return View(profile);
        }

        [HttpPost]
        public async Task<IActionResult> Update(UserProfileViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var success = await _userProfileService.UpdateUserProfileAsync(userId, model);
            if (success)
            {
                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = "Failed to update profile.";
            return View("Index", model);
        }
    }
}

// Controllers/Api/CompanyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PromptPlatform.Data;
using PromptPlatform.Models;
using PromptPlatform.ViewModels;

namespace PromptPlatform.Controllers.Api
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CompanyController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CompanyController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var companies = await _context.Companies
                .Where(c => c.UserId == userId)
                .Select(c => new CompanyViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Industry = c.Industry,
                    Description = c.Description,
                    ProductsServices = c.ProductsServices,
                    ValueProposition = c.ValueProposition,
                    TargetMarket = c.TargetMarket,
                    Website = c.Website,
                    CompanySize = c.CompanySize,
                    IsPrimary = c.IsPrimary
                })
                .ToListAsync();

            return Ok(companies);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CompanyViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var company = new Company
            {
                UserId = userId,
                Name = model.Name,
                Industry = model.Industry,
                Description = model.Description,
                ProductsServices = model.ProductsServices,
                ValueProposition = model.ValueProposition,
                TargetMarket = model.TargetMarket,
                Website = model.Website,
                CompanySize = model.CompanySize,
                IsPrimary = model.IsPrimary
            };

            // If this is primary, unset other primary companies
            if (company.IsPrimary)
            {
                var otherCompanies = await _context.Companies.Where(c => c.UserId == userId && c.IsPrimary).ToListAsync();
                foreach (var c in otherCompanies)
                {
                    c.IsPrimary = false;
                }
            }

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            return Ok(new { id = company.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompanyViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (company == null)
                return NotFound();

            company.Name = model.Name;
            company.Industry = model.Industry;
            company.Description = model.Description;
            company.ProductsServices = model.ProductsServices;
            company.ValueProposition = model.ValueProposition;
            company.TargetMarket = model.TargetMarket;
            company.Website = model.Website;
            company.CompanySize = model.CompanySize;
            company.IsPrimary = model.IsPrimary;
            company.UpdatedAt = DateTime.UtcNow;

            if (company.IsPrimary)
            {
                var otherCompanies = await _context.Companies.Where(c => c.UserId == userId && c.Id != id && c.IsPrimary).ToListAsync();
                foreach (var c in otherCompanies)
                {
                    c.IsPrimary = false;
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (company == null)
                return NotFound();

            _context.Companies.Remove(company);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CompetitorController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CompetitorController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var competitors = await _context.Competitors
                .Where(c => c.UserId == userId)
                .Select(c => new CompetitorViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Website = c.Website,
                    Strengths = c.Strengths,
                    Weaknesses = c.Weaknesses,
                    PricingStrategy = c.PricingStrategy,
                    MarketPosition = c.MarketPosition
                })
                .ToListAsync();

            return Ok(competitors);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CompetitorViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var competitor = new Competitor
            {
                UserId = userId,
                Name = model.Name,
                Website = model.Website,
                Strengths = model.Strengths,
                Weaknesses = model.Weaknesses,
                PricingStrategy = model.PricingStrategy,
                MarketPosition = model.MarketPosition
            };

            _context.Competitors.Add(competitor);
            await _context.SaveChangesAsync();

            return Ok(new { id = competitor.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CompetitorViewModel model)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var competitor = await _context.Competitors.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (competitor == null)
                return NotFound();

            competitor.Name = model.Name;
            competitor.Website = model.Website;
            competitor.Strengths = model.Strengths;
            competitor.Weaknesses = model.Weaknesses;
            competitor.PricingStrategy = model.PricingStrategy;
            competitor.MarketPosition = model.MarketPosition;
            competitor.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var competitor = await _context.Competitors.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (competitor == null)
                return NotFound();

            _context.Competitors.Remove(competitor);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
