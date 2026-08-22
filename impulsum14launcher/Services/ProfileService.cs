using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImpulsumLauncher14.Services;

public class ProfileService
{
    public const long MaxCoins = int.MaxValue;

    private static readonly string ProfilePath = Path.Combine(
        AppContext.BaseDirectory, "Server", "Profile", "fut_profile.json");

    public bool TryGet(out string personaName, out long coins)
    {
        personaName = "FUT14";
        coins = 0;
        try
        {
            if (!File.Exists(ProfilePath)) return false;
            var node = JsonNode.Parse(File.ReadAllText(ProfilePath));
            if (node == null) return false;
            personaName = node["PersonaName"]?.GetValue<string>() ?? "FUT14";
            coins = Math.Clamp(node["Coins"]?.GetValue<long>() ?? 0, 0, MaxCoins);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryUpdate(string personaName, long coins)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(personaName)) personaName = personaName.Trim();
            else personaName = "FUT14";
            coins = Math.Clamp(coins, 0, MaxCoins);

            JsonNode? node;
            if (File.Exists(ProfilePath))
            {
                node = JsonNode.Parse(File.ReadAllText(ProfilePath));
            }
            else
            {
                node = new JsonObject();
            }

            if (node == null) return false;
            node["PersonaName"] = personaName;
            node["Coins"] = coins;

            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            File.WriteAllText(ProfilePath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }
}