using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AliantWebS.Data
{
    /// <summary>
    /// Options for the invitation token, kept separate from Identity's shared
    /// DataProtectionTokenProviderOptions so the invitation can live for a week without also
    /// giving every forgot-password link a week of validity.
    ///
    /// An invitation sits in an inbox: it has to survive a weekend, a holiday, or a colleague who
    /// only reads mail on Mondays. Identity's 24-hour default failed those with a generic
    /// "Error confirming your email" and no way back other than deleting and recreating the user.
    /// </summary>
    public class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
    {
        public InvitationTokenProviderOptions()
        {
            Name = "InvitationTokenProvider";
            TokenLifespan = TimeSpan.FromDays(InvitationTokens.DefaultExpiryDays);
        }
    }

    public class InvitationTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
    {
        public InvitationTokenProvider(IDataProtectionProvider dataProtectionProvider,
                                       IOptions<InvitationTokenProviderOptions> options,
                                       ILogger<DataProtectorTokenProvider<TUser>> logger)
            : base(dataProtectionProvider, options, logger)
        {
        }
    }

    /// <summary>Names shared by the generating (UserManager page) and redeeming (AcceptInvitation) sides.</summary>
    public static class InvitationTokens
    {
        /// <summary>Provider name registered on the IdentityBuilder.</summary>
        public const string ProviderName = "Invitation";

        /// <summary>Purpose string — must match exactly between Generate and Verify.</summary>
        public const string Purpose = "invite-accept";

        public const int DefaultExpiryDays = 7;

        /// <summary>Configured lifetime in days ("InvitationExpiryDays"), for display in the mail and the page.</summary>
        public static int ExpiryDays(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            var raw = configuration?["InvitationExpiryDays"];
            return int.TryParse(raw, out var days) && days > 0 ? days : DefaultExpiryDays;
        }
    }
}
