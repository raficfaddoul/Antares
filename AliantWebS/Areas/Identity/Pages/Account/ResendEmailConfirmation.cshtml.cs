// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AliantWebS.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Retired by the administrator-invitation flow, and closed rather than merely unlinked.
    ///
    /// This page used to mail a bare ConfirmEmail link. That link confirms the address WITHOUT
    /// setting a password, which left an invited user worse off than before: still unable to sign
    /// in, because SignIn.RequireConfirmedAccount is not the obstacle -- the missing password is --
    /// and no longer visible to the administrator either, because the "Renvoyer l'invitation"
    /// button in UserManager.razor is shown only while EmailConfirmed is false. Using this page
    /// made an account unusable and invisible in the same click.
    ///
    /// An invited user who never activated asks the administrator to resend the invitation. That
    /// link sets the password AND confirms the address in one step (AcceptInvitation).
    ///
    /// Both handlers redirect. Dropping the links from the UI was not enough: a typed or
    /// bookmarked URL still reached the old behaviour, and OnPost was reachable without ever
    /// passing through OnGet. The view is left in place but never renders.
    /// </summary>
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        /// <summary>Kept only because the (never-rendered) view binds to it; nothing reads it.</summary>
        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost() => RedirectToPage("./Login");
    }
}
