using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    private static Settings _currentSettings;
    public static Settings CurrentSettings
    {
        get => _currentSettings;

        set
        {
            _currentSettings = value;

            // Using the Loaded paremeter avoids null checking every single time a setting is read
            Loaded = _currentSettings != null;
        }
    }

    public static Action OnSettingsUpdated;
    public static Action OnSettingsReset;

    public static bool Loaded { get; private set; }

    private const string settingsFile = "UserSettings.json";

    private bool saving;


    private async Task WriteFileAsync(string text, string path)
    {
        await File.WriteAllTextAsync(path, text);
    }


#if !UNITY_WEBGL || UNITY_EDITOR
    private IEnumerator SaveSettingsCoroutine()
    {
        saving = true;

        string filePath = Path.Combine(Application.persistentDataPath, settingsFile);
        //Need to use newtonsoft otherwise dictionaries don't serialize
        string json = JsonConvert.SerializeObject(CurrentSettings);

        Task writeTask = WriteFileAsync(json, filePath);
        yield return new WaitUntil(() => writeTask.IsCompleted);

        if(writeTask.Exception != null)
        {
            Debug.Log($"Failed to save settings with error: {writeTask.Exception.Message}, {writeTask.Exception.StackTrace}");
            ErrorHandler.Instance?.ShowPopup(ErrorType.Error, "Failed to save your settings!");
        }

        saving = false;
    }


    public void SaveSettings()
    {
        if(saving)
        {
            Debug.Log("Trying to save settings when already saving!");
            return;
        }

        StartCoroutine(SaveSettingsCoroutine());
    }


    private void LoadSettings()
    {
        string filePath = Path.Combine(Application.persistentDataPath, settingsFile);
        
        if(!File.Exists(filePath))
        {
            Debug.Log("Settings file doesn't exist. Using defaults.");
            CurrentSettings = Settings.GetDefaultSettings();
            SaveSettings();

            OnSettingsUpdated?.Invoke();
            return;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            CurrentSettings = JsonConvert.DeserializeObject<Settings>(json);
        }
        catch(Exception err)
        {
            ErrorHandler.Instance?.ShowPopup(ErrorType.Error, "Failed to load settings! Reverting to default.");
            Debug.LogWarning($"Failed to load settings with error: {err.Message}, {err.StackTrace}");

            CurrentSettings = Settings.GetDefaultSettings();
            SaveSettings();
        }

        OnSettingsUpdated?.Invoke();
    }
#endif


    public static bool GetBool(string name)
    {
        bool value;
#if UNITY_WEBGL && !UNITY_EDITOR
        //Use player prefs for WebGL
        //Use ints for bools since PlayerPrefs can't store them
        int defaultValue = 0;
        if(Settings.DefaultSettings.Bools.TryGetValue(name, out value))
        {
            defaultValue = value ? 1 : 0;
        }

        return PlayerPrefs.GetInt(name, defaultValue) > 0;
#else
        if(!Loaded)
        {
            Debug.LogWarning($"Setting {name} was accessed before settings loaded!");
            return false;
        }

        if(CurrentSettings.Bools.TryGetValue(name, out value))
        {
            return value;
        }
        else if(Settings.DefaultSettings.Bools.TryGetValue(name, out value))
        {
            return value;
        }
        else return false;
#endif
    }


    public static int GetInt(string name)
    {
        int value;
#if UNITY_WEBGL && !UNITY_EDITOR
        //Use player prefs for WebGL
        int defaultValue = 0;
        if(Settings.DefaultSettings.Ints.TryGetValue(name, out value))
        {
            defaultValue = value;
        }

        return PlayerPrefs.GetInt(name, defaultValue);
#else
        if(!Loaded)
        {
            Debug.LogWarning($"Setting {name} was accessed before settings loaded!");
            return 0;
        }

        if(CurrentSettings.Ints.TryGetValue(name, out value))
        {
            return value;
        }
        else if(Settings.DefaultSettings.Ints.TryGetValue(name, out value))
        {
            return value;
        }
        else return 0;
#endif
    }


    public static float GetFloat(string name)
    {
        float value;
#if UNITY_WEBGL && !UNITY_EDITOR
        //Use player prefs for WebGL
        float defaultValue = 0;
        if(Settings.DefaultSettings.Floats.TryGetValue(name, out value))
        {
            defaultValue = value;
        }

        return PlayerPrefs.GetFloat(name, defaultValue);
#else
        if(!Loaded)
        {
            Debug.LogWarning($"Setting {name} was accessed before settings loaded!");
            return 0;
        }

        if(CurrentSettings.Floats.TryGetValue(name, out value))
        {
            return value;
        }
        else if(Settings.DefaultSettings.Floats.TryGetValue(name, out value))
        {
            return value;
        }
        else return 0;
#endif
    }


    public static void SetRule(string name, bool value)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.SetInt(name, value ? 1 : 0);
