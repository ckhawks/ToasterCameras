using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace ToasterCameras;

public class CameraPosition
{
    public string name { get; set; } = "";
    public float[] position { get; set; } = new float[3];
    public float[] rotation { get; set; } = new float[3];
    public string keybind { get; set; } = "";

    public Vector3 GetPosition() => new Vector3(position[0], position[1], position[2]);
    public Vector3 GetRotation() => new Vector3(rotation[0], rotation[1], rotation[2]);
}

public class CameraModeKeybinds
{
    public string becomePuck { get; set; } = "";
    public string watchPuck { get; set; } = "";
    public string watchPuckAbove { get; set; } = "";
    public string watchPuckSmart { get; set; } = "";
    public string watchPuckSmart2 { get; set; } = "";
    public string watchOff { get; set; } = "";
    public string cinematicSmoothing { get; set; } = "<keyboard>/f9";
    public string slowDown { get; set; } = "<keyboard>/leftAlt";
}

public class CinematicSettings
{
    public float rotationSmoothingFactor { get; set; } = 0.1f;
    public float positionSmoothingFactor { get; set; } = 0.1f;
}

// Player order goes C->LW->RW->LD->RD->G, but if one of those is missing they shift down. 6 always selects the G?
public class PlayerKeybinds
{
    public string player1 { get; set; } = "";
    public string player2 { get; set; } = "";
    public string player3 { get; set; } = "";
    public string player4 { get; set; } = "";
    public string player5 { get; set; } = "";
    public string player6 { get; set; } = "";
}

public class TeamKeybinds
{
    public PlayerKeybinds blue { get; set; } = new PlayerKeybinds
    {
        player1 = "<Keyboard>/1",
        player2 = "<Keyboard>/2",
        player3 = "<Keyboard>/3",
        player4 = "<Keyboard>/4",
        player5 = "<Keyboard>/5",
        player6 = "<Keyboard>/6"
    };
    
    public PlayerKeybinds red { get; set; } = new PlayerKeybinds
    {
        player1 = "<Keyboard>/7",
        player2 = "<Keyboard>/8",
        player3 = "<Keyboard>/9",
        player4 = "<Keyboard>/0",
        player5 = "<Keyboard>/minus",
        player6 = "<Keyboard>/equals"
    };
}

public class ModSettings
{
    public Dictionary<string, CameraPosition> cameraPositions { get; set; } = GetDefaultCameraPositions();
    public CameraModeKeybinds cameraModes { get; set; } = new CameraModeKeybinds();
    public TeamKeybinds watchPlayer { get; set; } = new TeamKeybinds();
    public CinematicSettings cinematicSettings { get; set; } = new CinematicSettings();
    public bool disableQuickChatsInSpectator { get; set; } = true;
    
    static string ConfigurationFileName = $"{Plugin.MOD_NAME}.json";

