// Accepts an administrator's invitation: one link that sets the first password AND confirms the
// address, replacing the old two-step "activate, then use forgot-password to get a password".
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
using AliantWebS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace AliantWebS.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class AcceptInvitationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly JournalService _journal;

        public AcceptInvitationModel(UserManager<ApplicationUser> userManager,
                                     SignInManager<ApplicationUser> signInManager,
                                     JournalService journal)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _journal = journal;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public bool TokenValid { get; set; }
        public string Email { get; set; }
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            public string UserId { get; set; }

            [Required]
            public string Code { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "Le mot de passe doit contenir au moins {2} caractères.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Mot de passe")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirmation")]
            [Compare("Password", ErrorMessage = "Le mot de passe et sa confirmation ne correspondent pas.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                ErrorMessage = "Le lien est incomplet.";
                return Page();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                // Deliberately vague: this page is anonymous, so it must not confirm whether an
                // account exists for a guessed id.
                ErrorMessage = "Le lien n'est pas valable.";
                return Page();
            }

            string token = Decode(code);
            if (token == null || !await _userManager.VerifyUserTokenAsync(
                    user, InvitationTokens.ProviderName, InvitationTokens.Purpose, token))
            {
                ErrorMessage = "Ce lien a expiré, a déjà servi, ou a été remplacé par un lien plus récent.";
                return Page();
            }

            TokenValid = true;
            Email = user.Email;
            Input = new InputModel { UserId = userId, Code = code };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = Input?.UserId == null ? null : await _userManager.FindByIdAsync(Input.UserId);
            if (user == null)
            {
                ErrorMessage = "Le lien n'est pas valable.";
                return Page();
            }

            Email = user.Email;

            // Re-verified on POST: the GET check proves nothing about this request, and the token
            // could have expired or been redeemed in the meantime.
            string token = Decode(Input.Code);
            if (token == null || !await _userManager.VerifyUserTokenAsync(
                    user, InvitationTokens.ProviderName, InvitationTokens.Purpose, token))
            {
                ErrorMessage = "Ce lien a expiré, a déjà servi, ou a été remplacé par un lien plus récent.";
                return Page();
            }

            TokenValid = true;

            if (!ModelState.IsValid) return Page();

            // An invited account has no password yet; a re-invited one might. Setting the password
            // rotates the security stamp, which invalidates this invitation link — single use.
            if (await _userManager.HasPasswordAsync(user))
                await _userManager.RemovePasswordAsync(user);

            var result = await _userManager.AddPasswordAsync(user, Input.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return Page();
            }

            // Redeeming a token that was only ever sent to that mailbox proves control of it, so the
            // address is confirmed here. Without this, SignIn.RequireConfirmedAccount blocks login.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            await _journal.WriteAsAsync(user.UserName, user.Email, JournalCategory.Session,
                "user.invite.accepted", details: "Mot de passe défini et adresse confirmée");

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(Url.Content("~/SurveillanceAntares"));
        }

        private static string Decode(string code)
        {
            try
            {
                return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch
            {
                return null;   // mangled link (a mail client broke the URL)
            }
        }
    }
}
