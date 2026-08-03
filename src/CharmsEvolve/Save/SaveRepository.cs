using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace CharmsEvolve.Save
{
    internal sealed class SaveRepository
    {
        private readonly string _directory;

        public SaveRepository()
        {
            _directory = Path.Combine(Paths.ConfigPath, "CharmsEvolve");
            Directory.CreateDirectory(_directory);
        }

        public CharmSaveData Load(int slot)
        {
            string path = GetPath(slot);
            if (!File.Exists(path))
                return new CharmSaveData();

            try
            {
                string json = File.ReadAllText(path);
                CharmSaveData data = JsonUtility.FromJson<CharmSaveData>(json);
                return data ?? new CharmSaveData();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to load CharmsEvolve save: " + ex);
                return new CharmSaveData();
            }
        }

        public void Save(int slot, CharmSaveData data)
        {
            if (slot < 0 || data == null)
                return;

            string path = GetPath(slot);
            string temp = path + ".tmp";

            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(temp, json);

                if (File.Exists(path))
                {
                    string backup = path + ".bak";
                    File.Copy(path, backup, true);
                    File.Delete(path);
                }

                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to save CharmsEvolve data: " + ex);
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // ignored
                }
            }
        }

        private string GetPath(int slot)
        {
            return Path.Combine(_directory, "slot-" + slot + ".json");
        }
    }
}