    public static Dictionary<string, CameraPosition> GetDefaultCameraPositions()
    {
        return new Dictionary<string, CameraPosition>()
        {
            { "f", new CameraPosition { 
                name = "Faceoff Normal",
                position = new float[] { -7.5f, 4, 0 }, 
                rotation = new float[] { 35, 90, 0 },
                keybind = "<Keyboard>/f"
            }},
            { "fc", new CameraPosition { 
                name = "Faceoff Close",
                position = new float[] { -4, 0.5f, 0 }, 
                rotation = new float[] { -20, 90, 0 },
                keybind = ""
            }},
            { "fa", new CameraPosition { 
                name = "Faceoff Above",
                position = new float[] { 0, 4, 0 }, 
                rotation = new float[] { 90, 90, 0 },
                keybind = ""
            }},
            { "r", new CameraPosition { 
                name = "Right Middle",
                position = new float[] { 21, 3, 0 }, 
                rotation = new float[] { 0, 270, 0 },
                keybind = ""
            }},
            { "l", new CameraPosition { 
                name = "Left Middle",
                position = new float[] { -21, 3, 0 }, 
                rotation = new float[] { 0, -270, 0 },
                keybind = ""
            }},
            // blue team
            { "bg", new CameraPosition { 
                name = "Blue Goal",
                position = new float[] { 0, 0.5f, 41.1f }, 
                rotation = new float[] { -15, 180, 0 },
                keybind = ""
            }},
            { "bge", new CameraPosition { 
                name = "Blue Goal Elevated",
                position = new float[] { 0, 6f, 45.6f }, 
                rotation = new float[] { 26.5f, 180, 0 },
                keybind = ""
            }},
            { "brg", new CameraPosition { 
                name = "Blue Right Glass",
                position = new float[] { -21.2f, 2.3f, 40f }, 
                rotation = new float[] { 3, 120, 0 },
                keybind = ""
            }},
            { "blg", new CameraPosition { 
                name = "Blue Left Glass",
                position = new float[] { 21.2f, 2.3f, 40f }, 
                rotation = new float[] { 3, 240, 0 },
                keybind = ""
            }},
            { "bz", new CameraPosition { 
                name = "Blue Zone",
                position = new float[] { 0.2f, 9.5f, 0.7f }, 
                rotation = new float[] { 25, 0, 0 },
                keybind = ""
            }},
            { "br", new CameraPosition { 
                name = "Blue Right",
                position = new float[] { -10f, 7.5f, 44.5f }, 
                rotation = new float[] { 25, 150, 0 },
                keybind = ""
            }},
            { "bl", new CameraPosition { 
                name = "Blue Left",
                position = new float[] { 10f, 7.5f, 44.5f }, 
                rotation = new float[] { 25, 210, 0 },
                keybind = ""
            }},
            // red team
            { "rg", new CameraPosition { 
                name = "Red Goal",
                position = new float[] { 0, 0.5f, -41.1f }, 
                rotation = new float[] { -15, 0, 0 },
                keybind = ""
            }},
            { "rge", new CameraPosition { 
                name = "Red Goal Elevated",
                position = new float[] { 0, 6f, -45.6f }, 
                rotation = new float[] { 26.5f, 0, 0 },
                keybind = ""
            }},
            { "rrg", new CameraPosition { 
                name = "Red Right Glass",
                position = new float[] { 21.2f, 2.3f, -40f }, 
                rotation = new float[] { 3, 300, 0 },
                keybind = ""
            }},
            { "rlg", new CameraPosition { 
                name = "Red Left Glass",
                position = new float[] { -21.2f, 2.3f, -40f }, 
                rotation = new float[] { 3, 60, 0 },
                keybind = ""
            }},
            { "rz", new CameraPosition { 
                name = "Red Zone",
                position = new float[] { 0.2f, 9.5f, -0.7f }, 
                rotation = new float[] { 25, 180, 0 },
                keybind = ""
            }},
            { "rr", new CameraPosition { 
                name = "Red Right",
                position = new float[] { 10f, 7.5f, -44.5f }, 
                rotation = new float[] { 25, 330, 0 },
                keybind = ""
            }},
            { "rl", new CameraPosition { 
                name = "Red Left",
                position = new float[] { -10f, 7.5f, -44.5f }, 
                rotation = new float[] { 25, 30, 0 },
                keybind = ""
            }},
        };
    }

    public static ModSettings Load()
    {
        Plugin.Log($"Loading {ConfigurationFileName}...");
        var path = GetConfigPath();
        var dir = Path.GetDirectoryName(path);

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            Plugin.Log($"Created missing /config directory");
        }
        
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<ModSettings>(json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                return settings ?? new ModSettings();
            }
            catch (JsonException je)
            {
                Plugin.Log($"Corrupt config JSON, using defaults: {je.Message}");
                return new ModSettings();
            }
        }
        
        var defaults = new ModSettings();
        File.WriteAllText(path,
            JsonSerializer.Serialize(defaults, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
                
        Plugin.Log($"Config file `{path}` did not exist, created with defaults.");
        return defaults;
    }

    public void Save()
    {
        Plugin.Log($"Saving {ConfigurationFileName}...");
        var path = GetConfigPath();
        var dir  = Path.GetDirectoryName(path);

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    public static string GetConfigPath()
    {
        string rootPath = Path.GetFullPath(".");
        string configPath = Path.Combine(rootPath, "config", ConfigurationFileName);
        return configPath;
    }
}