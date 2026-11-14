namespace Extatic.Api.Notifications;

/// <summary>Bound from the "Email" configuration section.</summary>
public class EmailOptions
{
    public string FromAddress { get; set; } = "no-reply@extatic.local";
    public string FromName { get; set; } = "Extatic";
    public SmtpOptions Smtp { get; set; } = new();

    public class SmtpOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseStartTls { get; set; } = true;
    }
}
