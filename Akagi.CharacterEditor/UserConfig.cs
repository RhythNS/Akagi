using System.IO;
using System.Text.Json;

namespace Akagi.CharacterEditor;

/// <summary>
/// Stores user preferences and settings
/// </summary>
public class UserConfig
{
    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Akagi",
        "CharacterEditor",
        "userconfig.json"
    );

    public bool SnapToGrid { get; set; } = true;
    
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>
    /// Loads the user config from disk, or creates a default one if it doesn't exist
    /// </summary>
    public static UserConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                UserConfig? config = JsonSerializer.Deserialize<UserConfig>(json);
                return config ?? new UserConfig();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user config: {ex.Message}");
        }

        return new UserConfig();
    }

    /// <summary>
    /// Saves the user config to disk
    /// </summary>
    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(ConfigFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save user config: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a file to the recent files list, maintaining a maximum of 5 items
    /// </summary>
    public void AddRecentFile(string filePath)
    {
        // Remove if already exists
        RecentFiles.Remove(filePath);
        
        // Add to the beginning
        RecentFiles.Insert(0, filePath);
        
        // Keep only the 5 most recent
        if (RecentFiles.Count > 5)
        {
            RecentFiles = [.. RecentFiles.Take(5)];
        }
        
        Save();
    }

    /// <summary>
    /// Removes a file from the recent files list
    /// </summary>
    public void RemoveRecentFile(string filePath)
    {
        if (RecentFiles.Remove(filePath))
        {
            Save();
        }
    }

    /// <summary>
    /// Gets the recent files list, filtering out files that no longer exist
    /// </summary>
    public List<string> GetValidRecentFiles()
    {
        List<string> validFiles = [.. RecentFiles.Where(File.Exists)];
        
        // Update the list if any files were invalid
        if (validFiles.Count != RecentFiles.Count)
        {
            RecentFiles = validFiles;
            Save();
        }
        
        return validFiles;
    }
}