#else
        Dictionary<string, bool> rules = CurrentSettings.Bools;
        if(rules.ContainsKey(name))
        {
            rules[name] = value;
        }
        else
        {
            rules.Add(name, value);
        }
#endif
        OnSettingsUpdated?.Invoke();
    }


    public static void SetRule(string name, int value)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.SetInt(name, value);
#else
        Dictionary<string, int> rules = CurrentSettings.Ints;
        if(rules.ContainsKey(name))
        {
            rules[name] = value;
        }
        else
        {
            rules.Add(name, value);
        }
#endif
        OnSettingsUpdated?.Invoke();
    }


    public static void SetRule(string name, float value)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.SetFloat(name, value);
#else
        value = (float)Math.Round(value, 3); //Why tf does the compiler read value as a double here?

        Dictionary<string, float> rules = CurrentSettings.Floats;
        if(rules.ContainsKey(name))
        {
            rules[name] = value;
        }
        else
        {
            rules.Add(name, value);
        }
#endif
        OnSettingsUpdated?.Invoke();
    }


    public static void SetDefaults()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PlayerPrefs.DeleteAll();
#else
        CurrentSettings = Settings.GetDefaultSettings();
#endif
        
        OnSettingsReset?.Invoke();
        OnSettingsUpdated?.Invoke();
    }


    private void Awake()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        //Load settings from json if not running in WebGL
        //Otherwise settings are handled through playerprefs instead
        LoadSettings();
#else
        OnSettingsUpdated?.Invoke();
#endif
    }
}


[Serializable]
public class Settings
{
    public Dictionary<string, bool> Bools;
    public Dictionary<string, int> Ints;
    public Dictionary<string, float> Floats;


    public static readonly Settings DefaultSettings = new Settings
    {
        Bools = new Dictionary<string, bool>
        {
            {"randomhitsoundpitch", false},
            {"spatialhitsounds", false},
            {"simplenotes", false},
            {"moveanimations", true},
            {"rotateanimations", true},
            {"flipanimations", true},
            {"arcfadeanimation", true},
            {"arctextureanimation", true},
            {"vsync", true},
            {"ssao", true},
            {"dynamicsoundpriority", true},
            {"concurrentloading", true}
        },

        Ints = new Dictionary<string, int>
        {
            {"hitsound", 0},
            {"arcdensity", 60},
            {"camerafov", 80},
            {"cameratilt", 0},
            {"framecap", 60},
            {"antialiasing", 0},
            {"cachesize", 3}
        },

        Floats = new Dictionary<string, float>
        {
            {"musicvolume", 0.5f},
            {"hitsoundvolume", 0.5f},
            {"uiscale", 1f},
            {"chainvolume", 0.8f},
            {"wallopacity", 0.5f},
            {"arcbrightness", 1f},
            {"arcwidth", 0.5f},
            {"cameraposition", -2},
            {"bloom", 1},
            {"backgroundbloom", 1},
            {"hitsoundbuffer", 0.2f}
        }
    };


    public static Settings GetDefaultSettings()
    {
        //Provides a deep copy of the default settings I hate reference types I hate reference types I hate reference types I hate reference types I hate reference types
        Settings settings = new Settings
        {
            Bools = new Dictionary<string, bool>(),
            Ints = new Dictionary<string, int>(),
            Floats = new Dictionary<string, float>()
        };

        foreach(var key in DefaultSettings.Bools)
        {
            settings.Bools.Add(key.Key, key.Value);
        }

        foreach(var key in DefaultSettings.Ints)
        {
            settings.Ints.Add(key.Key, key.Value);
        }

        foreach(var key in DefaultSettings.Floats)
        {
            settings.Floats.Add(key.Key, key.Value);
        }

        return settings;
    }
}