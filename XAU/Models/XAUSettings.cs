public class XAUSettings
{
    public const string DefaultDiscordApplicationId = "1374776817669460038";
    public string? SettingsVersion { get; set; }
    public string? ToolVersion { get; set; }
    public bool UnlockAllEnabled { get; set; }
    public bool AutoSpooferEnabled { get; set; }
    public bool AutoLaunchXboxAppEnabled { get; set; }
    public bool LaunchHidden { get; set; }
    public bool FakeSignatureEnabled { get; set; }
    public bool RegionOverride { get; set; }
    public bool UseAcrylic { get; set; }
    public bool PrivacyMode { get; set; }
    public bool OAuthLogin { get; set; }
    public bool AutoGrabEventsToken { get; set; }
    public bool DiscordPresenceEnabled { get; set; } = true;
    public string? DiscordApplicationId { get; set; } = DefaultDiscordApplicationId;
    public string? CachedEventsToken { get; set; }
    public DateTime? EventsTokenObtainedAt { get; set; }
    public string? EventsUserHash { get; set; }
}
