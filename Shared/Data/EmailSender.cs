using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
//using SendGrid.Helpers.Mail;
//using SendGrid;
using brevo_csharp.Client;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;



namespace AliantWebS.Data
{
    public class EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor,

    ILogger<EmailSender> logger, IConfiguration configuration) : IEmailSender<ApplicationUser>
    {
        private readonly ILogger logger = logger;
        private readonly IConfiguration configuration = configuration;

        public AuthMessageSenderOptions Options { get; } = optionsAccessor.Value;

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            SendEmailAsync(email, "Activation de votre compte Argos",
                $"""
                <p>Nous avons le plaisir de vous informer que votre compte Argos a été créé.</p>
                <p>Pour pouvoir utiliser ce compte, il ne vous reste plus qu'à l'activer en cliquant sur
                   <a href='{confirmationLink}'>Activer mon compte</a>.
                </p>
                <p>Votre compte sera accessible dès cette activation.</p>
                """);

        /// <summary>Subject, HTML and plain-text parts of one message, composed but not yet sent.</summary>
        public sealed record MailContent(string Subject, string Html, string Text);

        /// <summary>
        /// Invitation sent when an administrator creates an account for someone.
        ///
        /// Distinct from SendConfirmationLinkAsync on purpose: the recipient did not ask for this
        /// account and has never seen the application, so the message has to say who invited them
        /// and what they are being invited to.
        ///
        /// The wording is shaped by delivery as much as by content. Strict gateways (university ones
        /// especially) silently dropped the first version, which a filter reads as credential
        /// phishing: "an account was created for you", a button naming the password, a bold expiry
        /// warning, "this link is personal, do not forward it", "automated message, do not reply".
        /// Keep all of that out. The password is chosen on the AcceptInvitation page, which explains
        /// it there; the mail only has to name the inviter, the address and the link.
        ///
        /// The plain-text part is written from the same sentences rather than derived with StripHtml,
        /// which left the multi-line button tag and its inline CSS in the text and kept an
        /// HTML-encoded ampersand that broke the link. Filters penalise a text part that disagrees
        /// with the HTML one.
        /// </summary>
        public Task SendInvitationAsync(string email, string activationLink, string? invitedBy, string? appUrl,
                                        IEnumerable<string>? roles = null, int expiryDays = 7,
                                        bool isReminder = false)
        {
            var mail = ComposeInvitation(email, activationLink, invitedBy, appUrl, roles, expiryDays, isReminder);
            return SendEmailAsync(email, mail.Subject, mail.Html, mail.Text);
        }

        /// <summary>Builds the invitation without sending it; see SendInvitationAsync for the wording rules.</summary>
        public static MailContent ComposeInvitation(string email, string activationLink, string? invitedBy,
                                                    string? appUrl, IEnumerable<string>? roles = null,
                                                    int expiryDays = 7, bool isReminder = false)
        {
            string? inviter = string.IsNullOrWhiteSpace(invitedBy) ? null : invitedBy.Trim();
            var roleList = roles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? new List<string>();

            // Each sentence is written once and rendered for both parts, so they cannot drift apart.
            // v renders a value (HTML-encoded for the HTML part, where the link's '&' must become an
            // entity; raw for the text part) and b emphasises it.
            string Intro(Func<string, string> v, Func<string, string> b) =>
                (inviter == null ? "Vous êtes invité" : $"{b(v(inviter))} vous invite")
                + $" à rejoindre {b("Argos")}, la plateforme de surveillance et d'instrumentation des structures."
                + (isReminder ? " Voici un nouveau lien pour accepter l'invitation : il remplace le précédent." : "");

            string Address(Func<string, string> v, Func<string, string> b) =>
                $"L'invitation est établie pour l'adresse {b(v(email))}."
                + (roleList.Count == 0
                    ? " Vos droits d'accès vous seront attribués par la suite."
                    : $" Droits prévus : {b(string.Join(", ", roleList.Select(v)))}.");

            string Contact(Func<string, string> v) =>
                "Pour toute question, ou pour recevoir un nouveau lien, contactez "
                + (inviter == null ? "l'administrateur qui vous a invité." : $"{v(inviter)}.");

            string validity = $"L'invitation reste valable {expiryDays} jour{(expiryDays > 1 ? "s" : "")}.";
            const string notExpected = "Si vous ne vous attendiez pas à cette invitation, ignorez simplement ce message.";

            Func<string, string> encode = s => System.Net.WebUtility.HtmlEncode(s ?? "");
            Func<string, string> strong = s => $"<strong>{s}</strong>";
            Func<string, string> plain = s => s ?? "";

            string link = encode(activationLink);
            string appUrlLine = string.IsNullOrWhiteSpace(appUrl)
                ? ""
                : $"<p style='font-size:14px;'>Argos est accessible à l'adresse <a href='{encode(appUrl)}'>{encode(appUrl)}</a>.</p>";

            string html = $@"<div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 640px;'>
    <h2 style='color:#2d3748; border-bottom: 3px solid #667eea; padding-bottom: 10px;'>Invitation à rejoindre Argos</h2>
    <p>Bonjour,</p>
    <p>{Intro(encode, strong)}</p>
    <p>{Address(encode, strong)}</p>
    <p style='text-align:center; margin:28px 0;'><a href='{link}' style='background:#667eea; color:#ffffff; text-decoration:none; font-weight:bold; padding:13px 28px; border-radius:8px; display:inline-block;'>Accepter l'invitation</a></p>
    <p style='font-size:13px; color:#718096;'>Si le bouton ne s'affiche pas, ouvrez ce lien :<br/><span style='word-break:break-all;'>{link}</span></p>
    {appUrlLine}
    <p style='font-size:14px;'>{validity}</p>
    <hr style='border:none; border-top:1px solid #e2e8f0; margin:28px 0;'/>
    <p style='font-size:13px; color:#718096;'>{Contact(encode)}<br/>{notExpected}</p>
</div>";

            var text = new List<string>
            {
                "Bonjour,", "",
                Intro(plain, plain), "",
                Address(plain, plain), "",
                "Pour accepter l'invitation, ouvrez ce lien :",
                activationLink ?? "", "",
            };
            if (!string.IsNullOrWhiteSpace(appUrl))
                text.AddRange(new[] { $"Argos est accessible à l'adresse {appUrl}.", "" });
            text.AddRange(new[] { validity, "", Contact(plain), notExpected });

            string subject = isReminder ? "Nouvelle invitation à rejoindre Argos" : "Invitation à rejoindre Argos";
            return new MailContent(subject, html, string.Join("\n", text));
        }

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            SendEmailAsync(email, "Réinitialisation de votre mot de passe Argos",
                $"""
                <p>Nous avons reçu une demande de réinitialisation de votre mot de passe.</p>
                <p>Pour réinitialiser votre mot de passe, veuillez cliquer sur le lien suivant :
                   <a href='{resetLink}'>Réinitialiser mon mot de passe</a>.
                </p>
                <p>Si vous n'êtes pas à l'origine de cette demande, vous pouvez ignorer ce message.</p>
                """);

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            SendEmailAsync(email, "Réinitialisation de votre mot de passe Argos",
                $"""
                <p>Nous avons reçu une demande de réinitialisation de votre mot de passe.</p>
                <p>Votre code de réinitialisation est le suivant : <strong>{resetCode}</strong></p>
                <p>Veuillez utiliser ce code pour réinitialiser votre mot de passe.</p>
                <p>Si vous n'êtes pas à l'origine de cette demande, vous pouvez ignorer ce message.</p>
                """);

        public Task SendEmailAsync(string toEmail, string subject, string message) =>
            SendEmailAsync(toEmail, subject, message, textMessage: null);

        /// <param name="textMessage">
        /// Plain-text part. Null derives one with StripHtml, which only copes with single-line tags and
        /// leaves entities encoded, so pass one explicitly for any message carrying a link.
        /// </param>
        public async Task SendEmailAsync(string toEmail, string subject, string message, string? textMessage)
        {
           // if (string.IsNullOrEmpty(Options.SendGridKey))
                var apiKey = configuration["SwanBrevoAPIKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("Null SwanBrevoAPIKey");
            }
            await Execute(apiKey, subject, message, toEmail, textMessage);
        }


        public async Task Execute(string brevoApiKey, string subject, string message, string toEmail,
                                  string? textMessage = null)
        {
            try
            {
               if (string.IsNullOrEmpty(brevoApiKey))
                {
                    throw new Exception("SwanBrevoAPIKey not found in configuration");
                }

                // Configure Brevo client
                var config = new brevo_csharp.Client.Configuration();
                config.AddApiKey("api-key", brevoApiKey); 

                var emailApi = new brevo_csharp.Api.TransactionalEmailsApi(config);

                // Create sender and recipient
                var emailSender = new brevo_csharp.Model.SendSmtpEmailSender(
                    name: "Cideco",
                    email: "ne-pas-repondre@instrumentation-antares.cideco.tech"
                );

                var emailRecipient = new brevo_csharp.Model.SendSmtpEmailTo(email: toEmail);

                // Create the email message
                var sendSmtpEmail = new brevo_csharp.Model.SendSmtpEmail(
                    sender: emailSender,
                    to: new List<brevo_csharp.Model.SendSmtpEmailTo> { emailRecipient },
                    subject: subject,
                    htmlContent: message,
                    textContent: textMessage ?? StripHtml(message) // derived only when no text part was written
                );

                // Send the email
                var result = await emailApi.SendTransacEmailAsync(sendSmtpEmail);
                logger.LogInformation("Email to {Email} sent successfully! Message Id: {MessageId}", toEmail, result.MessageId);
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to send email to {Email}. Error: {Error}", toEmail, ex.Message);
                throw; // Re-throw to let calling code handle the error
            }
        }

        // Helper method to strip HTML tags for plain text content
        private string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
        }




        //public async Task Execute(string apiKey, string subject, string message, string toEmail)
        //{
        //    var client = new SendGridClient(apiKey);
        //    var msg = new SendGridMessage()
        //    {
        //        From = new EmailAddress("support@swananalytics.fr", "Password Recovery"),
        //        Subject = subject,
        //        PlainTextContent = message,
        //        HtmlContent = message
        //    };
        //    msg.AddTo(new EmailAddress(toEmail));

        //    // Disable click tracking.
        //    // See https://sendgrid.com/docs/User_Guide/Settings/tracking.html
        //    msg.SetClickTracking(false, false);
        //    var response = await client.SendEmailAsync(msg);

        //    if (response.IsSuccessStatusCode)
        //    {
        //        logger.LogInformation("Email to {Email} queued successfully!", toEmail);
        //    }
        //    else
        //    {
        //        // Add detailed error logging
        //        var responseBody = await response.Body.ReadAsStringAsync();
        //        logger.LogError("Failed to send email to {Email}. Status: {StatusCode}, Response: {Response}",
        //            toEmail, response.StatusCode, responseBody);
        //    }
        //}

    }
}
