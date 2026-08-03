using BepInEx;
using CharmsEvolve.Api;
using UnityEngine;

namespace CharmsEvolveTextureExample
{
    [BepInPlugin(
        "com.hollowknight.charmsevolve.textureexample",
        "Charms Evolve Texture Example",
        "1.0.0")]
    [BepInDependency("com.hollowknight.charmsevolve")]
    public sealed class TextureExamplePlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            string path = System.IO.Path.Combine(
                Paths.PluginPath,
                "MyCharmTextures",
                "X-01.png");

            if (!CharmsEvolveApi.RegisterTextureFromPng("X-01", path))
                Logger.LogWarning("Texture not found: " + path);
        }
    }
}
