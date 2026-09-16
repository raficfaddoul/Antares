// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using AliantWebS.Data;
using Microsoft.Extensions.Localization;
using AliantWebS.Pages;

namespace AliantWebS.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly IStringLocalizer<Languages> _localizer;
        private readonly JournalService _journal;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            ILogger<LoginModel> logger,
            IStringLocalizer<Languages> localizer,
            JournalService journal)
        {
            _signInManager = signInManager;
            _logger = logger;
            _localizer = localizer;
            _journal = journal;
        }

        public IStringLocalizer<Languages> L => _localizer;

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "RememberMe", ResourceType = typeof(Languages))]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/SurveillanceAntares");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl = Url.Content("~/SurveillanceAntares");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (string.IsNullOrWhiteSpace(Input.Email))
            {
                ModelState.AddModelError(nameof(Input.Email), _localizer["EmailRequired"]);
            }

            if (string.IsNullOrWhiteSpace(Input.Password))
            {
                ModelState.AddModelError(nameof(Input.Password), _localizer["PasswordRequired"]);
            }

            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

                // Journaled with an explicit identity: this runs as a plain Razor Page request, so
                // there is no Blazor circuit for JournalService to read the current user from.
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    await _journal.WriteAsAsync(Input.Email, Input.Email,
                        JournalCategory.Session, "session.login", details: RemoteIp());
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    await _journal.WriteAsAsync(Input.Email, Input.Email,
                        JournalCategory.Session, "session.lockout", details: RemoteIp(), severity: 2);
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    await _journal.WriteAsAsync(Input.Email, Input.Email,
                        JournalCategory.Session, "session.login.failed", details: RemoteIp(), severity: 1);
                    ModelState.AddModelError(string.Empty, _localizer["InvalidLoginAttempt"]);
                    return Page();
                }
            }

            return Page();
        }

        /// <summary>Client address for the journal. Behind nginx the forwarded header is the real one.</summary>
        private string RemoteIp()
        {
            var fwd = HttpContext?.Request?.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd)) return $"IP {fwd.Split(',')[0].Trim()}";

            var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString();
            return string.IsNullOrWhiteSpace(ip) ? null : $"IP {ip}";
        }
    }
}
