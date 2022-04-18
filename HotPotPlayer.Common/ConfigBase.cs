﻿using HotPotPlayer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotPotPlayer
{
    public abstract class ConfigBase
    {
        public abstract string CacheFolder { get; }
        public abstract string LocalFolder { get; }
        public abstract string DatabaseFolder { get; }

        public abstract List<LibraryItem> MusicLibrary { get; set; }
        public abstract List<LibraryItem> VideoLibrary { get; set; }

        private List<LibraryItem> _musicPlayList;
        public List<LibraryItem> MusicPlayList
        {
            get
            {
                _musicPlayList ??= MusicLibrary.Select(m =>
                {
                    var p = Path.Combine(m.Path, "Playlists");
                    return new LibraryItem { Path = p, IsSystemLibrary = m.IsSystemLibrary };
                }).ToList();
                return _musicPlayList;
            }
        }

        private JObject _settings;
        private JObject Settings
        {
            get
            {
                if (_settings == null)
                {
                    var configDir = Path.Combine(LocalFolder, "Config");
                    if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                    var configFile = Path.Combine(configDir, "Settings.json");
                    if (File.Exists(configFile))
                    {
                        var json = File.ReadAllText(configFile);
                        _settings = JObject.Parse(json);
                    }
                    else
                    {
                        _settings = new JObject();
                    }
                }
                return _settings;
            }
        }

        public void ResetSettings()
        {
            _settings = new JObject();
        }

        public void SaveSettings()
        {
            exitSaveActions?.ForEach(a => a?.Invoke());

            var configDir = Path.Combine(LocalFolder, "Config");
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            var configFile = Path.Combine(configDir, "Settings.json");
            var json = Settings.ToString(Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(configFile, json);
        }

        public T GetConfig<T>(string key)
        {
            var r = Settings.TryGetValue(key, out var value);
            if (r)
            {
                return value.ToObject<T>();
            }
            return default;
        }

        public T[] GetConfigArray<T>(string key)
        {
            var r = Settings.TryGetValue(key, out var value);
            if (r)
            {
                return ((JArray)value).ToObject<T[]>();
            }
            return null;
        }

        public void SetConfig<T>(string key, T value)
        {
            Settings[key] = JToken.FromObject(value);
        }

        public void SetConfigArray<T>(string key, T[] value)
        {
            Settings[key] = new JArray(value);
        }

        readonly List<Action> exitSaveActions = new();

        public void SaveConfigWhenExit<T>(string key, Func<T> value)
        {
            exitSaveActions.Add(() =>
            {
                var v = value.Invoke();
                SetConfig(key, v);
            });
        }

        public void SaveConfigWhenExit<T>(string key, Func<(bool, T)> value)
        {
            exitSaveActions.Add(() =>
            {
                var (shouldSave, v) = value.Invoke();
                if (shouldSave)
                {
                    SetConfig(key, v);
                }
            });
        }
    }
}