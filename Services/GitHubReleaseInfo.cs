using Newtonsoft.Json;
using System.Collections.Generic;

namespace UltimateKtv.Services
{
    public class GitHubReleaseInfo
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
    }

    public class GitHubReleaseAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
