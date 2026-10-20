using System.Net;
using System.Net.Mail;

namespace BackEnd.Services;

// Sends email through SMTP (e.g. Gmail) when "Email:SmtpHost" is configured.
// Otherwise each email is saved as an .eml file in the pickup folder so the flow can be demoed offline.
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration configuration, IWebHostEnvironment environment, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var settings = _configuration.GetSection("Email");
        var from = settings["From"];
        if (string.IsNullOrWhiteSpace(from)) from = "no-reply@funewsmanagement.org";

        using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient();

        var host = settings["SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            var folder = Path.Combine(_environment.ContentRootPath, settings["PickupDirectory"] ?? "EmailPickup");
            Directory.CreateDirectory(folder);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = folder;
            _logger.LogInformation("SMTP is not configured. Email \"{Subject}\" to {To} was saved in {Folder}", subject, to, folder);
        }
        else
        {
            client.Host = host;
            client.Port = settings.GetValue("SmtpPort", 587);
            client.EnableSsl = settings.GetValue("EnableSsl", true);
            client.Credentials = new NetworkCredential(settings["UserName"], settings["Password"]);
        }

        await client.SendMailAsync(message);
    }
}
